#!/usr/bin/env bash
# Audits the complete production C# tree for project architecture, settings,
# and performance regressions. Existing violations fail exactly like new ones;
# this is a project linter, not a staged-diff decoration. Replacements:
#   - singletons -> VContainer DI
#   - ServiceLocator -> constructor/DI injection
#   - InputAction -> actions in InputSystem_Actions.inputactions
#   - Coroutines -> UniTask
#   - Legacy Input -> UnityEngine.InputSystem
#   - AudioSource -> FMOD Studio (AudioSystem)

set -euo pipefail

# ANSI color codes
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m' # No Color

start_time=$(date +%s)

PATTERNS=(
    'public[[:space:]]+static[[:space:]]+[A-Za-z0-9_<>?.]+[[:space:]]+Instance[[:space:]]*([({;=]|=>)'
    'ServiceLocator'
    'new[[:space:]]+InputAction\('
    'FitFieldDimensionsToAtlasBudget'
    'Mathf\.Approximately\([^,]*CameraOrthoSize'
    'Camera\.main'
    'Application\.targetFrameRate[[:space:]]*='
    'QualitySettings\.vSyncCount[[:space:]]*='
    'new[[:space:]]+Texture2D(Array)?[[:space:]]*\('
    '\.LoadImage[[:space:]]*\('
    '\.styleSheets\.Add[[:space:]]*\('
    'new[[:space:]]+Vector2[[:space:]]*\([^,]+,[[:space:]]*Screen\.height[[:space:]]*-'
    '\.style\.(width|height)[[:space:]]*=[^;]*Screen\.(width|height)'
    'LightingCascadeAtlasLimit[[:space:]]*<=[[:space:]]*256[[:space:]]*\?'
    '(FindAnyObjectByType|FindFirstObjectByType)<Camera>'
    'AddComponent<[A-Za-z0-9_]*(Manager|Service)>'
    '(Config|config)\.GraphicsPreset[[:space:]]*='
    '(Config|config)\.GraphicsQualitySettings[[:space:]]*='
    'QualitySettings\.antiAliasing[[:space:]]*='
    'QualitySettings\.SetQualityLevel[[:space:]]*\('
    '\.renderScale[[:space:]]*='
    'PlayerPrefs\.(Set|Delete|Save)'
    '(slider|toggle|dropdown|quality|preset)\.value[[:space:]]*='
    'ServerConfig[^;]*(Master|Sfx|Music|Ambience|Voice|Ui)Volume'
    '_clientConfig\.Config\.[A-Za-z0-9_]+[[:space:]]*='
    '_clientConfig\.Save[[:space:]]*\('
    '(FindAnyObjectByType|FindFirstObjectByType|FindObjectsByType)<Canvas>'
    'using[[:space:]]+UnityEngine\.UI;'
    'new[[:space:]]+GameObject\('
    'GameObject\.Find(GameObjectWithTag|GameObjectsWithTag)?\('
    'SceneManager\.LoadScene\('
    'FindObjects?OfType<'
    '\bInput\.(GetKey|GetKeyDown|GetKeyUp|GetButton|GetButtonDown|GetMouseButton|mousePosition|GetAxis|anyKey)\b'
    '\b(StartCoroutine|StopCoroutine)\s*\('
    '\bAudioSource\b'
    '\bDontDestroyOnLoad\s*\('
    '\bScreen\.SetResolution\s*\('
    '\bThread\.Sleep\s*\('
    '\bGC\.Collect\s*\('
    '\bCamera\.(allCameras|current)\b'
    '\bTime\.timeScale\s*='
    'new\s+(WebClient|HttpClient)\s*\('
    'Shader\.WarmupAllShaders'
)

