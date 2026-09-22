# Kern Project Context

Kern is a 2D MMORPG sandbox built with Unity 6 (`6000.6.0f1`), URP 2D 17.6,
C# 12, UI Toolkit, UniTask, and the `darkar25.fodinae.*` networking packages.
Read only the sections relevant to the current task. General agent rules live in
the root `AGENTS.md`.

---

## 0. Unity authority boundary

The agent must not launch, open, close, restart, control, or inspect Unity
Editor/Hub, Unity CLI, Unity MCP, Editor APIs, batch mode, builds, tests, asset
imports, scenes, prefabs, materials, logs, or renders unless the current user
explicitly requests that specific Unity operation.

Ordinary source files (`.cs`, `.shader`, `.hlsl`, `.uxml`, `.uss`, and
documentation) may be edited and checked without Unity. If completion requires
Unity, report the exact operation that remains for the user.

## 0.1. No rollback without explicit authorization

Do not reset, restore, clean, revert, rebase, amend, force-push, or otherwise
rewrite Git state without an explicit request naming the operation and target.
Never undo user-owned working-tree changes. Do not use `--no-verify`.

## 1. Mandatory development standards

### C# and Unity types

- Use file-scoped namespaces for ordinary types.
- Types deriving from `MonoBehaviour`, `ScriptableObject`,
  `ScriptableRendererFeature`, or `VolumeComponent` use block namespaces so
  `MonoScript.GetClass()` remains valid.
- Nullable reference types are enabled. Annotate every reference explicitly.
- Use Allman braces, mandatory braces, SA1513/SA1508 spacing, trailing commas in
  multiline initializers, and a blank line before `//` comments.
- Types, public members, and constants use `PascalCase`; private fields use
  `_camelCase`; parameters and locals use `camelCase`.
- FMOD events, network tags, and CDN paths use `lowercase/snake_case`.

### Serialization, assets, and documentation

- Never edit `.prefab`, `.unity`, or `.asset` files as text. Use Unity Editor
  APIs/Inspector and preserve GUIDs and `.meta` files.
- A Unity script filename must match its class name. Verify `MonoScript.GetClass()`
  after renaming; `dotnet build` cannot perform this check.
- `VolumeProfile.Add<T>()` creates an in-memory component. Editor code must call
  `AssetDatabase.AddObjectToAsset()` before saving.
- Documentation under `docs/` is self-contained HTML with inline styles and no
  external dependencies.

## 2. Scenes and startup

Production scenes and build order:

1. `Bootstrap.unity` (index 0): `BootstrapLifetimeScope` and persistent managers.
2. `Gateway.unity` (index 1): authentication gateway.
3. `MainMenu.unity` (index 2): main-menu UI only, without the game DI graph.
4. `MainGame.unity` (index 3): `GameLifetimeScope`, gameplay, and offline mode.

The only scene-transition path is `BootstrapLifetimeScope.TransitionAsync`. Each
transition creates a `SceneTransitionTicket`, passes it to the child scope with
`LifetimeScope.EnqueueParent`, and registers it in the child container.
Serialized `ParentReference` lookups and scene searches for scopes are forbidden.

The child composition root must call `Attach` exactly once, then
`RequestActivation`, `MarkStartupReady`, and `MarkPresentationReady`. Startup
failure calls `Fail`, unloads the candidate scene, and returns the UI to a
diagnostic state.

`BootstrapLifetimeScope` owns application services. `GatewayLifetimeScope` and
`MainMenuLifetimeScope` own typed references, controllers, and their bootstrap.
`GameLifetimeScope` owns models, processors, factories, the scene contract, and
`GameBootstrap`.

Returning from game to menu uses `ReturnToMainMenu`: disconnect, prepare game
teardown, then load the menu again. MainMenu remains loaded as a descent/loading
layer until `GameManager.WorldReady`; only then is the previous scene unloaded.

## 3. DI and lifecycle

The project uses stock VContainer. `CompositionRoot` and
`SingletonMonoBehaviour` are removed. UniTask provides asynchronous work;
models, gateways, and `Action` events provide cross-system communication.

`BootstrapLifetimeScope` runs at `DefaultExecutionOrder -30000` and owns
persistent managers such as `ConnectionManager`, `NetworkService`, `AudioSystem`,
`ClientConfigManager`, and `ClientAssetLoader`. `GameLifetimeScope` runs at
`-20000` and owns game services.

`RegisterManager<T>` requires a serialized typed `ManagerBinding` contract and
registers scene references with `RegisterComponent`. Name lookup, runtime
fallbacks, missing bindings, and duplicate bindings are forbidden. The editor
contract migrator only writes references; it does not move or repair scene data.

