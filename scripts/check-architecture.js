#!/usr/bin/env node
/**
 * Fodinae merged architecture linter.
 *
 * Single Node.js entry point that used to be three separate scripts:
 *
 *   1. scripts/check-forbidden-patterns.sh   — 43 grep-style architecture,
 *      settings and performance pattern rules against production C# files.
 *   2. scripts/check_di_lifecycle.py         — deep semantic DI and lifecycle
 *      analyzer (execution-order contracts, Configure reentrancy, Unity
 *      namespace syntax, unguarded [Inject] access in early lifecycle,
 *      async void in MonoBehaviours).
 *   3. scripts/check_settings_wiring.py      — settings wiring analyzer (dead
 *      ClientConfig fields + startup application contract for config
 *      consumers + UI-only reads flagged as dead wiring).
 *   4. Assets/Editor/Tools/lint-uss.py        — USS stylesheet validator
 *      (UI Toolkit property/function/easing allowlists from the UIElements
 *      registry, custom token usage, brace balance).
 *   5. Localization linter (no predecessor)   — language-file parity, used
 *      keys must exist in every language, placeholder sanity ({0},{1},...),
 *      dead keys and the unwired-dictionary check.
 *
 * Usage:
 *   node scripts/check-architecture.js [files...]
 *
 * With no arguments it scans Assets/Scripts and Assets/Editor for *.cs files
 * and always validates Assets/Resources/Styles/*.uss. With arguments it scans
 * only the given files (pattern rules only; the DI, settings-wiring and USS
 * parts always analyze the full tree, as the originals did).
 * Exits 1 on any violation, 0 otherwise.
 */

"use strict";

const fs = require("fs");
const path = require("path");

const RED = "\x1b[0;31m";
const GREEN = "\x1b[0;32m";
const YELLOW = "\x1b[1;33m";
const CYAN = "\x1b[0;36m";
const BOLD = "\x1b[1m";
const NC = "\x1b[0m";

const violations = [];

function recordViolation(category, loc, message) {
    violations.push({ category, loc, message });
}