RULE_NAMES=(
    'static Instance singleton'
    'ServiceLocator access'
    'ad-hoc InputAction'
    'fractional lighting-field fitting'
    'exact camera zoom cache comparison'
    'Camera.main outside GameplayCamera'
    'FPS cap outside DisplayManager'
    'VSync ownership outside DisplayManager'
    'runtime Texture2D construction outside RuntimeTextureFactory'
    'runtime image decoding outside RuntimeTextureFactory'
    'controller-local UI Toolkit stylesheet'
    'manual screen-to-panel Y flip'
    'UI root sized from Screen dimensions'
    'duplicated radiance-cascade count policy'
    'ad-hoc gameplay camera lookup'
    'manual manager/service construction'
    'graphics preset mutation outside ClientConfigManager'
    'graphics quality snapshot mutation outside ClientConfigManager'
    'MSAA ownership outside LightingEngine'
    'Unity quality-level ownership outside LightingEngine'
    'URP render-scale ownership outside LightingEngine'
    'settings persistence in PlayerPrefs'
    'notifying UI settings refresh'
    'audio volume in ServerConfig'
    'direct ClientConfig field mutation'
    'unowned ClientConfig persistence'
    'screen-space uGUI Canvas lookup'
    'screen-space uGUI namespace'
    'runtime GameObject construction outside SceneObjectFactory'
    'global unscoped GameObject lookup'
    'synchronous scene loading outside SceneCoordinator'
    'deprecated FindObject(s)OfType call'
    'legacy Input Manager call (use UnityEngine.InputSystem)'
    'legacy MonoBehaviour coroutines (use UniTask)'
    'Unity AudioSource usage (FMOD Studio is the sole audio engine)'
    'DontDestroyOnLoad outside BootstrapLifetimeScope'
    'Screen.SetResolution outside DisplayManager'
    'blocking Thread.Sleep in gameplay/async code'
    'manual GC.Collect in runtime gameplay'
    'unmanaged camera lookup (use GameplayCamera.Resolve)'
    'unowned Time.timeScale mutation'
    'ad-hoc HTTP client (use ClientAssetLoader or UnityWebRequest)'
    'Shader.WarmupAllShaders in URP (throws keyword space assert)'
)

# Per-rule path exemptions. Tests may construct tiny fixture textures directly;
# production runtime code may not. Generated/vendored code is excluded below.
ALLOW_REGEX=(
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^Assets/Scripts/Core/GameplayCamera\.cs$'
    '^Assets/Scripts/Rendering/DisplayManager\.cs$'
    '^Assets/Scripts/Rendering/DisplayManager\.cs$'
    '^(Assets/Editor/|Assets/Scripts/AssetPipeline/RuntimeTextureFactory\.cs|Assets/Scripts/Tests/)'
    '^(Assets/Editor/|Assets/Scripts/AssetPipeline/RuntimeTextureFactory\.cs|Assets/Scripts/Tests/)'
    '^$'
    '^$'
    '^$'
    '^$'
    '^Assets/Scripts/Core/GameplayCamera\.cs$'
    '^$'
    '^(Assets/Scripts/Core/ClientConfigManager\.cs|Assets/Scripts/World/Lighting/Lighting(ConfigHolder|Engine)\.cs)$'
    '^Assets/Scripts/Core/ClientConfigManager\.cs$'
    '^Assets/Scripts/World/Lighting/LightingEngine\.cs$'
    '^Assets/Scripts/World/Lighting/LightingEngine\.cs$'
    '^Assets/Scripts/World/Lighting/LightingEngine\.cs$'
    '^(Assets/Editor/.*|Assets/Scripts/Networking/Auth/AuthTokenManager\.cs|Assets/Scripts/UI/AuthGate\.cs|Assets/Scripts/UI/GatewayController\.cs)$'
    '^$'
    '^$'
    '^$'
    '^(Assets/Scripts/Rendering/GraphicsSettingsController\.cs|Assets/Scripts/Rendering/DisplayManager\.cs|Assets/Scripts/World/Lighting/Lighting(ConfigHolder|Engine)\.cs)$'
    '^$'
    '^$'
    '^(Assets/Editor/.*|Assets/Scripts/Editor/.*|Assets/Scripts/Tests/.*|Assets/Scripts/Core/Lifecycle/SceneObjectFactory\.cs|Assets/Scripts/Game/.*)$'
    '^(Assets/Editor/|Assets/Scripts/Editor/|Assets/Scripts/Tests/)'
    '^Assets/Scripts/Tests/'
    '^$'
    '^$'
    '^$'
    '^(Assets/Editor/|Assets/Scripts/Editor/|Assets/Scripts/Tests/)'
    '^(Assets/Editor/|Assets/Scripts/Core/BootstrapLifetimeScope\.cs|Assets/Scripts/Tests/)'
    '^Assets/Scripts/Rendering/DisplayManager\.cs$'
    '^(Assets/Editor/|Assets/Scripts/Tests/)'
    '^(Assets/Editor/|Assets/Scripts/Tests/)'
    '^$'
    '^(Assets/Scripts/UI/PauseMenu\.cs|Assets/Scripts/Game/Managers/GameManager\.cs|Assets/Scripts/Tests/)'
    '^(Assets/Editor/|Assets/Scripts/Tests/)'
    '^$'
)