Never call `AddComponent` for managers inside `Configure`: it can invoke
`Awake`/`OnEnable` before injection. Never resolve the container from
`Awake`/`OnEnable`/`Start`. Use direct `[Inject]` dependencies; `IObjectResolver`
is limited to composition roots and factories. `RegisterInstance` does not
inject manually constructed objects.

Startup order is:

1. wait for `ticket.WaitForActivationAsync()`;
2. activate the authored `Services` root after DI;
3. initialize configuration, networking, processors, assets, and terrain hooks;
4. apply terrain, post-processing, lighting, and surface settings;
5. initialize UI services;
6. validate required shaders, compute shaders, and project defaults, then connect;
7. mark startup ready;
8. wait for world, terrain, surface, lighting, assets, and required FMOD banks;
9. mark the scope and presentation ready.

Adding a subsystem means registering it in `GameLifetimeScope.Configure` and in
the corresponding `GameStartupPipeline` phase. The pipeline is the source of truth.

## 4. Client subsystems

### Networking and caching

The server supplies lightweight state and identifiers. Heavy textures, sprites,
and FMOD banks are loaded on demand once. The cache hierarchy is RAM, persistent
disk cache with versioned ETag/length/SHA-256 manifest, then CDN/server.

`PacketHandler` only dispatches packets to processors and owns subscriptions. It
must not contain UI, scene managers, or player state. Packet logic belongs in
`Networking/Processors/*` and updates models, gateways, and domain services.

`DummyConnection` is the offline transport. `IOfflineScenarioSettings` selects
one deterministic negative scenario for the lifetime: authentication rejection,
disconnect during handshake, handshake timeout, or world-init timeout.

### UI Toolkit

`GameManager.SetupUI()` is the sole builder for ordinary UI. Build it once under
the disabled `_uiRoot`, then authorize it. Server windows are authoritative:
ESC and UI buttons send close requests; local hiding waits for `CloseWindowPacket`.

The MainMenu document has sorting order 100 and acts as the full-screen loader.
The MainGame document has sorting order 0 and stays beneath it until the world is
ready. Do not raise the game document above the menu.

For new or rewritten UI:

1. `PanelSettings.themeUss` imports `KernTheme.tss`; controllers must not add
   duplicate style sheets.
2. Static structure belongs in UXML. C# only binds data and callbacks; dynamic
   lists and grids may create elements.
3. Layout uses flexbox and stretch rules, not `Screen.width`, `Screen.height`,
   or absolute `top`/`left` calculations.
4. Visibility uses the `is-hidden` class and `UIState`; do not set inline
   `display`. `UIVisibilityAnimator` owns transitions.
5. Build UI and subscribe callbacks once; unsubscribe in `OnDisable`.
6. Screen coordinates enter a panel only through `RuntimePanelUtils.ScreenToPanel`.
7. No `EventSystem` or `InputSystemUIInputModule` is created. Keyboard navigation
   is suppressed; UI interaction is mouse-only.

### World, chunks, and coordinates

Server coordinates use top-left origin `(0, 0)` with Y downward. Every conversion
must use `CoordinateUtils` and `MapManager.WorldHeight`.

`MapStorage` stores 32×32 chunks and raises `OnCellChanged`. Rendering waits for
`MapStorage.IsReady`, which follows `WorldInitPacket`. `WorldLayer<T>` provides
disk streaming, an LRU RAM cache, RLE, append-only writes, and compaction.

Terrain and lighting windows are separate caches. Terrain preserves the overlap
when the streaming origin moves and builds only entered bands. Camera-dependent
geometry uses quantized coverage caches; do not rebuild meshes for every smooth
camera-position, zoom, or aspect change.

### Terrain and surface rendering

`TerrainRenderer` draws the visible world as one mesh with seven UV channels and
sorting order `-1000`. `SurfaceRenderer` is registered and resolved through
`GameLifetimeScope`; never construct a second instance manually.

Runtime textures are created through `RuntimeTextureFactory` in canonical RGBA32,
without mipmaps, with explicit color space, filter, and wrap modes. Atlas copies
must validate dimensions and graphics format. Diagnostic textures for missing
assets are intentional and must not be removed.

### Player and input

The authoritative local position is `PlayerMovementController.Position` in
server top-left coordinates. Movement uses WASD/arrows, Space digs, E enables
auto-dig, L attacks, and Shift runs. `PlayerInteractionController` sends a
`ClickCellPacket` only when the pointer is outside UI after `ScreenToPanel` conversion.