function escapeRegExp(s) {
    return s.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

// ---------------------------------------------------------------------------
// File discovery
// ---------------------------------------------------------------------------

const EXCLUDE_REGEX = /^(Assets\/Scripts\/VContainer\/|Assets\/Plugins\/|Packages\/|Library\/)/;

function walkCs(root, result = []) {
    let entries;
    try {
        entries = fs.readdirSync(root, { withFileTypes: true });
    } catch {
        return result;
    }
    for (const entry of entries) {
        const full = path.join(root, entry.name);
        if (entry.isDirectory()) {
            walkCs(full, result);
        } else if (entry.isFile() && entry.name.endsWith(".cs")) {
            result.push(full);
        }
    }
    return result;
}

function collectProductionFiles() {
    const files = [];
    for (const root of ["Assets/Scripts", "Assets/Editor"]) {
        walkCs(root, files);
    }
    return files;
}

function readFile(filePath) {
    try {
        return fs.readFileSync(filePath, "utf8");
    } catch {
        return null;
    }
}

const SCRIPT_CLASS_BY_GUID = new Map();
let scriptClassIndexBuilt = false;

function buildScriptClassIndex() {
    if (scriptClassIndexBuilt) {
        return;
    }
    scriptClassIndexBuilt = true;

    for (const filePath of walkCs("Assets/Scripts")) {
        const meta = readFile(filePath + ".meta");
        const source = readFile(filePath);
        if (meta === null || source === null) {
            continue;
        }
        const guid = meta.match(/^guid:\s*([a-f0-9]+)\s*$/m)?.[1];
        const className = source.match(/\bclass\s+([A-Za-z0-9_]+)/)?.[1];
        if (guid && className) {
            SCRIPT_CLASS_BY_GUID.set(guid, className);
        }
    }
}

// ---------------------------------------------------------------------------
// Part 1: architectural pattern rules
// (ported from scripts/check-forbidden-patterns.sh)
// ---------------------------------------------------------------------------

const COMMENT_LINE_REGEX = /^\s*(?:\/\/|\/\*|\*|\/\/\/)/;

// Each rule: { pattern, name, allow (path exemption, nullable), allowContent (line exemption, nullable) }.
// "allow" and "allowContent" were the ALLOW_REGEX / ALLOW_CONTENT_REGEX arrays of the shell linter.
const RULES = [
    { pattern: /\b(?:StageAsync|CommitStagedAsync|DiscardStagedAsync|RestartCurrentAsync)\b/, name: "branching/staged scene lifecycle", allow: null, allowContent: null },
    { pattern: /\b(?:ContentSceneRoot|SceneInjectionBridge|LifecycleGraph|LifecycleParticipant|WorldSessionLifecycle)\b/, name: "removed lifecycle infrastructure", allow: null, allowContent: null },
    { pattern: /Transform\?\s+managerObject|_servicesRoot\.Find\(|transform\.Find\(/, name: "runtime composition-root name lookup (use serialized typed references)", allow: /^(Assets\/Scripts\/VContainer\/|Assets\/Scripts\/Tests\/|Assets\/Scripts\/Editor\/ManagerContractMigrator\.cs|Assets\/Scripts\/(Game|Rendering|UI|World)\/)/, allowContent: null },
    { pattern: /TryResolve<|TryResolve\s*\(/, name: "DI fallback resolution (use required constructor/explicit dependency)", allow: /^(Assets\/Scripts\/Tests\/|Assets\/Scripts\/VContainer\/)/, allowContent: null },
    { pattern: /using\s+Fodinae\.UI(?:\.|;)|using\s+Fodinae\.Game\.Managers;/, name: "networking layer references presentation/game manager namespaces", allow: /^(?!Assets\/Scripts\/Networking\/)/, allowContent: null },
    { pattern: /\b(?:SceneCoordinator|ISceneCoordinator|SceneStartup|ISceneEntryPoint)\b/, name: "removed scene DI proxy", allow: null, allowContent: null },
    { pattern: /\bRegisterComponentOnNewGameObject\b/, name: "runtime fallback manager construction", allow: /^Assets\/Scripts\/VContainer\//, allowContent: null },
    { pattern: /\b(?:GlobalChatUI|InventoryView|PlayerHUDView|MinimapController|WorldMapController|PauseMenu|FloatingChatManager)\b/, name: "packet processor depends directly on UI", allow: /^(?!Assets\/Scripts\/Networking\/Processors\/)/, allowContent: null },
    { pattern: /\bFindAnyObjectByType\s*</, name: "global runtime object lookup", allow: /^(Assets\/Editor\/|Assets\/Scripts\/Editor\/|Assets\/Scripts\/VContainer\/|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /public\s+static\s+[A-Za-z0-9_<>?.]+\s+Instance\s*([({;=]|=>)/, name: "static Instance singleton", allow: null, allowContent: null },
    { pattern: /ServiceLocator/, name: "ServiceLocator access", allow: null, allowContent: null },
    { pattern: /(?:private|protected|public)\s+(?:readonly\s+)?IObjectResolver\s+_?[A-Za-z0-9_]+/, name: "IObjectResolver injected into runtime logic (use direct dependencies; resolver belongs to composition roots/factories)", allow: /^(Assets\/Scripts\/Core\/(?:BootstrapLifetimeScope|GameBootstrap|GameLifetimeScope)\.cs|Assets\/Scripts\/Core\/Lifecycle\/SceneObjectFactory\.cs)$/, allowContent: null },
    { pattern: /new\s+InputAction\(/, name: "ad-hoc InputAction", allow: null, allowContent: null },
    { pattern: /FitFieldDimensionsToAtlasBudget/, name: "fractional lighting-field fitting", allow: null, allowContent: null },
    { pattern: /Mathf\.Approximately\([^,]*CameraOrthoSize/, name: "exact camera zoom cache comparison", allow: null, allowContent: null },
    { pattern: /Camera\.main/, name: "Camera.main outside GameplayCamera", allow: /^Assets\/Scripts\/Core\/GameplayCamera\.cs$/, allowContent: null },
    { pattern: /Application\.targetFrameRate\s*=/, name: "FPS cap outside DisplayManager", allow: /^Assets\/Scripts\/Rendering\/DisplayManager\.cs$/, allowContent: null },
    { pattern: /QualitySettings\.vSyncCount\s*=/, name: "VSync ownership outside DisplayManager", allow: /^Assets\/Scripts\/Rendering\/DisplayManager\.cs$/, allowContent: null },
    { pattern: /new\s+Texture2D(Array)?\s*\(/, name: "runtime Texture2D construction outside RuntimeTextureFactory", allow: /^(Assets\/Editor\/|Assets\/Scripts\/AssetPipeline\/RuntimeTextureFactory\.cs|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /\.LoadImage\s*\(/, name: "runtime image decoding outside RuntimeTextureFactory", allow: /^(Assets\/Editor\/|Assets\/Scripts\/AssetPipeline\/RuntimeTextureFactory\.cs|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /\.styleSheets\.Add\s*\(/, name: "controller-local UI Toolkit stylesheet", allow: null, allowContent: null },
    { pattern: /new\s+Vector2\s*\([^,]+,\s*Screen\.height\s*-/, name: "manual screen-to-panel Y flip", allow: null, allowContent: null },
    { pattern: /\.style\.(width|height)\s*=[^;]*Screen\.(width|height)/, name: "UI root sized from Screen dimensions", allow: null, allowContent: null },
    { pattern: /LightingCascadeAtlasLimit\s*<=\s*256\s*\?/, name: "duplicated radiance-cascade count policy", allow: null, allowContent: /return atlasDimension <= 256 \? 3 : 4;/ },
    { pattern: /(FindAnyObjectByType|FindFirstObjectByType)<Camera>/, name: "ad-hoc gameplay camera lookup", allow: /^Assets\/Scripts\/Core\/GameplayCamera\.cs$/, allowContent: null },
    { pattern: /AddComponent<[A-Za-z0-9_]*(Manager|Service)>/, name: "manual manager/service construction", allow: null, allowContent: null },
    { pattern: /(Config|config)\.GraphicsPreset\s*=/, name: "graphics preset mutation outside ClientConfigManager", allow: /^(Assets\/Scripts\/Core\/ClientConfigManager\.cs|Assets\/Scripts\/World\/Lighting\/Lighting(ConfigHolder|Engine)\.cs)$/, allowContent: null },
    { pattern: /(Config|config)\.GraphicsQualitySettings\s*=/, name: "graphics quality snapshot mutation outside ClientConfigManager", allow: /^Assets\/Scripts\/Core\/ClientConfigManager\.cs$/, allowContent: null },
    { pattern: /QualitySettings\.antiAliasing\s*=/, name: "MSAA ownership outside LightingEngine", allow: /^Assets\/Scripts\/World\/Lighting\/LightingEngine\.cs$/, allowContent: null },
    { pattern: /QualitySettings\.SetQualityLevel\s*\(/, name: "Unity quality-level ownership outside LightingEngine", allow: /^Assets\/Scripts\/World\/Lighting\/LightingEngine\.cs$/, allowContent: null },
    { pattern: /\.renderScale\s*=/, name: "URP render-scale ownership outside LightingEngine", allow: /^Assets\/Scripts\/World\/Lighting\/LightingEngine\.cs$/, allowContent: null },
    { pattern: /PlayerPrefs\.(Set|Delete|Save)/, name: "settings persistence in PlayerPrefs", allow: /^(Assets\/Editor\/.*|Assets\/Scripts\/Networking\/Auth\/AuthTokenManager\.cs|Assets\/Scripts\/UI\/AuthGate\.cs|Assets\/Scripts\/UI\/GatewayController\.cs)$/, allowContent: null },
    { pattern: /(slider|toggle|dropdown|quality|preset)\.value\s*=/, name: "notifying UI settings refresh", allow: null, allowContent: null },
    { pattern: /ServerConfig[^;]*(Master|Sfx|Music|Ambience|Voice|Ui)Volume/, name: "audio volume in ServerConfig", allow: null, allowContent: null },
    { pattern: /_clientConfig\.Config\.[A-Za-z0-9_]+\s*=/, name: "direct ClientConfig field mutation", allow: null, allowContent: null },
    { pattern: /_clientConfig\.Save\s*\(/, name: "unowned ClientConfig persistence", allow: /^(Assets\/Scripts\/Rendering\/GraphicsSettingsController\.cs|Assets\/Scripts\/Rendering\/DisplayManager\.cs|Assets\/Scripts\/World\/Lighting\/Lighting(ConfigHolder|Engine)\.cs)$/, allowContent: null },
    { pattern: /(FindAnyObjectByType|FindFirstObjectByType|FindObjectsByType)<Canvas>/, name: "screen-space uGUI Canvas lookup", allow: null, allowContent: null },
    { pattern: /using\s+UnityEngine\.UI;/, name: "screen-space uGUI namespace", allow: null, allowContent: null },
    { pattern: /new\s+GameObject\(/, name: "runtime GameObject construction outside SceneObjectFactory", allow: /^(Assets\/Editor\/.*|Assets\/Scripts\/Editor\/.*|Assets\/Scripts\/Tests\/.*|Assets\/Scripts\/Core\/Lifecycle\/SceneObjectFactory\.cs|Assets\/Scripts\/Game\/.*)$/, allowContent: null },
    { pattern: /:\s*new\s+GameObject\(/, name: "fallback GameObject construction when DI is missing", allow: /^(Assets\/Editor\/.*|Assets\/Scripts\/Editor\/.*|Assets\/Scripts\/Tests\/.*|Assets\/Scripts\/Core\/Lifecycle\/SceneObjectFactory\.cs)$/, allowContent: null },
    { pattern: /GameObject\.Find(GameObjectWithTag|GameObjectsWithTag)?\(/, name: "global unscoped GameObject lookup", allow: /^(Assets\/Editor\/|Assets\/Scripts\/Editor\/|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /SceneManager\.LoadScene\(/, name: "synchronous scene loading outside BootstrapLifetimeScope", allow: /^Assets\/Scripts\/Tests\//, allowContent: null },
    { pattern: /FindObjects?OfType</, name: "deprecated FindObject(s)OfType call", allow: null, allowContent: null },
    { pattern: /\bInput\.(GetKey|GetKeyDown|GetKeyUp|GetButton|GetButtonDown|GetMouseButton|mousePosition|GetAxis|anyKey)\b/, name: "legacy Input Manager call (use UnityEngine.InputSystem)", allow: null, allowContent: null },
    { pattern: /\b(StartCoroutine|StopCoroutine)\s*\(/, name: "legacy MonoBehaviour coroutines (use UniTask)", allow: null, allowContent: null },
    { pattern: /\bAudioSource\b/, name: "Unity AudioSource usage (FMOD Studio is the sole audio engine)", allow: /^(Assets\/Editor\/|Assets\/Scripts\/Editor\/|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /\bDontDestroyOnLoad\s*\(/, name: "DontDestroyOnLoad outside BootstrapLifetimeScope", allow: /^(Assets\/Editor\/|Assets\/Scripts\/Core\/Bootstrap\/BootstrapLifetimeScope\.cs|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /\bScreen\.SetResolution\s*\(/, name: "Screen.SetResolution outside DisplayManager", allow: /^Assets\/Scripts\/Rendering\/DisplayManager\.cs$/, allowContent: null },
    { pattern: /\bThread\.Sleep\s*\(/, name: "blocking Thread.Sleep in gameplay/async code", allow: /^(Assets\/Editor\/|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /\bGC\.Collect\s*\(/, name: "manual GC.Collect in runtime gameplay", allow: /^(Assets\/Editor\/|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /\bCamera\.(allCameras|current)\b/, name: "unmanaged camera lookup (use explicit gameplay camera contract)", allow: null, allowContent: null },
    { pattern: /\bTime\.timeScale\s*=/, name: "unowned Time.timeScale mutation", allow: /^(Assets\/Scripts\/UI\/PauseMenu\.cs|Assets\/Scripts\/Game\/Managers\/GameManager\.cs|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /new\s+(WebClient|HttpClient)\s*\(/, name: "ad-hoc HTTP client (use ClientAssetLoader or UnityWebRequest)", allow: /^(Assets\/Editor\/|Assets\/Scripts\/Tests\/)/, allowContent: null },
    { pattern: /Shader\.WarmupAllShaders/, name: "Shader.WarmupAllShaders in URP (throws keyword space assert)", allow: null, allowContent: null },
    { pattern: /GameStartupServices/, name: "deleted GameStartupServices aggregate (inject startup dependencies directly into GameBootstrap)", allow: /^Assets\/Scripts\/Tests\//, allowContent: null },
    { pattern: /SceneScopeAuthoring|SceneContractMigration/, name: "scene auto-fixing editor tools are deleted (use the read-only ProductionSceneContractValidator)", allow: null, allowContent: null },
    { pattern: /PlayerMovementController\.(LocalPlayer|OnLocalPlayerSpawned)/, name: "static local-player access (resolve ILocalPlayerState)", allow: /^Assets\/Scripts\/Core\/Interfaces\/ILocalPlayerState\.cs$/, allowContent: null },
    { pattern: /\b(MenuStarfield|MenuSceneryController)\.Current\b/, name: "static menu-scenery access (use the MainMenuLifetimeScope serialized contract)", allow: null, allowContent: null },
    { pattern: /\b(PauseMenu\.IsMenuOpen|ChatInput\.IsFocused|ProgrammatorGrid\.IsOpen)\b/, name: "static UI state access outside the UI layer (compose IInputBlocker)", allow: /^(Assets\/Scripts\/UI\/|Assets\/Scripts\/DiagnosticRunner\.cs|Assets\/Scripts\/Core\/Bootstrap\/DiagnosticRunner\.cs)/, allowContent: null },
];

const STANDARDS_LIST = [
    "  - static 'Instance' singletons              -> use VContainer DI",
    "  - ServiceLocator                            -> constructor / DI injection",
    "  - IObjectResolver in gameplay/UI logic      -> direct constructor/field dependencies; resolver only in roots/factories",
    "  - 'new InputAction(...)'                    -> configure in InputSystem_Actions.inputactions",
    "  - legacy coroutines (StartCoroutine)        -> use UniTask / CancellationToken",
    "  - legacy Input (Input.Get*)                 -> use UnityEngine.InputSystem (Keyboard.current/Mouse.current)",
    "  - AudioSource components                    -> use FMOD Studio (IAudioSystem / AudioSystem)",
    "  - Camera.main / Camera.allCameras           -> use injected IGameplayCamera (render features may use the explicit marker)",
    "  - targetFrameRate / VSync / SetResolution   -> DisplayManager is the single owner",
    "  - runtime Texture2D construction/decoding   -> use RuntimeTextureFactory",
    "  - UI Toolkit stylesheets in controllers     -> use PanelSettings.themeUss (@import)",
    "  - screen-to-panel coordinate conversion     -> use RuntimePanelUtils.ScreenToPanel",
    "  - UI element sizing from Screen.dimensions  -> use PanelSettings & USS flex layout",
    "  - manager/service runtime creation          -> register and resolve through VContainer",
    "  - graphics preset/quality mutation          -> use ClientConfigManager",
    "  - MSAA, quality-level, URP render-scale     -> LightingEngine is the owner",
    "  - settings persistence in PlayerPrefs       -> use ClientConfigManager (client_config.json)",
    "  - UI settings notifications                 -> use SetValueWithoutNotify",
    "  - runtime GameObject construction           -> use ISceneObjectFactory",
    "  - unscoped GameObject.Find / FindWithTag    -> prohibit global scene searches (use DI or FindInOwnScene)",
    "  - synchronous SceneManager.LoadScene        -> use BootstrapLifetimeScope.TransitionAsync",
    "  - deprecated FindObject(s)OfType            -> use FindObjectsByType / FindAnyObjectByType",
    "  - Unity classes namespace syntax            -> use block namespace { } for MonoBehaviour/ScriptableObject",
];

function checkPatterns(files) {
    const cache = new Map();
    const contentOf = (file) => {
        if (!cache.has(file)) {
            cache.set(file, readFile(file));
        }
        return cache.get(file);
    };

    for (const rule of RULES) {
        for (const file of files) {
            if (rule.allow && rule.allow.test(file)) {
                continue;
            }
            const content = contentOf(file);
            if (content === null) {
                continue;
            }
            const lines = content.split("\n");
            for (let i = 0; i < lines.length; i++) {
                const line = lines[i].replace(/\r$/, "");
                if (!rule.pattern.test(line)) {
                    continue;
                }
                if (COMMENT_LINE_REGEX.test(line)) {
                    continue;
                }
                if (rule.allowContent && rule.allowContent.test(line)) {
                    continue;
                }
                violations.push({
                    category: "Architecture",
                    loc: `${file}:${i + 1}`,
                    message: `${BOLD}${rule.name}${NC}\n  File: ${BOLD}${file}:${i + 1}${NC}\n  Code: ${CYAN}${line}${NC}`,
                    kind: "pattern",
                });
            }
        }
    }
}

// ---------------------------------------------------------------------------
// Part 2: deep semantic DI and lifecycle analyzer
// (ported from scripts/check_di_lifecycle.py)
// ---------------------------------------------------------------------------

const EXECUTION_ORDER_CONTRACTS = {
    "Assets/Scripts/Core/BootstrapLifetimeScope.cs": -30000,
    "Assets/Scripts/Core/GameLifetimeScope.cs": -20000,
    "Assets/Scripts/Game/Managers/MapManager.cs": -10000,
};

function isExcludedDiPath(filePath) {
    return /(^|\/)Tests\//.test(filePath) ||
        /(^|\/)Plugins\//.test(filePath) ||
        /(^|\/)VContainer\//.test(filePath) ||
        /(^|\/)Editor\//.test(filePath);
}

function checkExecutionOrders() {
    for (const [filePath, expected] of Object.entries(EXECUTION_ORDER_CONTRACTS)) {
        const content = readFile(filePath);
        if (content === null) {
            continue;
        }
        const m = /\[DefaultExecutionOrder\(\s*(-?\d+)\s*\)\]/.exec(content);
        if (!m || parseInt(m[1], 10) !== expected) {
            recordViolation(
                "Execution Order Contract",
                filePath,
                `Expected [DefaultExecutionOrder(${expected})], found ${m ? m[0] : "none"}.`,
            );
        }
    }
}

function checkLifetimeScopeConfigure() {
    for (const filePath of walkCs("Assets/Scripts")) {
        if (isExcludedDiPath(filePath)) {
            continue;
        }
        const content = readFile(filePath);
        if (content === null || !content.includes("LifetimeScope")) {
            continue;
        }
        const lines = content.split("\n");
        for (let index = 0; index < lines.length; index++) {
            if (!lines[index].includes("RegisterBuildCallback")) {
                continue;
            }
            // A callback that only injects authored scene objects is required
            // before Unity calls Start. Resolve, scene loading and startup work
            // still belong in IPostStartable.
            if (lines[index].includes("InjectSceneBehaviours")) {
                continue;
            }
            recordViolation(
                "Configure Reentrancy",
                filePath + ":" + (index + 1),
                "RegisterBuildCallback may only inject authored scene behaviours. Move Resolve/scene loading/startup work to IPostStartable.",
            );
        }
    }
}

function checkProjectCompileIncludes() {
    for (const projectFile of fs.readdirSync(".").filter((file) => file.endsWith(".csproj"))) {
        const content = readFile(projectFile);
        if (content === null) {
            continue;
        }

        for (const match of content.matchAll(/<Compile Include="([^"]+)"/g)) {
            const sourcePath = match[1].replaceAll("\\", "/");
            if (!fs.existsSync(sourcePath)) {
                recordViolation(
                    "Project References",
                    `${projectFile}:${content.slice(0, match.index).split("\n").length}`,
                    `Compile Include points to a missing source file: ${sourcePath}. Regenerate or clean the project file.`,
                );
            }
        }
    }
}

function checkSceneReadinessContracts() {
    const scope = readFile("Assets/Scripts/Core/GameLifetimeScope.cs");
    const bootstrap = readFile("Assets/Scripts/Core/BootstrapLifetimeScope.cs");
    const gameBootstrap = readFile("Assets/Scripts/Core/GameBootstrap.cs");
    const gameManager = readFile("Assets/Scripts/Game/Managers/GameManager.cs");

    if (scope !== null &&
        (!scope.includes("WaitUntilReadyAsync") ||
            !scope.includes("MarkReady") ||
            !scope.includes("MarkFailed"))) {
        recordViolation(
            "Scene Readiness",
            "Assets/Scripts/Core/GameLifetimeScope.cs",
            "GameLifetimeScope must expose a deterministic ready/failed signal for Bootstrap scene transitions.",
        );
    }

    if (bootstrap !== null &&
        (!bootstrap.includes("WaitForPresentationAsync") ||
            !bootstrap.includes("SceneTransitionTicket"))) {
        recordViolation(
            "Scene Readiness",
            "Assets/Scripts/Core/BootstrapLifetimeScope.cs",
            "Bootstrap must await the SceneTransitionTicket presentation readiness before unloading the previous scene.",
        );
    }

    if (gameBootstrap !== null &&
        (!gameBootstrap.includes("_scope.MarkReady()") ||
            !gameBootstrap.includes("_scope.MarkFailed(exception)"))) {
        recordViolation(
            "Scene Readiness",
            "Assets/Scripts/Core/GameBootstrap.cs",
            "GameBootstrap must publish both successful and failed startup outcomes.",
        );
    }

    if (gameManager !== null &&
        (!gameManager.includes("IsVisualsLoaded") ||
            !gameManager.includes("PendingAssetCount") ||
            !gameManager.includes("PendingCellTextureRequests") ||
            !gameManager.includes("_surfaceRenderer.IsInitialized") ||
            !gameManager.includes("_lightingEngine.IsInitialized"))) {
        recordViolation(
            "World Readiness",
            "Assets/Scripts/Game/Managers/GameManager.cs",
            "OnWorldLoaded must wait for player visuals, surface, lighting and pending asset/texture queues.",
        );
    }
}

function checkTransitionStateContracts() {
    const bootstrapPath = "Assets/Scripts/Core/BootstrapLifetimeScope.cs";
    const source = readFile(bootstrapPath);
    if (source === null) {
        return;
    }

    if (!source.includes("_currentSceneName = null;")) {
        recordViolation(
            "Scene Transition Contract",
            bootstrapPath,
            "Transitioning away from a loaded scene must clear the current-scene state before loading the replacement.",
        );
    }

    if (!source.includes("TransitionStarted?.Invoke(sceneName)") ||
        !source.includes("TransitionCompleted?.Invoke(sceneName)")) {
        recordViolation(
            "Scene Transition Contract",
            bootstrapPath,
            "Bootstrap scene transitions must publish both start and completion events for the loading screen.",
        );
    }
}

function checkUiTransitionGuards() {
    const mainMenuPath = "Assets/Scripts/UI/MainMenu.cs";
    const gatewayPath = "Assets/Scripts/UI/GatewayController.cs";
    const mainMenu = readFile(mainMenuPath);
    const gateway = readFile(gatewayPath);
    if (mainMenu !== null && !/private void OnPlayButtonClicked\(\)\s*\{\s*if \(_loadingActive \|\| _teardownStarted\)/s.test(mainMenu)) {
        recordViolation(
            "UI Transition Contract",
            mainMenuPath,
            "MainMenu Play transition must be guarded against duplicate clicks while loading or tearing down.",
        );
    }
    if (gateway !== null && !/private void GoToMainMenu\(\)\s*\{\s*if \(_leaving\)/s.test(gateway)) {
        recordViolation(
            "UI Transition Contract",
            gatewayPath,
            "Gateway-to-menu transition must be guarded against duplicate activation.",
        );
    }
}

function checkSceneScopeInjection() {
    const contracts = [
        ["Assets/Scripts/Core/GatewayLifetimeScope.cs", "GatewayController"],
        ["Assets/Scripts/Core/MainMenuLifetimeScope.cs", "MainMenu"],
    ];
    for (const [filePath, component] of contracts) {
        const source = readFile(filePath);
        if (source === null) {
            continue;
        }
        if (!new RegExp(`RegisterComponent\\([^)]*_${component === "MainMenu" ? "controller" : "controller"}[^)]*\\)`).test(source)) {
            recordViolation(
                "Scene Scope Injection",
                filePath,
                `${component} must be registered as an authored scene component so VContainer injects it during resolution.`,
            );
        }
    }
}

function checkLifecycleSelfCalls() {
    for (const filePath of walkCs("Assets/Scripts")) {
        if (isExcludedDiPath(filePath)) {
            continue;
        }
        const source = readFile(filePath);
        if (source === null) {
            continue;
        }
        for (const methodName of ["Awake", "OnEnable", "Start", "OnDisable", "OnDestroy"]) {
            const method = new RegExp(`(?:void|UniTask|UniTaskVoid)\\s+${methodName}\\s*\\([^)]*\\)\\s*\\{([\\s\\S]*?)\\n\\s*\\}`, "g");
            for (const match of source.matchAll(method)) {
                if (new RegExp(`(?<!base\\.)\\b${methodName}\\s*\\(\\s*\\)`).test(match[1])) {
                    recordViolation(
                        "Lifecycle Contract",
                        filePath,
                        `${methodName}() must not be called manually from its own lifecycle logic; use an explicit initialization/rebinding method.`,
                    );
                }
            }
        }
    }
}

function checkMenuSceneryOwnership() {
    const bootstrapScene = readFile("Assets/Scenes/Bootstrap.unity");
    const mainMenuScene = readFile("Assets/Scenes/MainMenu.unity");
    if (bootstrapScene === null || mainMenuScene === null) {
        return;
    }

    const bootstrapOwnsScenery = bootstrapScene.includes("m_Name: MenuScenery");
    const menuOwnsScenery = mainMenuScene.includes("m_Name: MenuScenery");
    if (bootstrapOwnsScenery || !menuOwnsScenery) {
        recordViolation(
            "Scene Ownership",
            "Assets/Scenes/Bootstrap.unity",
            "MainMenu must own MenuScenery and Bootstrap must not contain menu scenery. Use Unity Editor API to restore scene ownership.",
        );
    }
}

function checkEditorSceneAuthoringContract() {
    const authoring = readFile("Assets/Scripts/Editor/SceneScopeAuthoring.cs");
    const migration = readFile("Assets/Scripts/Editor/SceneContractMigration.cs");
    const validator = readFile("Assets/Scripts/Editor/ProductionSceneContractValidator.cs");
    const runtimeScope = readFile("Assets/Scripts/Core/GameLifetimeScope.cs");

    if (authoring !== null || migration !== null) {
        recordViolation(
            "Scene Authoring Contract",
            "Assets/Scripts/Editor/SceneScopeAuthoring.cs",
            "Scene auto-fixing editor tools are deleted; only the read-only ProductionSceneContractValidator may exist.",
        );
    }

    if (validator === null) {
        recordViolation(
            "Scene Authoring Contract",
            "Assets/Scripts/Editor/ProductionSceneContractValidator.cs",
            "The read-only ProductionSceneContractValidator must exist and guard scene contracts.",
        );
    }

    if (runtimeScope !== null && !runtimeScope.includes('RegisterManager<WorldTextureManager>(builder, "World")')) {
        recordViolation(
            "Scene Authoring Contract",
            "Assets/Scripts/Core/GameLifetimeScope.cs",
            "MainGame World manager contract must include WorldTextureManager.",
        );
    }
}

function checkGameBootstrapResolvesRegisteredManagers() {
    const scopePath = "Assets/Scripts/Core/GameLifetimeScope.cs";
    const bootstrapPath = "Assets/Scripts/Core/GameBootstrap.cs";
    const scope = readFile(scopePath);
    const bootstrap = readFile(bootstrapPath);
    if (scope === null || bootstrap === null) {
        return;
    }

    if (scope.includes("GameStartupServices")) {
        recordViolation(
            "Startup Dependency Contract",
            scopePath,
            "GameStartupServices is deleted: GameBootstrap receives only its real startup dependencies via constructor injection.",
        );
    }

    if (!scope.includes("RegisterEntryPoint<GameBootstrap>")) {
        recordViolation(
            "Startup Dependency Contract",
            scopePath,
            "GameLifetimeScope must register GameBootstrap as the entry point of the MainGame composition root.",
        );
    }

    if (/\b(?:_resolver|resolver)\.Resolve\s*</.test(bootstrap) ||
        /\bResolve\s*<[^>]+>\s*\(/.test(bootstrap)) {
        recordViolation(
            "Startup Resolve Contract",
            bootstrapPath,
            "GameBootstrap must not resolve from the container; constructor injection only.",
        );
    }
}

function checkCompositionRootContracts() {
    const roots = [
        "Assets/Scripts/Core/BootstrapLifetimeScope.cs",
        "Assets/Scripts/Core/GameLifetimeScope.cs",
        "Assets/Scripts/Core/GatewayLifetimeScope.cs",
        "Assets/Scripts/Core/MainMenuLifetimeScope.cs",
    ];
    for (const filePath of roots) {
        const source = readFile(filePath);
        if (source === null) {
            continue;
        }
        if (/Find(?:AnyObject|FirstObject|Objects?ByType)<|FindGameObjectWithTag\s*\(/.test(source)) {
            recordViolation(
                "Composition Root Scene Scan",
                filePath,
                "Composition roots must use serialized references or their own authored hierarchy; global runtime scene scans are forbidden.",
            );
        }
    }
}

function checkDirectDependencyCycles() {
    const graph = new Map();
    const typeNames = new Set();

    for (const filePath of walkCs("Assets/Scripts")) {
        if (isExcludedDiPath(filePath)) {
            continue;
        }
        const content = readFile(filePath);
        if (content === null) {
            continue;
        }

        for (const match of content.matchAll(/\bclass\s+([A-Za-z0-9_]+)/g)) {
            typeNames.add(match[1]);
        }

        for (const match of content.matchAll(/\[Inject\]\s*(?:private|protected|public|internal)?\s*([A-Za-z0-9_<>.?]+)\s+[_A-Za-z0-9]+/g)) {
            const owner = content.slice(0, match.index).match(/\bclass\s+([A-Za-z0-9_]+)[^{]*\{[^{}]*$/)?.[1];
            if (owner) {
                const dependency = match[1].replace(/[<>.?]/g, "");
                if (!graph.has(owner)) {
                    graph.set(owner, new Set());
                }
                graph.get(owner).add(dependency);
            }
        }

        for (const className of typeNames) {
            const constructor = new RegExp(`\\b${escapeRegExp(className)}\\s*\\(([^)]*)\\)`, "m").exec(content);
            if (!constructor) {
                continue;
            }
            const dependencies = constructor[1].match(/[A-Za-z_][A-Za-z0-9_]*(?=\s+[_A-Za-z])/g) ?? [];
            if (!graph.has(className)) {
                graph.set(className, new Set());
            }
            for (const dependency of dependencies) {
                graph.get(className).add(dependency);
            }
        }
    }

    const reported = new Set();
    const visit = (typeName, path, active) => {
        if (active.has(typeName)) {
            const cycleStart = path.indexOf(typeName);
            const cycle = path.slice(cycleStart).concat(typeName);
            const key = [...cycle].sort().join("|");
            if (!reported.has(key)) {
                reported.add(key);
                recordViolation(
                    "DI Dependency Cycle",
                    "Assets/Scripts",
                    "Direct dependency cycle detected: " + cycle.join(" -> ") + ". Break the cycle with an event/callback or a narrow interface.",
                );
            }
            return;
        }
        if (!typeNames.has(typeName)) {
            return;
        }
        active.add(typeName);
        for (const dependency of graph.get(typeName) ?? []) {
            visit(dependency, path.concat(typeName), active);
        }
        active.delete(typeName);
    };

    for (const typeName of typeNames) {
        visit(typeName, [], new Set());
    }
}

function checkPacketSubscriptionSymmetry() {
    for (const filePath of walkCs("Assets/Scripts")) {
        if (isExcludedDiPath(filePath)) {
            continue;
        }
        const content = readFile(filePath);
        if (content === null) {
            continue;
        }

        const subscriptions = [...content.matchAll(/\.OnPacketReceived\s*\+=/g)].length;
        const unsubscriptions = [...content.matchAll(/\.OnPacketReceived\s*-\s*=/g)].length;
        if (subscriptions > 0 && unsubscriptions === 0) {
            recordViolation(
                "Subscription Lifetime",
                filePath,
                "OnPacketReceived is subscribed without a matching unsubscribe. This leaks scene listeners across transitions.",
            );
        }
    }
}

// Validate the serialized scene/DI contract. This catches the class of failure
// that code-only linting missed: a manager is registered for Services/UI while
// the scene asset is still flat or points the scope at the wrong root.
const SCENE_CONTRACTS = {
    "Assets/Scenes/Bootstrap.unity": {
        scope: "BootstrapLifetimeScope",
        groupRoot: "BootstrapLifetimeScope",
        components: ["BootstrapLifetimeScope"],
        uniqueComponents: ["UIDocument"],
        groups: {
            Networking: ["ConnectionManager", "NetworkService"],
            Content: ["ClientAssetLoader", "ClientConfigManager", "TextureStorageManager"],
            Audio: ["AudioSystem"],
            Presentation: ["BootstrapLoadingScreen"],
        },
        forbidden: ["GameLifetimeScope"],
    },
    "Assets/Scenes/MainGame.unity": {
        scope: "GameLifetimeScope",
        groupRoot: "GameLifetimeScope/Services",
        components: ["GameLifetimeScope"],
        uniqueComponents: ["UIDocument"],
        groups: {
            Networking: ["PacketHandler"],
            World: ["MapManager", "WorldBackgroundSetup", "WorldTextureManager"],
            Rendering: ["TerrainRenderer", "WorldEntityBatchRenderer", "PostProcessController", "LightingEngine", "SurfaceRenderer", "CameraFollow", "VFXPool"],
            Gameplay: ["GameManager", "BuildingManager", "RobotManager", "ServerConfig"],
            UI: ["GlobalChatUI", "UIInputManager", "FPSCounter", "FloatingChatManager", "ReconnectUI", "AssetLoadingIndicator", "MissionArrowUI", "DiagnosticRunner", "PlayerHUDView", "InventoryView", "PauseMenu", "MinimapController", "WorldMapController", "WorldMapRenderer", "DisplayManager", "InGameDebugOverlay"],
            Audio: ["ServerAudioEventManager"],
        },
    },
    "Assets/Scenes/Gateway.unity": {
        scope: "GatewayLifetimeScope",
        components: ["GatewayLifetimeScope", "GatewayController", "UIDocument"],
        uniqueComponents: ["UIDocument"],
    },
    "Assets/Scenes/MainMenu.unity": {
        scope: "MainMenuLifetimeScope",
        components: ["MainMenuLifetimeScope", "MainMenu", "UIDocument"],
        uniqueComponents: ["UIDocument"],
    },
};

function parseUnitySceneContract(filePath) {
    const source = readFile(filePath);
    if (source === null) {
        return null;
    }
    buildScriptClassIndex();
    const objects = new Map();
    const unresolvedScripts = [];
    for (const raw of source.split(/^--- !u!/m).slice(1)) {
        const header = raw.match(/^(\d+) &(-?\d+)\n/);
        if (!header) {
            continue;
        }
        const type = Number(header[1]);
        const id = Number(header[2]);
        const body = raw.slice(header[0].length);
        if (type === 1) {
            const name = body.match(/^  m_Name: (.*)$/m)?.[1]?.trim() ?? "";
            const componentIds = [...body.matchAll(/- component: \{fileID: (-?\d+)\}/g)].map((m) => Number(m[1]));
            objects.set(id, { type, id, name, componentIds });
        } else if (type === 4) {
            objects.set(id, {
                type,
                id,
                goId: Number(body.match(/m_GameObject: \{fileID: (-?\d+)\}/)?.[1] ?? 0),
                parentId: Number(body.match(/m_Father: \{fileID: (-?\d+)\}/)?.[1] ?? 0),
            });
        } else if (type === 114) {
            const scriptGuid = body.match(/m_Script: \{fileID: 11500000, guid: ([a-f0-9]+), type: 3\}/)?.[1];
            const serializedIdentifier = body.match(/^  m_EditorClassIdentifier:[ \t]*(.*?)[ \t]*$/m)?.[1] ?? "";
            const serializedClassName = serializedIdentifier
                ? serializedIdentifier.split("::").pop().split(".").pop()
                : "";
            objects.set(id, {
                type,
                id,
                goId: Number(body.match(/m_GameObject: \{fileID: (-?\d+)\}/)?.[1] ?? 0),
                scriptGuid,
                className: serializedClassName ||
                    (scriptGuid ? SCRIPT_CLASS_BY_GUID.get(scriptGuid) ?? "" :
                        body.includes("m_Script: {fileID: 19102,")
                            ? "UIDocument"
                            : ""),
            });
            if (scriptGuid && !SCRIPT_CLASS_BY_GUID.has(scriptGuid) &&
                !body.includes("m_Script: {fileID: 19102,") && !serializedClassName) {
                unresolvedScripts.push({ id, scriptGuid });
            }
        }
    }
    const transforms = new Map([...objects.values()]
        .filter((object) => object.type === 4)
        .map((object) => [object.goId, object]));
    const pathFor = (goId, seen = new Set()) => {
        if (seen.has(goId)) {
            return "<cycle>";
        }
        seen.add(goId);
        const object = objects.get(goId);
        if (!object || object.type !== 1) {
            return "<unknown>";
        }
        const parentTransformId = transforms.get(goId)?.parentId ?? 0;
        if (!parentTransformId) {
            return object.name;
        }

        const parentTransform = objects.get(parentTransformId);
        const parentGameObjectId = parentTransform?.type === 4 ? parentTransform.goId : 0;
        return parentGameObjectId ? pathFor(parentGameObjectId, seen) + "/" + object.name : object.name;
    };
    const gameObjects = [...objects.values()].filter((object) => object.type === 1);
    const components = [];
    for (const object of gameObjects) {
        for (const componentId of object.componentIds ?? []) {
            const component = objects.get(componentId);
            if (component?.type === 114) {
                components.push({ className: component.className, path: pathFor(object.id) });
            }
        }
    }
    return { gameObjects, components, pathFor, unresolvedScripts };
}

function checkSerializedSceneContracts() {
    for (const [filePath, contract] of Object.entries(SCENE_CONTRACTS)) {
        const scene = parseUnitySceneContract(filePath);
        if (scene === null) {
            recordViolation("Scene Contract", filePath, "Scene file could not be read.");
            continue;
        }
        for (const unresolved of scene.unresolvedScripts) {
            recordViolation(
                "Scene Contract",
                filePath,
                `Scene contains an unresolved script GUID ${unresolved.scriptGuid} on MonoBehaviour ${unresolved.id}. The component may be missing or belongs to an unindexed assembly.`,
            );
        }
        const scopeMatches = scene.components.filter((component) => component.className === contract.scope);
        const allScopeMatches = scene.components.filter((component) => component.className.endsWith("LifetimeScope"));
        if (allScopeMatches.length !== 1) {
            recordViolation(
                "Scene Contract",
                filePath,
                `Expected exactly one LifetimeScope component in the scene, found ${allScopeMatches.length}: ` +
                    allScopeMatches.map((component) => `${component.className}@${component.path}`).join(", "),
            );
        }
        if (scopeMatches.length !== 1) {
            recordViolation(
                "Scene Contract",
                filePath,
                `Expected exactly one ${contract.scope} component, found ${scopeMatches.length}.`,
            );
        }

        if (scopeMatches.length === 0) {
            recordViolation("Scene Contract", filePath, "Required scope '" + contract.scope + "' is missing.");
            continue;
        }

        for (const componentName of contract.components ?? []) {
            if (!scene.components.some((component) => component.className === componentName)) {
                recordViolation("Scene Contract", filePath, `Required component '${componentName}' is missing.`);
            }
        }
        for (const componentName of contract.uniqueComponents ?? []) {
            const matches = scene.components.filter((component) => component.className === componentName);
            if (matches.length !== 1) {
                recordViolation(
                    "Scene Contract",
                    filePath,
                    `Expected exactly one ${componentName} component, found ${matches.length}: ` +
                        matches.map((match) => match.path).join(", "),
                );
            }
        }
        for (const forbidden of contract.forbidden ?? []) {
            if (scene.gameObjects.some((object) => object.name === forbidden)) {
                recordViolation("Scene Contract", filePath, "Foreign object '" + forbidden + "' is present; it belongs only to its own scene.");
            }
        }
        for (const [group, classNames] of Object.entries(contract.groups ?? {})) {
            const prefix = contract.groupRoot + "/" + group;
            if (!scene.gameObjects.some((object) => scene.pathFor(object.id) === prefix)) {
                recordViolation("Scene Contract", filePath, "Required hierarchy '" + prefix + "' is missing.");
                continue;
            }
            for (const className of classNames) {
                const matches = scene.components.filter((component) => component.className === className);
                if (matches.length === 0) {
                    recordViolation("Scene Contract", filePath, "Registered manager '" + className + "' has no authored component.");
                } else if (matches.length > 1) {
                    recordViolation(
                        "Scene Contract",
                        filePath,
                        "Registered manager '" + className + "' has duplicate authored components: " +
                            matches.map((match) => match.path).join(", "),
                    );
                } else if (!matches.some((component) => component.path.startsWith(prefix + "/"))) {
                    recordViolation("Scene Contract", filePath, "Manager '" + className + "' is outside '" + prefix + "': " + matches.map((m) => m.path).join(", "));
                } else if (!matches.some((component) => component.path === prefix + "/" + className)) {
                    recordViolation(
                        "Scene Contract",
                        filePath,
                        "Manager '" + className + "' must be the direct authored object '" + prefix + "/" + className + "'.",
                    );
                }
            }
        }
    }
}

function checkUnityNamespaces() {
    for (const filePath of walkCs("Assets/Scripts")) {
        if (isExcludedDiPath(filePath)) {
            continue;
        }
        const content = readFile(filePath);
        if (content === null) {
            continue;
        }
        const unity = /class\s+([A-Za-z0-9_]+)\s*:[^{]*\b(MonoBehaviour|ScriptableObject|VolumeComponent|ScriptableRendererFeature)\b/.exec(content);
        if (unity && /^\s*namespace\s+[A-Za-z0-9_.]+\s*;/m.test(content)) {
            recordViolation(
                "Unity Namespace Contract",
                filePath,
                `Class '${unity[1]}' inherits from Unity type but uses file-scoped namespace. Must use block namespace { } to prevent MonoScript.GetClass() == null.`,
            );
        }
    }
}

function checkEarlyLifecycleDiAndCallgraph() {
    for (const filePath of walkCs("Assets/Scripts")) {
        if (isExcludedDiPath(filePath)) {
            continue;
        }
        const content = readFile(filePath);
        if (content === null) {
            continue;
        }

        // All [Inject] field names in the class.
        const fieldNames = new Set();
        for (const m of content.matchAll(/\[Inject\]\s*(?:private|protected|public)?\s*([A-Za-z0-9_<>?]+)\s+([_A-Za-z0-9]+)\s*(=|;)/g)) {
            fieldNames.add(m[2]);
        }
        if (fieldNames.size === 0) {
            continue;
        }

        // Parse every method body (brace-matched) into name -> body.
        const methods = {};
        const methodRe = /(?:private|protected|public|internal)?\s*(?:override|virtual|static)?\s*(?:void|bool|int|string|Task|UniTask|UniTaskVoid)\s+([A-Za-z0-9_]+)\s*\([^)]*\)\s*\{/g;
        for (const m of content.matchAll(methodRe)) {
            const name = m[1];
            let end = m.index + m[0].length;
            let braceCount = 1;
            while (end < content.length && braceCount > 0) {
                if (content[end] === "{") {
                    braceCount++;
                } else if (content[end] === "}") {
                    braceCount--;
                }
                end++;
            }
            methods[name] = content.slice(m.index + m[0].length, end - 1);
        }

        // Trace the synchronous call graph from Awake/OnEnable.
        for (const entry of ["Awake", "OnEnable"]) {
            if (!(entry in methods)) {
                continue;
            }
            const visited = new Set([entry]);
            const queue = [entry];
            while (queue.length > 0) {
                const curr = queue.shift();
                let body = methods[curr] || "";
                // Strip lambda bodies and UI Toolkit callback registrations so
                // delegate subscriptions are not treated as synchronous calls.
                body = body.replace(/=>\s*\{[^}]*\}/g, "=> {}");
                body = body.replace(/RegisterCallback<[^>]+>\s*\([^)]*\)/g, "");
                for (const other of Object.keys(methods)) {
                    if (visited.has(other) || other === entry) {
                        continue;
                    }
                    let found = false;
                    for (const rawLine of body.split("\n")) {
                        const line = rawLine.trim();
                        if (line.includes("+=") || line.includes("-=") || line.includes("=>")) {
                            continue;
                        }
                        if (new RegExp("\\b" + escapeRegExp(other) + "\\s*\\(").test(line)) {
                            found = true;
                            break;
                        }
                    }
                    if (found) {
                        visited.add(other);
                        queue.push(other);
                    }
                }
            }

            for (const reached of visited) {
                const body = methods[reached] || "";
                const norm = body.replace(/\s+/g, " ");

                if (/\b(Session|_session)\.Resolve</.test(norm)) {
                    recordViolation(
                        "Early Lifecycle DI",
                        filePath,
                        `Calling Resolve<T>() in ${entry}() -> ${reached}() is forbidden. Use TryResolve<T>() with null-guard.`,
                    );
                }

                for (const fn of fieldNames) {
                    const derefRe = new RegExp("\\b" + escapeRegExp(fn) + "\\s*(\\.|\\(|\\[)", "g");
                    if (![...body.matchAll(derefRe)].length) {
                        continue;
                    }
                    const hasGuard =
                        (new RegExp("if\\s*\\([^)]*\\b" + escapeRegExp(fn) + "\\s*==\\s*null").test(body) && body.includes("return")) ||
                        new RegExp("if\\s*\\([^)]*\\b" + escapeRegExp(fn) + "\\s*!=\\s*null").test(body) ||
                        new RegExp("if\\s*\\([^)]*\\b" + escapeRegExp(fn) + "\\s*is\\s+not\\s+null").test(body) ||
                        new RegExp("\\b" + escapeRegExp(fn) + "\\s*!=\\s*null\\s*\\?").test(norm) ||
                        new RegExp("\\b" + escapeRegExp(fn) + "\\s*\\?\\.").test(body) ||
                        new RegExp("if\\s*\\([^)]*_isInitialized[^)]*\\)\\s*\\{[^}]*\\b" + escapeRegExp(fn) + "\\b").test(body) ||
                        (reached === "TrySubscribeToNetworkService" && filePath.includes("PacketHandler"));
                    if (!hasGuard) {
                        recordViolation(
                            "Unguarded [Inject] Field Access",
                            filePath,
                            `Field '${fn}' is accessed in ${entry}() -> ${reached}() without a null check.`,
                        );
                    }
                }
            }
        }
    }
}

function checkAsyncVoid() {
    for (const filePath of walkCs("Assets/Scripts")) {
        if (isExcludedDiPath(filePath)) {
            continue;
        }
        const content = readFile(filePath);
        if (content === null || !content.includes("MonoBehaviour")) {
            continue;
        }
        for (const m of content.matchAll(/async\s+void\s+([A-Za-z0-9_]+)\s*\(([^)]*)\)/g)) {
            const name = m[1];
            if (name.startsWith("On") || name.endsWith("Click") || name.endsWith("Clicked")) {
                continue;
            }
            recordViolation(
                "Async Void in MonoBehaviour",
                filePath,
                `Method 'async void ${name}' escapes UniTask lifecycle tracking. Use 'async UniTaskVoid' or 'async UniTask' with CancellationToken.`,
            );
        }
    }
}

// ---------------------------------------------------------------------------
// Part 3: settings wiring analyzer
// (ported from scripts/check_settings_wiring.py)
// ---------------------------------------------------------------------------

const CONFIG_PATH = "Assets/Scripts/Core/Interfaces/Contracts/ClientConfig.cs";
const BOOTSTRAP_PATH = "Assets/Scripts/Core/Bootstrap/GameBootstrap.cs";

const WIRING_EXCLUDE_DIRS = new Set(["Tests", "Plugins", "VContainer"]);

// Config-consuming MonoBehaviours that must apply their ClientConfig at
// startup. "Applied at startup" means GameBootstrap.cs invokes one of the
// listed methods on a typed receiver. Keep this list current: a MonoBehaviour
// exposing ApplyClientConfig that is missing from it fails the build, and a
// listed consumer whose method is no longer invoked fails too.
const STARTUP_APPLY_CONTRACTS = {
    "TerrainRenderer": ["ApplyClientConfig"],
    "SurfaceRenderer": ["ApplyClientConfig"],
    "LightingEngine": ["EnsureInitialized", "ApplyClientConfig"],
    "PostProcessController": ["EnsureVolumeSetup", "ApplyClientConfig"],
};

function collectWiringFiles() {
    const files = [];
    for (const root of ["Assets/Scripts", "Assets/Editor"]) {
        let entries;
        try {
            entries = fs.readdirSync(root, { withFileTypes: true });
        } catch {
            continue;
        }
        const stack = [...entries.map((e) => path.join(root, e.name))];
        while (stack.length > 0) {
            const full = stack.pop();
            const name = path.basename(full);
            if (WIRING_EXCLUDE_DIRS.has(name)) {
                continue;
            }
            let stat;
            try {
                stat = fs.statSync(full);
            } catch {
                continue;
            }
            if (stat.isDirectory()) {
                for (const entry of fs.readdirSync(full, { withFileTypes: true })) {
                    stack.push(path.join(full, entry.name));
                }
            } else if (stat.isFile() && name.endsWith(".cs")) {
                files.push(full);
            }
        }
    }
    return files;
}

function parseConfigFields(content) {
    const fields = [];
    for (const m of content.matchAll(/^\s*public\s+(?!const\b)([A-Za-z0-9_<>\[\],.\s?]+?)\s+([A-Za-z0-9_]+)\s*(?:=|;)/gm)) {
        fields.push(m[2]);
    }
    return fields;
}

// Collect every production file that references each ClientConfig field
// (ClientConfig.cs itself excluded). Shared by the dead-field and UI-only
// wiring checks so the tree is scanned once.
function collectConfigFieldReads() {
    const configSrc = readFile(CONFIG_PATH);
    const fields = configSrc === null ? [] : parseConfigFields(configSrc);
    const reads = new Map(fields.map((field) => [field, []]));
    const configAbs = path.resolve(CONFIG_PATH);
    for (const file of collectWiringFiles()) {
        if (path.resolve(file) === configAbs) {
            continue;
        }
        const content = readFile(file);
        if (content === null) {
            continue;
        }
        for (const field of fields) {
            if (new RegExp("\\." + escapeRegExp(field) + "\\b").test(content)) {
                reads.get(field).push(file);
            }
        }
    }
    return { configSrc, fields, reads };
}

function checkDeadConfigFields() {
    const { configSrc, fields, reads } = collectConfigFieldReads();
    if (configSrc === null) {
        recordViolation("Settings Wiring (dead field)", CONFIG_PATH, "Could not read ClientConfig.cs.");
        return;
    }
    if (fields.length === 0) {
        recordViolation("Settings Wiring (dead field)", CONFIG_PATH, "Could not parse ClientConfig fields.");
        return;
    }

    for (const field of fields) {
        if (reads.get(field).length === 0) {
            recordViolation(
                "Settings Wiring (dead field)",
                CONFIG_PATH,
                `ClientConfig.${field} is never referenced in production code — the setting does nothing. Wire it to a consumer or remove it.`,
            );
        }
    }
}

const CONFIG_MANAGER_PATH = "Assets/Scripts/Core/ClientConfigManager.cs";

// ClientConfig fields whose consumer legitimately lives in the UI layer: they
// are read AND applied there (e.g. via panelSettings.scale), so UI-only reads
// are correct, not dead wiring. Keep this list minimal and justified — a
// setting that is merely shown/saved by Settings but never applied anywhere
// (TargetFrameRate before DisplayManager wired it) must NOT be added here.
const UI_WIRING_ALLOWED_FIELDS = new Set([
    // Applied via UIDocument panelSettings.scale: PauseMenu.cs applies the
    // saved scale at startup and PauseMenuSettingsBuilder applies it live on
    // slider change — the UI panel itself is the consumer by design.
    "UIScale",
]);

function isUiControllerFile(file) {
    const normalized = file.replace(/\\/g, "/");
    const basename = normalized.split("/").pop();
    return normalized.includes("/UI/") || /(Gateway|PauseMenu)/.test(basename);
}

function checkUiOnlyWiring() {
    const { configSrc, fields, reads } = collectConfigFieldReads();
    if (configSrc === null || fields.length === 0) {
        return; // parse failure is reported by the dead-field check
    }
    const managerAbs = path.resolve(CONFIG_MANAGER_PATH);
    for (const field of fields) {
        if (UI_WIRING_ALLOWED_FIELDS.has(field)) {
            continue;
        }
        // ClientConfigManager validates/migrates fields — that is not a
        // consumer applying the setting, so it does not count as wiring.
        const readers = reads.get(field).filter((file) => path.resolve(file) !== managerAbs);
        if (readers.length === 0) {
            continue; // never referenced -> the dead-field check owns it
        }
        if (readers.every(isUiControllerFile)) {
            recordViolation(
                "Settings Wiring (UI-only)",
                CONFIG_PATH,
                `ClientConfig.${field} is read only from UI controllers (${readers.join(", ")}) — Settings can show and save it, but no game system ever applies it (the TargetFrameRate bug before DisplayManager wired it). Connect the field to a consumer or remove the setting.`,
            );
        }
    }
}

function checkUncoveredConsumers() {
    const applyRe = /public\s+void\s+ApplyClientConfig\s*\(/;
    const monoClassRe = /\bclass\s+[A-Za-z0-9_]+[^{]*:\s*MonoBehaviour\b/;
    for (const file of collectWiringFiles()) {
        if (path.resolve(file) === path.resolve(BOOTSTRAP_PATH)) {
            continue;
        }
        const content = readFile(file);
        if (content === null || !applyRe.test(content) || !monoClassRe.test(content)) {
            continue;
        }
        for (const m of content.matchAll(/\bclass\s+([A-Za-z0-9_]+)\s*:[^{]*\{/g)) {
            const cls = m[1];
            if (!(cls in STARTUP_APPLY_CONTRACTS)) {
                recordViolation(
                    "Settings Wiring (uncovered consumer)",
                    file,
                    `${cls} exposes ApplyClientConfig() but is missing from STARTUP_APPLY_CONTRACTS in scripts/check-architecture.js. Either wire it into GameBootstrap.PostStart and add it to the contract, or it will apply saved config only from the pause menu.`,
                );
            }
        }
    }
}

function checkStartupApplicationContract() {
    const bootstrapSrc = readFile(BOOTSTRAP_PATH);
    if (bootstrapSrc === null) {
        recordViolation("Settings Wiring (startup application)", BOOTSTRAP_PATH, "Could not read GameBootstrap.cs.");
        return;
    }

    // Map local variables to their contract class, e.g.
    //   out TerrainRenderer? terrainRenderer   -> terrainRenderer: TerrainRenderer
    //   var lightingEngine = Resolve<LightingEngine>() -> lightingEngine: LightingEngine
    const variables = {};
    for (const m of bootstrapSrc.matchAll(/\b(out\s+)?([A-Za-z0-9_<>]+)\??\s+([a-z_][A-Za-z0-9_]*)\s*(?:=|;|\))/g)) {
        if (m[2] in STARTUP_APPLY_CONTRACTS) {
            variables[m[3]] = m[2];
        }
    }
    for (const m of bootstrapSrc.matchAll(/\bvar\s+([a-z_][A-Za-z0-9_]*)\s*=\s*[^;]*?Resolve<([A-Za-z0-9_<>]+)>/g)) {
        if (m[2] in STARTUP_APPLY_CONTRACTS && !(m[1] in variables)) {
            variables[m[1]] = m[2];
        }
    }

    // Which contract methods are invoked on typed receivers:
    //   terrainRenderer.ApplyClientConfig()  -> (TerrainRenderer, ApplyClientConfig)
    const receivers = new Set();
    for (const [varName, typeName] of Object.entries(variables)) {
        for (const m of bootstrapSrc.matchAll(new RegExp("\\b" + escapeRegExp(varName) + "\\.([A-Za-z0-9_]+)\\s*\\(", "g"))) {
            receivers.add(`${typeName}.${m[1]}`);
        }
    }

    for (const [cls, applyMethods] of Object.entries(STARTUP_APPLY_CONTRACTS)) {
        if (!applyMethods.some((method) => receivers.has(`${cls}.${method}`))) {
            recordViolation(
                "Settings Wiring (startup application)",
                BOOTSTRAP_PATH,
                `${cls} is not applied at startup: GameBootstrap.cs must invoke ${cls}.${applyMethods.join(" or ")}() on a typed receiver — a resolve alone does not apply saved config, so its values are ignored until the player opens Settings.`,
            );
        }
    }
}

// ---------------------------------------------------------------------------
// Part 4: USS stylesheet validator
// (ported from Assets/Editor/Tools/lint-uss.py)
// ---------------------------------------------------------------------------

// Styles are validated against the UIElements property registry — the only
// reliable source: a name being present in the CSS parser (ExCSS) or as a
// string inside the engine assembly does not mean it is a registered property.
// The allowlist below is taken from the Unity 6000.5 USS properties reference
// (UIE-USS-SupportedProperties), plus -unity-background-scale-mode and all,
// which the original captured registry list was missing.
const STYLES_DIR = path.join(__dirname, "..", "Assets", "Resources", "Styles");

// Longhand properties from the UIElements 6000.5 registry.
const USS_LONGHAND = new Set([
    "all", "-unity-background-image-tint-color", "-unity-background-scale-mode",
    "-unity-editor-text-rendering-mode", "-unity-font", "-unity-font-definition",
    "-unity-material", "-unity-overflow-clip-box", "-unity-paragraph-spacing",
    "-unity-slice-bottom", "-unity-slice-left", "-unity-slice-right",
    "-unity-slice-scale", "-unity-slice-top", "-unity-slice-type",
    "-unity-text-align", "-unity-text-auto-size", "-unity-text-generator",
    "-unity-text-outline-color", "-unity-text-outline-width",
    "-unity-text-overflow-position",
    "align-content", "align-items", "align-self", "aspect-ratio",
    "background-color", "background-image", "background-position-x",
    "background-position-y", "background-repeat", "background-size",
    "border-bottom-color", "border-bottom-left-radius", "border-bottom-right-radius",
    "border-bottom-width", "border-left-color", "border-left-width",
    "border-right-color", "border-right-width", "border-top-color",
    "border-top-left-radius", "border-top-right-radius", "border-top-width",
    "bottom", "color", "cursor", "display", "filter", "flex-basis",
    "flex-direction", "flex-grow", "flex-shrink", "flex-wrap", "font-size",
    "height", "justify-content", "left", "letter-spacing", "margin-bottom",
    "margin-left", "margin-right", "margin-top", "max-height", "max-width",
    "min-height", "min-width", "opacity", "overflow", "padding-bottom",
    "padding-left", "padding-right", "padding-top", "position", "right",
    "rotate", "scale", "text-overflow", "text-shadow", "top", "transform-origin",
    "transition-delay", "transition-duration", "transition-property",
    "transition-timing-function", "translate", "visibility", "white-space",
    "word-spacing", "width",
    // Single-word and special properties — the registry stores them
    // differently from kebab-case pairs.
    "-unity-font-style", "-unity-text-outline-color",
]);

// Shorthands expand into longhand properties and are not in the registry.
const USS_SHORTHAND = new Set([
    "background", "background-position", "border", "border-color",
    "border-radius", "border-width", "flex", "font", "margin", "padding",
    "transition", "-unity-slice", "-unity-text-outline",
]);

const USS_ALLOWED = new Set([...USS_LONGHAND, ...USS_SHORTHAND]);

// Functions that do not exist in UIElements at all.
const USS_BAD_FUNCS = {
    "cubic-bezier": "в USS только 23 именованные кривые; ближайшая к сигнатурной — ease-out-circ",
    "radial-gradient": "поддерживается только linear-gradient",
    "conic-gradient": "поддерживается только linear-gradient",
    "calc": "арифметики в значениях нет",
    "min": "арифметики в значениях нет",
    "max": "арифметики в значениях нет",
    "clamp": "арифметики в значениях нет",
    "color-mix": "не поддерживается",
    "drop-shadow": "в наборе filter нет; свечение делается подложкой с blur()",
    "brightness": "в наборе filter нет",
    "saturate": "в наборе filter нет",
};

// The 23 named easing curves supported by USS.
const USS_EASINGS = new Set([
    "ease", "ease-in", "ease-out", "ease-in-out", "linear",
    "ease-in-sine", "ease-out-sine", "ease-in-out-sine",
    "ease-in-cubic", "ease-out-cubic", "ease-in-out-cubic",
    "ease-in-circ", "ease-out-circ", "ease-in-out-circ",
    "ease-in-elastic", "ease-out-elastic", "ease-in-out-elastic",
    "ease-in-back", "ease-out-back", "ease-in-out-back",
    "ease-in-bounce", "ease-out-bounce", "ease-in-out-bounce",
]);

function stripUssComments(text) {
    // Remove /* ... */ comments, preserving line numbers for diagnostics.
    return text.replace(/\/\*[\s\S]*?\*\//g, (m) => "\n".repeat(m.split("\n").length - 1));
}

function checkUssStyles() {
    let names;
    try {
        names = fs.readdirSync(STYLES_DIR).filter((n) => n.endsWith(".uss")).sort();
    } catch {
        recordViolation("USS Stylesheet", STYLES_DIR, `Не найдено ни одного .uss в ${STYLES_DIR}.`);
        return;
    }
    if (names.length === 0) {
        recordViolation("USS Stylesheet", STYLES_DIR, `Не найдено ни одного .uss в ${STYLES_DIR}.`);
        return;
    }

    const declared = new Set();
    const used = new Map(); // token -> Set(stylesheet basenames)
    let problemCount = 0;

    for (const name of names) {
        const full = path.join(STYLES_DIR, name);
        const src = readFile(full);
        if (src === null) {
            recordViolation("USS Stylesheet", full, "Не удалось прочитать файл.");
            problemCount++;
            continue;
        }
        const body = stripUssComments(src);

        const openBraces = (body.match(/\{/g) || []).length;
        const closeBraces = (body.match(/\}/g) || []).length;
        if (openBraces !== closeBraces) {
            recordViolation("USS Stylesheet", full, `${name}: скобки не сбалансированы`);
            problemCount++;
        }

        for (const m of body.matchAll(/(--[a-z0-9-]+)\s*:/gi)) {
            declared.add(m[1]);
        }
        for (const m of body.matchAll(/var\(\s*(--[a-z0-9-]+)/gi)) {
            if (!used.has(m[1])) {
                used.set(m[1], new Set());
            }
            used.get(m[1]).add(name);
        }

        const lines = body.split("\n");
        for (let i = 0; i < lines.length; i++) {
            const line = lines[i];
            const lineNo = i + 1;

            const decl = line.match(/^\s*(-?[a-zA-Z][\w-]*)\s*:/);
            if (decl) {
                const prop = decl[1];
                if (!prop.startsWith("--") && !USS_ALLOWED.has(prop)) {
                    recordViolation("USS Stylesheet", full, `${name}:${lineNo} свойство '${prop}' отсутствует в UI Toolkit`);
                    problemCount++;
                }
            }

            for (const [func, why] of Object.entries(USS_BAD_FUNCS)) {
                if (new RegExp("\\b" + escapeRegExp(func) + "\\s*\\(").test(line)) {
                    recordViolation("USS Stylesheet", full, `${name}:${lineNo} функция ${func}() — ${why}`);
                    problemCount++;
                }
            }

            const timing = line.match(/transition-timing-function\s*:\s*([^;]+);/);
            if (timing) {
                for (const raw of timing[1].split(",")) {
                    const value = raw.trim();
                    if (value.startsWith("var(") || value === "") {
                        continue;
                    }
                    if (!USS_EASINGS.has(value)) {
                        recordViolation("USS Stylesheet", full, `${name}:${lineNo} кривая '${value}' не входит в набор USS`);
                        problemCount++;
                    }
                }
            }
        }
    }

    for (const token of [...used.keys()].filter((t) => !declared.has(t)).sort()) {
        recordViolation("USS Stylesheet", STYLES_DIR, `токен ${token} используется (${[...used.get(token)].sort().join(", ")}), но не объявлен`);
        problemCount++;
    }

    console.log(`${CYAN}${BOLD}USS stylesheets:${NC} ${names.length} file(s), ${declared.size} token(s) declared, ${problemCount} violation(s)`);
}

// ---------------------------------------------------------------------------
// Part 5: localization linter
// ---------------------------------------------------------------------------

const LOCALIZATION_DIR = path.join(__dirname, "..", "Assets", "Resources", "Localization");

// Localization-key usages in production C#. Two sources, both excluding
// tests via collectWiringFiles:
//   1. Literal lookups: .Get("menu.play") / .HasKey("menu.play").
//   2. Keys referenced as data: a "dotted" string literal that exactly
//      matches a dictionary key counts as usage too (e.g. MenuLoaderProgress
//      stores phase keys in an array and resolves them through Get() at
//      runtime). Filenames like "client_config.json" never equal a key.
const LOC_KEY_USAGE_RE = /\.(?:Get|HasKey)\(\\?"([a-z][a-z0-9_.-]*\.[a-z0-9_.-]+)"/g;

// Render a set of placeholder indices as "{0},{1}" for diagnostics.
function placeholderList(indices) {
    return indices.map((i) => "{" + i + "}").join(",");
}

function checkLocalization() {
    // 1. Load all language files as flat string->string dictionaries.
    let names;
    try {
        names = fs.readdirSync(LOCALIZATION_DIR).filter((n) => n.endsWith(".json")).sort();
    } catch {
        recordViolation("Localization", LOCALIZATION_DIR, `Не найден каталог локализации ${LOCALIZATION_DIR}.`);
        return;
    }
    if (names.length === 0) {
        recordViolation("Localization", LOCALIZATION_DIR, "В Assets/Resources/Localization нет ни одного .json — словаря локализации нет вовсе.");
        return;
    }

    let problemCount = 0;
    const dictionaries = new Map(); // lang -> Map(key -> value)
    for (const name of names) {
        const full = path.join(LOCALIZATION_DIR, name);
        const src = readFile(full);
        if (src === null) {
            recordViolation("Localization (file)", full, `${name}: не удалось прочитать файл.`);
            problemCount++;
            continue;
        }
        let parsed;
        try {
            parsed = JSON.parse(src);
        } catch (ex) {
            recordViolation("Localization (invalid JSON)", full, `${name}: файл не парсится как JSON (${ex.message}).`);
            problemCount++;
            continue;
        }
        // JSON.parse silently collapses duplicate keys (last wins), so detect
        // them on the raw text: keys in these files sit on their own lines.
        const seenRaw = new Set();
        for (const m of src.matchAll(/^\s*"([^"]+)"\s*:/gm)) {
            if (seenRaw.has(m[1])) {
                recordViolation("Localization (duplicate key)", full, `${name}: ключ '${m[1]}' объявлен несколько раз — побеждает последний, остальные потеряны.`);
                problemCount++;
            }
            seenRaw.add(m[1]);
        }
        const dict = new Map();
        for (const [key, value] of Object.entries(parsed)) {
            if (typeof value !== "string") {
                recordViolation("Localization (value type)", full, `${name}: значение ключа '${key}' — не строка (${typeof value}).`);
                problemCount++;
                continue;
            }
            dict.set(key, value);
        }
        dictionaries.set(name.replace(/\.json$/, ""), dict);
    }
    if (dictionaries.size === 0) {
        return;
    }

    const allKeys = new Set();
    for (const dict of dictionaries.values()) {
        for (const key of dict.keys()) {
            allKeys.add(key);
        }
    }
    const langs = [...dictionaries.keys()];

    // 2. Key-set parity: en is the runtime fallback, so a key missing in any
    //    language either shows the raw key (en) or silently falls back to en.
    for (const key of [...allKeys].sort()) {
        const missing = langs.filter((lang) => !dictionaries.get(lang).has(key));
        if (missing.length > 0) {
            recordViolation(
                "Localization (key parity)",
                LOCALIZATION_DIR,
                `Ключ '${key}' есть не во всех языках: отсутствует в ${missing.join(", ")}. В игре покажется сырой ключ или сработает неявный fallback на en.`,
            );
            problemCount++;
        }
    }

    // 2b. Translated languages must not carry source-language (Cyrillic) text:
    //     the ru dictionary is the source, every other language is a translation.
    for (const lang of langs) {
        if (lang === "ru") {
            continue;
        }
        for (const [key, value] of dictionaries.get(lang)) {
            if (/[А-Яа-яЁё]/.test(value)) {
                recordViolation(
                    "Localization (translation has Cyrillic)",
                    path.join(LOCALIZATION_DIR, lang + ".json"),
                    `Ключ '${key}': значение содержит кириллицу — похоже, перевод не сделан и остался русский текст.`,
                );
                problemCount++;
            }
        }
    }

    // 3. Placeholder sanity: {N} indices must be a contiguous prefix from {0}
    //    (string.Format throws FormatException and Get() returns the raw
    //    string otherwise), and identical across languages for the same key.
    for (const [lang, dict] of dictionaries) {
        for (const [key, value] of dict) {
            const indices = [...value.matchAll(/\{(\d+)\}/g)].map((m) => parseInt(m[1], 10));
            const sorted = [...new Set(indices)].sort((a, b) => a - b);
            if (sorted.length > 0 && sorted.some((v, i) => v !== i)) {
                recordViolation(
                    "Localization (placeholders)",
                    path.join(LOCALIZATION_DIR, lang + ".json"),
                    `'${key}' в '${lang}': плейсхолдеры {${placeholderList(sorted)}} — должны идти подряд, начиная с {0}.`,
                );
                problemCount++;
            }
        }
    }
    for (const key of allKeys) {
        const perLang = [];
        let complete = true;
        for (const lang of langs) {
            const dict = dictionaries.get(lang);
            if (!dict.has(key)) {
                complete = false; // parity check already reported the gap
                break;
            }
            perLang.push([lang, new Set([...(dict.get(key) ?? "").matchAll(/\{(\d+)\}/g)].map((m) => m[1]))]);
        }
        if (!complete) {
            continue;
        }
        const [firstLang, firstSet] = perLang[0];
        for (const [lang, set] of perLang.slice(1)) {
            if (firstSet.size !== set.size || [...firstSet].some((i) => !set.has(i))) {
                recordViolation(
                    "Localization (placeholders)",
                    LOCALIZATION_DIR,
                    `Ключ '${key}': набор плейсхолдеров в '${firstLang}' ({${placeholderList([...firstSet].sort())}}) отличается от '${lang}' ({${placeholderList([...set].sort())}}) — string.Format упадёт на одном из языков.`,
                );
                problemCount++;
            }
        }
    }

    // 4. Usage wiring: every key used in production C# must exist in every
    //    language; dictionary keys never used are dead. Keys also count as
    //    used when they appear as UXML text attributes (text="hud.mission"):
    //    UILocalizer resolves them at runtime, so UXML is a legitimate reader.
    const usedKeys = new Set();
    for (const file of collectWiringFiles()) {
        const content = readFile(file);
        if (content === null) {
            continue;
        }
        for (const m of content.matchAll(LOC_KEY_USAGE_RE)) {
            usedKeys.add(m[1]);
        }
        for (const key of allKeys) {
            // Plain "key" or escaped \"key\" (inside interpolated strings).
            if (content.includes('"' + key + '"') || content.includes('\\"' + key + '\\"')) {
                usedKeys.add(key);
            }
        }
    }
    {
        const UI_DIR = path.join(__dirname, "..", "Assets", "Resources", "UI");
        for (const name of fs.readdirSync(UI_DIR)) {
            if (!name.endsWith(".uxml")) {
                continue;
            }
            const content = readFile(path.join(UI_DIR, name));
            if (content === null) {
                continue;
            }
            for (const m of content.matchAll(/(?:text|tooltip)="([^"]*)"/g)) {
                // Dotted lowercase values are localization keys: count them as
                // used (the missing-key check then catches typos that would
                // otherwise render as raw keys at runtime). Tooltips count too:
                // UILocalizer resolves them the same way as text.
                if (/^[a-z][a-z0-9_.-]*\.[a-z0-9_.-]+$/.test(m[1])) {
                    usedKeys.add(m[1]);
                }
            }
        }
    }

    if (usedKeys.size === 0) {
        recordViolation(
            "Localization (unwired)",
            LOCALIZATION_DIR,
            `Локализация не подключена: в словарях объявлено ${allKeys.size} ключей (${langs.join(", ")}), но ни один не используется в production-коде — UI показывает захардкоженные строки. Переведите строки на .Get("...") или удалите словарь.`,
        );
        problemCount++;
    } else {
        for (const key of [...usedKeys].sort()) {
            const missing = langs.filter((lang) => !dictionaries.get(lang).has(key));
            if (missing.length > 0) {
                recordViolation(
                    "Localization (missing key)",
                    LOCALIZATION_DIR,
                    `Ключ '${key}' используется в коде, но отсутствует в ${missing.join(", ")} — в игре покажется сырой ключ.`,
                );
                problemCount++;
            }
        }
        for (const key of [...allKeys].filter((k) => !usedKeys.has(k)).sort()) {
            recordViolation(
                "Localization (dead key)",
                LOCALIZATION_DIR,
                `Ключ '${key}' объявлен в словаре, но нигде не используется — строка либо захардкожена, либо ключ потерян.`,
            );
            problemCount++;
        }
    }

    console.log(`${CYAN}${BOLD}Localization:${NC} ${dictionaries.size} language(s), ${allKeys.size} key(s), ${problemCount} violation(s)`);
}

// Hardcoded-text bans: the localization dictionary is the single source of
// truth for displayed text, so UXML must not carry Cyrillic literals and UI
// code must not assign Cyrillic string literals to displayed text (or feed
// them to text constructors/tooltips). Debug/exception messages are not
// displayed text and stay exempt.
function checkHardcodedText() {
    // 1. UXML: text="..." with Cyrillic is a hardcoded string that would show
    //    in the source language regardless of the chosen language.
    const UI_DIR = path.join(__dirname, "..", "Assets", "Resources", "UI");
    for (const name of fs.readdirSync(UI_DIR)) {
        if (!name.endsWith(".uxml")) {
            continue;
        }
        const src = readFile(path.join(UI_DIR, name));
        if (src === null) {
            continue;
        }
        for (const m of src.matchAll(/(?:text|tooltip)="([^"]*[А-Яа-яЁё][^"]*)"/g)) {
            const attr = m[0].split("=")[0];
            recordViolation(
                "Localization (hardcoded UXML text)",
                path.join(UI_DIR, name),
                `'${m[1]}' — ${attr}-атрибут в UXML захардкожен; задайте ключ (${attr}="ключ") и переведите строку в словарь, либо уберите, если текст ставит код (Tooltip.AttachTo).`,
            );
        }
    }

    // 2. UI code: Cyrillic string literals that feed displayed text
    //    (.text / .tooltip / new Label / new Button / tooltip providers).
    //    Exempt: Debug.*/Assert/throw statements and their multi-line
    //    continuations — tracked until the statement's closing ';'.
    const UI_SRC = path.join(__dirname, "..", "Assets", "Scripts", "UI");
    const files = walkCs(UI_SRC);
    const LIT_RE = /"([^"\\]*(?:\\.[^"\\]*)*)"/g;
    const isLogLine = (s) => /Debug\.(Log|LogWarning|LogError|LogException|Assert)\s*\(/.test(s) || /throw new/.test(s);
    for (const file of files) {
        const src = readFile(file);
        if (src === null) {
            continue;
        }
        const lines = src.split("\n");
        let inLogContext = false;
        for (let i = 0; i < lines.length; i++) {
            const raw = lines[i];
            const trimmed = raw.trim();
            if (!trimmed || trimmed.startsWith("//") || trimmed.startsWith("///") || trimmed.startsWith("*") || trimmed.startsWith("/*")) {
                continue;
            }
            const codePart = raw.split("//")[0];
            if (isLogLine(codePart)) {
                inLogContext = true;
            }
            if (!inLogContext) {
                // L("key", fallback[, args]) is a null-safe lookup helper: its
                // fallback literal is only used when localization is not
                // injected, so strip whole L(...) calls before scanning.
                const stripped = codePart.replace(/L\([^)]*\)/g, "");
                LIT_RE.lastIndex = 0;
                for (const m of stripped.matchAll(LIT_RE)) {
                    if (/[А-Яа-яЁё]/.test(m[1])) {
                        recordViolation(
                            "Localization (hardcoded UI text)",
                            file,
                            `строка ${i + 1}: '${m[1].slice(0, 60)}${m[1].length > 60 ? "…" : ""}' — текст задаётся литералом; используйте _loc.Get("...").`,
                        );
                    }
                }
            }
            if (/;\s*$/.test(codePart)) {
                inLogContext = false;
            }
        }
    }
}

function checkLocalizationWiring() {
    // The localization registry (LocalizationService.RegisterLocalizable) is the
    // only allowed way for UI views to hook into re-application on language
    // change. Manual `_loc.OnLanguageChanged +=` subscriptions are how views end
    // up "subscribed but never applied" — Gateway/PlayerHUD/Inventory built UI
    // with raw keys because they subscribed for re-apply but never applied at
    // startup. The registry applies at registration AND on every change, so a
    // view cannot forget either half.
    //
    // Second half of the contract: any UI file that clones/instantiates a UI
    // resource AND uses localization must resolve static keys right at build
    // time (UILocalizer.Apply), not only via a later re-apply pass.
    const UI_SRC = path.join(__dirname, "..", "Assets", "Scripts", "UI");
    const files = walkCs(UI_SRC);
    for (const file of files) {
        const src = readFile(file);
        if (src === null) {
            continue;
        }

        // Rule A: no manual OnLanguageChanged subscription/unsubscription in UI
        // code — the registry is the only channel (comments are stripped via
        // the // split, so doc mentions do not trigger).
        const lines = src.split("\n");
        for (let i = 0; i < lines.length; i++) {
            const codePart = lines[i].split("//")[0];
            if (!codePart.trim()) {
                continue;
            }
            if (/OnLanguageChanged\s*[-+]?=/.test(codePart)) {
                recordViolation(
                    "Localization (manual subscription)",
                    file,
                    `строка ${i + 1}: ручная подписка на OnLanguageChanged; используйте _loc.RegisterLocalizable(this) — сервис применяет текст сразу при регистрации и на каждой смене языка, а UnregisterLocalizable(this) — в OnDestroy.`,
                );
            }
        }

        // Rule B: EVERY tree-build site (CloneTree / TemplateContainer.Instantiate)
        // in a localizing file must localize its tree in the SAME method — either
        // UILocalizer.Apply right at the build site, or an ApplyLocalizedText()
        // call in that method. A UILocalizer.Apply that lives only in a re-apply
        // method (language change) leaves the freshly built tree with raw keys
        // until the first language switch — the "localization disappears after
        // scene transitions" failure. A file-level check cannot see this: the
        // apply exists, just not where the tree is built. Files that do not use
        // localization at all are exempt.
        const usesLocalization = /_loc\b|ILocalizationService|ILocalizableUI/.test(src);
        if (usesLocalization) {
            const lines = src.split("\n");
            for (let i = 0; i < lines.length; i++) {
                if (!/\.CloneTree\(\)|\.Instantiate\(\)/.test(lines[i])) {
                    continue;
                }

                // End of the enclosing method: its opening brace sits before the
                // build line, so scanning forward with depth starting at 0, the
                // method body closes when cumulative depth reaches -1.
                let depth = 0;
                let end = i;
                for (let j = i; j < lines.length; j++) {
                    depth += (lines[j].match(/\{/g) || []).length -
                             (lines[j].match(/\}/g) || []).length;
                    if (depth < 0) {
                        end = j;
                        break;
                    }
                }

                const methodBody = lines.slice(i, end + 1).join("\n");
                if (!/UILocalizer\.Apply|ApplyLocalizedText\(\)/.test(methodBody)) {
                    recordViolation(
                        "Localization (unresolved at build)",
                        file,
                        `строка ${i + 1}: дерево строится (CloneTree/Instantiate), но в этом же методе нет ни UILocalizer.Apply, ни ApplyLocalizedText() — статические ключи UXML останутся сырыми до первой смены языка. Применяйте локализацию в методе сборки, а не только в re-apply-методе.`,
                    );
                }
            }
        }

        // Rule C: a view that implements ILocalizableUI must register with the
        // service, otherwise language changes never reach it.
        if (/ILocalizableUI/.test(src) && !/RegisterLocalizable/.test(src)) {
            recordViolation(
                "Localization (unregistered)",
                file,
                `класс реализует ILocalizableUI, но нигде не вызывает RegisterLocalizable — смена языка до него не дойдёт, а стартовое применение никто не гарантирует.`,
            );
        }
    }
}

function checkLocalizationRegistry() {
    // Rule D — across ALL production scripts, not just Assets/Scripts/UI:
    // every UXML that carries localization keys must be loaded by a view that
    // is registered in the registry (RegisterLocalizable). A view that builds a
    // keyed tree but never registers is re-applied on no language change — it
    // stays in the language it was built in. Loader detection by basename:
    // Resources.Load<VisualTreeAsset>("UI/X") and the
    // ProjectRuntimeContracts.ResourcePaths.<X>Uxml constants.
    const UI_DIR = path.join(__dirname, "..", "Assets", "Resources", "UI");
    const SCRIPTS_DIR = path.join(__dirname, "..", "Assets", "Scripts");

    const keyedUxml = new Set();
    for (const name of fs.readdirSync(UI_DIR)) {
        if (!name.endsWith(".uxml")) {
            continue;
        }
        const content = readFile(path.join(UI_DIR, name));
        if (content === null) {
            continue;
        }
        for (const m of content.matchAll(/text="([^"]*)"/g)) {
            if (/^[a-z][a-z0-9_.-]*\.[a-z0-9_.-]+$/.test(m[1])) {
                keyedUxml.add(name.replace(/\.uxml$/, ""));
                break;
            }
        }
    }

    if (keyedUxml.size === 0) {
        return;
    }

    const loaderFiles = new Map(); // basename -> Set<file>
    for (const file of walkCs(SCRIPTS_DIR)) {
        const src = readFile(file);
        if (src === null) {
            continue;
        }
        const found = new Set();
        for (const m of src.matchAll(/Resources\.Load<VisualTreeAsset>\("([^"]+)"\)/g)) {
            const base = m[1].split("/").pop();
            if (keyedUxml.has(base)) {
                found.add(base);
            }
        }
        for (const m of src.matchAll(/ResourcePaths\.([A-Za-z]+)Uxml/g)) {
            if (keyedUxml.has(m[1])) {
                found.add(m[1]);
            }
        }
        if (found.size > 0) {
            for (const base of found) {
                if (!loaderFiles.has(base)) {
                    loaderFiles.set(base, new Set());
                }
                loaderFiles.get(base).add(file);
            }
        }
    }

    for (const [base, files] of loaderFiles) {
        for (const file of files) {
            const src = readFile(file);
            if (src === null || /RegisterLocalizable/.test(src)) {
                continue;
            }
            recordViolation(
                "Localization (unregistered loader)",
                file,
                `загружает ключевой UXML (${base}.uxml), но не вызывает RegisterLocalizable — смена языка до этой вьюхи не дойдёт. Зарегистрируйте её в реестре (ILocalizableUI + RegisterLocalizable) либо делегируйте переприменение зарегистрированному родителю.`,
            );
        }
    }
}

function checkSilentUiNoop() {
    // UI views that guard on the UIDocument panel (rootVisualElement is only
    // created in UIDocument.OnEnable) and silently `return` are the failure mode
    // that black-screens with ZERO console output: the screen never builds and
    // nothing is logged. Every such guard must either log, or carry a comment
    // explaining why the silent return is expected (a retry loop elsewhere, a
    // boolean contract, etc.) — otherwise it is indistinguishable from a broken
    // screen and a linter that passes while the game shows nothing.
    const UI_SRC = path.join(__dirname, "..", "Assets", "Scripts", "UI");
    const files = walkCs(UI_SRC);
    for (const file of files) {
        const src = readFile(file);
        if (src === null) {
            continue;
        }
        const lines = src.split("\n");
        for (let i = 0; i < lines.length; i++) {
            const line = lines[i];
            if (!line.includes("if") || !line.includes("rootVisualElement") ||
                !line.includes("== null")) {
                continue;
            }

            // Collect the guard body: the brace block (opening brace may sit on
            // the next line), or the single line when the whole `if (...) return;`
            // sits on one line.
            const blockLines = [line];
            let openLine = i;
            if (!line.includes("{") && i + 1 < lines.length && lines[i + 1].includes("{")) {
                openLine = i + 1;
                blockLines.push(lines[i + 1]);
            }
            if (line.includes("{") || openLine > i) {
                let depth = (lines[openLine].match(/\{/g) || []).length -
                    (lines[openLine].match(/\}/g) || []).length;
                let j = openLine + 1;
                while (j < lines.length && depth > 0) {
                    blockLines.push(lines[j]);
                    depth += (lines[j].match(/\{/g) || []).length -
                        (lines[j].match(/\}/g) || []).length;
                    j++;
                }
            }

            const blockText = blockLines.join("\n");
            if (!/\breturn\s*[^;]*;/.test(blockText)) {
                continue;
            }

            if (/Debug\.(Log|LogWarning|LogError|LogException)/.test(blockText)) {
                continue;
            }

            // Justification: a comment inside the guard or on the two lines
            // directly above it (the project already documents retry loops there).
            const above = lines.slice(Math.max(0, i - 2), i).join("\n");
            const hasComment =
                blockText.includes("//") ||
                blockText.includes("/*") ||
                above.includes("//");
            if (hasComment) {
                continue;
            }

            recordViolation(
                "Silent UI no-op",
                file,
                `строка ${i + 1}: guard на rootVisualElement == null молча делает return без Debug-лога и без комментария — при неготовой панели экран не построится, а в консоль не попадёт ничего. Либо добавьте Debug.LogWarning, либо комментарий, объясняющий, почему тихий возврат ожидаем (ретрай, boolean-контракт и т.п.).`,
            );
        }
    }
}

function checkSingleRoadInit() {
    // Одна дорога: инициализация вьюхи — ровно одна точка на сущность: Start
    // (к нему зависимости инжектятся при сборке scope и панель UIDocument уже
    // создана) + событие готовности для async-зависимостей (ServerConfig.
    // OnInitialized / MapManager.OnWorldInitialized / LightingEngine.OnInitialized
    // / session.OnSet). Per-frame ретрай TryInitialize из Update — это «конвейер
    // из пяти тихих no-op», на котором вьюхи умирали молча: гард тихо выходит,
    // пока зависимость не готова, и ни один лог не появляется, а линтер зелёный.
    const UI_SRC = path.join(__dirname, "..", "Assets", "Scripts", "UI");
    const files = walkCs(UI_SRC);
    for (const file of files) {
        const src = readFile(file);
        if (src === null) {
            continue;
        }
        const lines = src.split("\n");
        for (let i = 0; i < lines.length; i++) {
            if (!/void\s+Update\s*\(/.test(lines[i])) {
                continue;
            }

            // Collect the Update method body (brace may sit on the same line
            // or the next one).
            let openLine = i;
            if (!lines[i].includes("{") && i + 1 < lines.length && lines[i + 1].includes("{")) {
                openLine = i + 1;
            }
            if (!lines[openLine].includes("{")) {
                continue;
            }
            let depth = (lines[openLine].match(/\{/g) || []).length -
                (lines[openLine].match(/\}/g) || []).length;
            const body = [lines[openLine]];
            let j = openLine + 1;
            while (j < lines.length && depth > 0) {
                body.push(lines[j]);
                depth += (lines[j].match(/\{/g) || []).length -
                    (lines[j].match(/\}/g) || []).length;
                j++;
            }

            if (/\bTryInitialize\s*\(/.test(body.join("\n"))) {
                recordViolation(
                    "One-road init",
                    file,
                    `строка ${i + 1}: Update() вызывает TryInitialize — инициализация обязана быть событийной: Start (к нему зависимости и панель гарантированы) + событие готовности для async-зависимостей (ServerConfig.OnInitialized / MapManager.OnWorldInitialized / LightingEngine.OnInitialized / session.OnSet). Per-frame ретрай — это тихий конвейер no-op: вьюха молча ждёт зависимости, экран не строится, в консоль не попадает ничего.`,
                );
            }
        }
    }
}

// ---------------------------------------------------------------------------
// Entry point
// ---------------------------------------------------------------------------

function printViolations() {
    let patternCount = 0;
    for (const v of violations) {
        if (v.kind === "pattern") {
            patternCount++;
            console.log(`${RED}${BOLD}[VIOLATION]${NC} ${YELLOW}${v.message}${NC}`);
            console.log("");
        } else {
            console.log(`${RED}${BOLD}[${v.category.toUpperCase()} VIOLATION]${NC} ${YELLOW}${v.category}${NC}`);
            console.log(`  Location: ${BOLD}${v.loc}${NC}`);
            console.log(`  Details:  ${CYAN}${v.message}${NC}`);
            console.log("");
        }
    }
    return patternCount;
}

function main() {
    const startedAt = Date.now();
    const args = process.argv.slice(2);

    const files = args.length > 0 ? args : collectProductionFiles();
    const productionFiles = files.filter(
        (file) => fs.existsSync(file) && !EXCLUDE_REGEX.test(file),
    );

    console.log(`${CYAN}${BOLD}=== Fodinae Architectural Pattern Linter ===${NC}`);
    console.log(`Scanning ${BOLD}${productionFiles.length}${NC} files against ${BOLD}${RULES.length}${NC} architectural rules...`);
    console.log("");

    checkPatterns(productionFiles);
    checkExecutionOrders();
    checkLifetimeScopeConfigure();
    checkProjectCompileIncludes();
    checkSceneReadinessContracts();
    checkTransitionStateContracts();
    checkUiTransitionGuards();
    checkSceneScopeInjection();
    checkLifecycleSelfCalls();
    checkMenuSceneryOwnership();
    checkEditorSceneAuthoringContract();
    checkGameBootstrapResolvesRegisteredManagers();
    checkCompositionRootContracts();
    checkDirectDependencyCycles();
    checkPacketSubscriptionSymmetry();
    checkSerializedSceneContracts();
    checkUnityNamespaces();
    checkEarlyLifecycleDiAndCallgraph();
    checkAsyncVoid();
    checkDeadConfigFields();
    checkUiOnlyWiring();
    checkUncoveredConsumers();
    checkStartupApplicationContract();
    checkUssStyles();
    checkLocalization();
    checkHardcodedText();
    checkLocalizationWiring();
    checkLocalizationRegistry();
    checkSilentUiNoop();
    checkSingleRoadInit();

    const duration = ((Date.now() - startedAt) / 1000).toFixed(0);
    if (violations.length > 0) {
        const patternCount = printViolations();
        const otherCount = violations.length - patternCount;
        console.log(`${RED}${BOLD}✖ FAILED:${NC} Found ${BOLD}${violations.length}${NC} violation(s) ` +
            `(${BOLD}${patternCount}${NC} architectural, ${BOLD}${otherCount}${NC} DI/lifecycle + settings wiring + USS + localization) ` +
            `across ${BOLD}${productionFiles.length}${NC} files (${duration}s).`);
        console.log("");
        console.log(`${BOLD}Architectural Standards & Replacements:${NC}`);
        for (const line of STANDARDS_LIST) {
            console.log(line);
        }
        console.log("");
        console.log(`${BOLD}Deep semantic checks:${NC}`);
        console.log("  - execution-order contracts on LifetimeScopes/MapManager");
        console.log("  - Configure() reentrancy and direct AddComponent prohibition");
        console.log("  - Unity-serialized types must use block namespace { }");
        console.log("  - unguarded [Inject] access in Awake/OnEnable call graphs");
        console.log("  - safe DI resolution in early lifecycle (TryResolve, not Resolve)");
        console.log("  - no async void in MonoBehaviours (use UniTask)");
        console.log("  - every ClientConfig field referenced in production code");
        console.log("  - no ClientConfig field read only from UI controllers (dead wiring)");
        console.log("  - every config consumer applied at startup from GameBootstrap.PostStart");
        console.log("  - USS stylesheets: only UI Toolkit properties, functions and easings");
        console.log("  - localization: language parity, used-key existence, placeholders, dead keys");
        console.log("  - localization wiring: no manual OnLanguageChanged, CloneTree+UILocalizer.Apply, ILocalizableUI registered");
        console.log("  - localization registry: every keyed-UXML loader across all scripts must call RegisterLocalizable");
        process.exit(1);
    }

    console.log(`${GREEN}${BOLD}✔ PASSED:${NC} All ${BOLD}${productionFiles.length}${NC} production files conform to ` +
        `${BOLD}${RULES.length}${NC} architectural rules; DI/lifecycle, settings-wiring, USS and localization checks passed (${duration}s).`);
    process.exit(0);
}

main();