# Narrow line-level exemptions for the canonical declaration of a shared
# policy. Path-wide exemptions would let a second copy sneak into the file.
ALLOW_CONTENT_REGEX=(
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    'return atlasDimension <= 256 \? 3 : 4;'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
    '^$'
)

if [ "${#PATTERNS[@]}" -ne "${#RULE_NAMES[@]}" ] ||
    [ "${#PATTERNS[@]}" -ne "${#ALLOW_REGEX[@]}" ] ||
    [ "${#PATTERNS[@]}" -ne "${#ALLOW_CONTENT_REGEX[@]}" ]; then
    echo -e "${RED}${BOLD}[ERROR]${NC} Lint configuration error: rule metadata arrays have different lengths." >&2
    echo "  PATTERNS count:            ${#PATTERNS[@]}" >&2
    echo "  RULE_NAMES count:          ${#RULE_NAMES[@]}" >&2
    echo "  ALLOW_REGEX count:         ${#ALLOW_REGEX[@]}" >&2
    echo "  ALLOW_CONTENT_REGEX count: ${#ALLOW_CONTENT_REGEX[@]}" >&2
    exit 2
fi

# Vendored / generated code is exempt.
EXCLUDE_REGEX='^(Assets/Scripts/VContainer/|Assets/Plugins/|Packages/|Library/)'

declare -a files=()
if [ "$#" -gt 0 ]; then
    files=("$@")
else
    while IFS= read -r file; do
        files+=("$file")
    done < <(rg --files Assets/Scripts Assets/Editor -g '*.cs' 2>/dev/null || find Assets/Scripts Assets/Editor -name '*.cs' | sort)
fi

if [ "${#files[@]}" -eq 0 ]; then
    echo -e "${YELLOW}No C# files found to scan.${NC}"
    exit 0
fi

declare -a production_files=()
for file in "${files[@]}"; do
    if [ ! -f "$file" ] || [[ "$file" =~ $EXCLUDE_REGEX ]]; then
        continue
    fi

    production_files+=("$file")
done

if [ "${#production_files[@]}" -eq 0 ]; then
    echo -e "${YELLOW}No production C# files to scan.${NC}"
    exit 0
fi

echo -e "${CYAN}${BOLD}=== Fodinae Architectural Pattern Linter ===${NC}"
echo -e "Scanning ${BOLD}${#production_files[@]}${NC} files against ${BOLD}${#PATTERNS[@]}${NC} architectural rules..."
echo ""

failed=0
violation_count=0