`IInputBlocker` has one implementation, `UI/InputBlockState`, composed from chat
focus, server windows, modals, pause menu, and Programmator state. Gameplay and
camera consumers inject the interface; they must not access UI singletons directly.

### Lighting

The active pipeline is GPU Radiance Cascades:
`LightingMaterialField`/`EmissionField` → `SolveCascade` → `ResolveDirect` →
`SolveDiffuseBounce` → `CompositeLighting`. Legacy SDF, raymarch, AO-neighbor,
blur, CPU sweep, readback, and runtime fallback paths must not return.

The server `CellConfigProperties.Glowing` flag is the only emission source;
emission color comes from `CellConfigurationPacket.Color`. Material RGB is albedo
for one diffuse bounce and alpha is physical occupancy. Beer–Lambert extinction,
direct radiance, transmission, and AO remain separate quantities.

AO is a persistent full-resolution `RHalf` field derived from occupancy. It is
recomputed only after geometry revision, lighting-region/field-size changes, or
AO-setting changes. Light-source movement does not invalidate AO.

The lighting field uses an integer number of texels per cell (`4`, `3`, `2`, or
`1`). Fractional scales are forbidden. Region size and reanchoring come from
`StreamingGovernor`; fixed cell-step movement is forbidden. Reuse field textures
only when `gridSize * integerScale` matches exactly.

Dynamic lighting follows smooth source positions every frame and is never tied to
cell entry or throttled by a timer. DDA calls remain in transport stages only.

### Camera, post-processing, and HDR

Gameplay components receive `IGameplayCamera` through DI. Direct `Camera.main` is
forbidden. There is one application camera owned by Bootstrap; other scenes must
not contain cameras.

Menu scenery renders into controller-owned render textures. The menu rig is on
its own layer and excluded from the gameplay camera and volume mask.

The working color space is scene-referred linear HDR. Lighting and bloom use HDR
buffers; display output is reconciled by the URP-owned transform. Custom display
transforms must not duplicate URP tone mapping or gamut conversion. Paper white,
peak brightness, and output mode are controlled by the display settings contract.

IMGUI is reserved for author tools. Player UI remains UI Toolkit and follows the
design-system token rules. The grading workbench stores its own persistent JSON,
not client gameplay configuration.

### Audio and Programmator

FMOD banks are downloaded through `ClientAssetLoader`, cached on disk, and loaded
on demand. Bus hierarchy: `Master`, `SFX`, `Music`, `Voice`, `Ambience`, `UI`.

`ProgrammatorGrid` is a 16×12 editor with `CELLSIZE = 32` and `CELL_GAP = 2`.
Programs are session data; only `programmator.json` is persisted. ESC returns from
the grid to the program list, closes the list, or leaves the creation dialog only
through its explicit close/cancel controls.

## 5. Critical invariants

1. Rendering waits for `MapStorage.IsReady` after `WorldInitPacket`.
2. Dummy cell configurations exist before `WorldInitPacket`.
3. Coordinate conversion preserves server top-left Y-down semantics.
4. Runtime textures are external build artifacts, not `Resources` assets.
5. Lifecycle methods do not resolve containers.
6. Window teardown tolerates a destroyed `UIDocument`; cleanup must not break
   `OnDestroy`.
7. Terrain animation speed is valid for a single frame; `FrameOffset = 0` is valid.
8. Input blocking is only through `IInputBlocker`, and mouse conversion uses
   `ScreenToPanel`.

## 6. Workflow, diagnostics, and optimization

Unity caches, shader caches, import caches, editor layout caches, VSync, monitor
refresh rate, Retina resolution, and editor overhead are never root-cause
explanations for performance defects. Analyze source code, serialized data,
configuration, runtime state, algorithms, allocations, and CPU/GPU work.

Do not mask defects with FPS caps, frame skipping, artificial delays, throttling,
or reduced simulation/render frequency. Optimize by removing real work while
preserving required per-frame updates.

Settings are validated by executing `tools/Kern.SettingsProbe`; compilation alone
does not validate reflection-based attributes or defaults. Dead-member counts are
an architectural debt budget, not permission to add public dead APIs.

PlayMode tests use `PlayModeHarness`, offline authentication, virtual input, and
deterministic `IDummyClock`. Soak tests are separate from ordinary PlayMode runs.
`dotnet build` verifies syntax and types only; gameplay requires PlayMode or Unity
MCP verification when explicitly authorized.

Use the existing F1 diagnostics window, `FPSCounter`, and subsystem bypasses before
adding new counters. Do not make changes based on unverified hypotheses.