for index in "${!PATTERNS[@]}"; do
    pattern="${PATTERNS[$index]}"
    allow_regex="${ALLOW_REGEX[$index]}"
    while IFS= read -r line; do
        [ -z "$line" ] && continue
        file="${line%%:*}"
        if [[ -n "$allow_regex" && "$file" =~ $allow_regex ]]; then
            continue
        fi

        location_and_content="${line#*:}"
        line_number="${location_and_content%%:*}"
        content="${location_and_content#*:}"
        if [[ "$content" =~ ^[[:space:]]*(//|/\*|\*|///) ]]; then
            continue
        fi

        allow_content_regex="${ALLOW_CONTENT_REGEX[$index]}"
        if [[ -n "$allow_content_regex" && "$content" =~ $allow_content_regex ]]; then
            continue
        fi

        echo -e "${RED}${BOLD}[VIOLATION]${NC} ${YELLOW}${RULE_NAMES[$index]}${NC}"
        echo -e "  File: ${BOLD}$file:$line_number${NC}"
        echo -e "  Code: ${CYAN}$content${NC}"
        echo ""
        failed=1
        ((violation_count++))
    done < <(grep -nHE "$pattern" "${production_files[@]}" 2>/dev/null || true)
done

# Run Deep Semantic DI & Lifecycle Analyzer
if [ -f "scripts/check_di_lifecycle.py" ]; then
    if ! python3 scripts/check_di_lifecycle.py; then
        failed=1
    fi
fi

end_time=$(date +%s)
duration=$((end_time - start_time))

if [ "$failed" -ne 0 ]; then
    echo -e "${RED}${BOLD}✖ FAILED:${NC} Found ${BOLD}$violation_count${NC} architectural violation(s) across ${BOLD}${#production_files[@]}${NC} files (${duration}s)."
    echo ""
    echo -e "${BOLD}Architectural Standards & Replacements:${NC}"
    echo "  - static 'Instance' singletons              -> use VContainer DI"
    echo "  - ServiceLocator                            -> constructor / DI injection"
    echo "  - 'new InputAction(...)'                    -> configure in InputSystem_Actions.inputactions"
    echo "  - legacy coroutines (StartCoroutine)        -> use UniTask / CancellationToken"
    echo "  - legacy Input (Input.Get*)                 -> use UnityEngine.InputSystem (Keyboard.current/Mouse.current)"
    echo "  - AudioSource components                    -> use FMOD Studio (IAudioSystem / AudioSystem)"
    echo "  - Camera.main / Camera.allCameras           -> use GameplayCamera.Resolve()"
    echo "  - targetFrameRate / VSync / SetResolution   -> DisplayManager is the single owner"
    echo "  - runtime Texture2D construction/decoding   -> use RuntimeTextureFactory"
    echo "  - UI Toolkit stylesheets in controllers     -> use PanelSettings.themeUss (@import)"
    echo "  - screen-to-panel coordinate conversion     -> use RuntimePanelUtils.ScreenToPanel"
    echo "  - UI element sizing from Screen.dimensions  -> use PanelSettings & USS flex layout"
    echo "  - manager/service runtime creation          -> register and resolve through VContainer"
    echo "  - graphics preset/quality mutation          -> use ClientConfigManager"
    echo "  - MSAA, quality-level, URP render-scale     -> LightingEngine is the owner"
    echo "  - settings persistence in PlayerPrefs       -> use ClientConfigManager (client_config.json)"
    echo "  - UI settings notifications                 -> use SetValueWithoutNotify"
    echo "  - runtime GameObject construction           -> use ISceneObjectFactory"
    echo "  - unscoped GameObject.Find / FindWithTag    -> prohibit global scene searches (use DI or FindInOwnScene)"
    echo "  - synchronous SceneManager.LoadScene        -> use ISceneCoordinator or SceneManager.LoadSceneAsync"
    echo "  - deprecated FindObject(s)OfType            -> use FindObjectsByType / FindAnyObjectByType"
    echo "  - Unity classes namespace syntax            -> use block namespace { } for MonoBehaviour/ScriptableObject"
    exit 1
fi

echo -e "${GREEN}${BOLD}✔ PASSED:${NC} All ${BOLD}${#production_files[@]}${NC} production files conform to ${BOLD}${#PATTERNS[@]}${NC} architectural rules (${duration}s)."
exit 0
