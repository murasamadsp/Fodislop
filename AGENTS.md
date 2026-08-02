# AGENTS.md — инструкции для клиента Fodinae

Fodinae — 2D MMORPG-песочница на Unity 6 (`6000.5.0f1`), URP 2D 17.5, C# 12, UI Toolkit, UniTask и сетевые пакеты `darkar25.fodinae.*` из MinesServerNetworking.

## 1. Обязательные стандарты

- Обычные типы используют file-scoped namespace. Типы Unity, наследующие `MonoBehaviour`, `ScriptableObject`, `ScriptableRendererFeature` или `VolumeComponent`, используют block namespace: file-scoped namespace для них может дать `MonoScript.GetClass() == null`.
- Включён `#nullable enable`: все поля, свойства и параметры явно nullable/non-null (`string?`, `null!`). Использовать primary constructors, `readonly record struct` и collection expressions `[]`, где это уместно.
- Global usings находятся в `Fodinae.Core.GlobalUsings` и покрывают `Fodinae.Core`, `AssetPipeline`, `Audio`, `Networking`, `World`, `Game`, `Player`, `UI`, `Effekseer`.
- StyleCop: Allman braces; обязательные `{}`; пустая строка после `}` (SA1513), но не перед `}` (SA1508); trailing comma в многострочных инициализаторах; пустая строка перед `//`.

Клиент получает от сервера лёгкое состояние (координаты и идентификаторы), а тяжёлые текстуры, спрайты и FMOD-банки загружает один раз on-demand. Кэширование: RAM (`AssetCache`, `CellTextureCache`) → диск (`PersistentAssetCache`, ETag/MD5) → CDN/сервер. Рендеринг после загрузки выполняется локально.

## 2. Карта проекта

```text
Assets/
  Editor/                 BuildScript, FmodBankBuilder, MapbConverter, utilities
  Plugins/                FMOD, UniTask и vendored DLL
  Prefabs/                Player.prefab
  Resources/              UI, Styles, Programmator, Skills, profiles
  Scenes/                 MainGame.unity и Tests/
  Scripts/
    AssetPipeline/       загрузка и кэш ассетов
    Audio/                FMOD Backend/Core/Spatial
    Core/                 DI, сервисы, GameLifetimeScope, global usings
    Editor/               Unity editor utilities
    Game/                 managers, robots, packs, VFX
    Networking/           connection, PacketHandler, processors, tests
    Player/               input, movement, camera, interaction
    Rendering/            URP post-processing
    UI/                   builders, HUD, chat, map, Programmator, controls
    World/                map, terrain, atlas, lighting, world rendering
    Tests/                Core/UI/World tests
  Settings/               URP, renderer, input and volume profiles
  Shaders/                terrain, background, lighting, post-processing
  StreamingAssets/       FMOD banks and local maps
  Textures/, UI Toolkit/  графика и UI-темы
scripts/                  pre-commit-lint.sh, setup-hooks.sh, helpers
.agents/skills/           fmod-sync и run-linter
```

## 3. Архитектура

### DI и жизненный цикл

CompositionRoot и `SingletonMonoBehaviour` удалены. Единственная точка регистрации — `GameLifetimeScope` (VContainer, `BeforeSceneLoad`). `RegisterManager<T>` ищет объект через `FindAnyObjectByType<T>()`, регистрирует найденный экземпляр или создаёт новый GameObject.

В `BuildCallback` нужно:

1. вызвать `ServiceLocator.Initialize(resolver)`;
2. явно разрешить критические ленивые компоненты, включая `PostProcessController`;
3. инжектировать `SingleMeshTerrainRenderer` и вызвать `EnsureSubscriptions()`;
4. просканировать все активные и неактивные `MonoBehaviour` с `[Inject]` и вызвать `resolver.Inject()`;
5. выполнить `ValidateStartup()` и проверить критические поля: `PacketHandler`, `PauseMenu`, `PlayerHUDView`, `InventoryView`, `PlayerMovementController`, `MapManager`, `WorldTextureManager`, `ClientAssetLoader`, `AudioSystem`, `SingleMeshTerrainRenderer`.

Регистрируются `MapStorage`, `InventoryModel`, `PlayerStatsModel` как instances; managers: `MapManager`, `SingleMeshTerrainRenderer`, `ClientAssetLoader`, `AudioSystem`, `WorldTextureManager`, `ServerAudioEventManager`, `ConnectionManager`, `PacketHandler`, `NetworkService`, `GameManager`, `VFXPool`, `PackManager`, `RobotManager`, `TentacleBatchRenderer`, `ServerConfig`, `TextureStorageManager`, `GlobalChatUI`, `UIInputManager`, `FPSCounter`, `FloatingChatManager` — с соответствующими интерфейсами из `Core/Interfaces`.

`ServiceLocator` содержит только `Initialize(IObjectResolver)` и `Resolve<T>()`. Для старого монолитного кода допустимы `Instance`; новый код использует `[Inject]`.

### Сеть и UI

- `NetworkService`/`PacketHandler`/`ConnectionManager` — сервисы подписок, диспетчеризации, авторизации и реконнекта. `DummyConnection` — offline transport, делает только connect/status/events и **не создаёт UI**.
- Процессоры обрабатывают world, map, chat, clan, audio, windows, inventory, stats, player, robots, packs, missions и config. Packet UI строится из `OpenWindowPacket` через `PacketUIBuilderFactory`.
- Единственный источник обычного UI — `GameManager.SetupUI()` под выключенным `_uiRoot`; `AuthorizeUI()` его активирует. `FindAnyObjectByType` не видит inactive объекты.
- Сервер авторитетен при закрытии packet-окон: ESC/кнопка отправляют запрос, локально окно не скрывать; закрытие происходит только после `CloseWindowPacket`.
- `PacketHandler.IsInputBlocked` вычисляется как `HasOpenWindows || IsModalShowing || PauseMenu.IsMenuOpen || ProgrammatorGrid.IsOpen`. `PauseMenu` сначала отдаёт ESC программатору, затем отправляет серверный close верхнего окна.
- `WindowBinding` использует SmartFormat. Inventory — модель/presenter/view, 9×6 + хотбар; HUD включает HP, энергию, баффы, авто-копку и Programmator. Chat состоит из global/local/floating компонентов.
- `MainMenu` загружает `Resources/UI/MainMenu.uxml`; после сборки UI фиксированный `PanelSettings` нужно восстановить, иначе элементы могут отображаться, но не принимать события.

### Мир и координаты

- Серверные координаты: левый верхний угол `(0, 0)`, X вправо, Y вниз. Все преобразования — через `CoordinateUtils`; всегда учитывать `MapManager.WorldHeight`.
- `MapManager` получает `WorldInitPacket`/`MapRegionPacket`; `MapStorage` хранит чанки 32×32, `persistentDataPath/*.mapb`, и уведомляет renderer через `OnCellChanged()`.
- `WorldLayer<T>` — дисковый streaming, LRU RAM-кэш, RLE и append-only запись с компактификацией. Текстуры загружаются из файловой системы, не из Resources/Addressables.
- `SingleMeshTerrainRenderer` рисует видимый мир одним mesh (7 UV-каналов, sorting order `-1000`) и обновляет изменения дифференциально. `SurfaceRenderer` обслуживает Transit/Perspective.
- `TerrainCellCache` привязан к мировой сетке шагом 8: при движении сохранять пересечение и заполнять только новые полосы. Zoom-кэш квантовать по 32 клеткам, уменьшать через 0.4 с после стабилизации; не аллоцировать ресурсы каждый кадр.
- `AnimationContainerDecoder` поддерживает PNG/GIF/WebP; анимация тайла не меняет его окклюзию или emission.

### Игрок

- Единственный источник позиции — `PlayerMovementController.Position` (`Vector2Int`, server Top-Left). `ClientPosition` и `ServerPosition` не возвращать.
- `IPlayerInput → PlayerInputHandler`: WASD/стрелки — движение, Space — копка, E — авто-копка, L — агрессия, Shift — бег. Валидация — `Passable` на клиенте и `MovePacket` на сервере.
- `DigCooldown = 0.3f` блокирует и повторную копку, и движение. Направление — `_lastSentDirection`, по умолчанию `Direction.Down`. Dummy отправляет SFX пустой клетки до проверки `Empty`.

### FMOD

`AudioSystem`/`FmodAudioBackend` используют FMOD Studio C++ Engine. Банки скачиваются через `ClientAssetLoader`, кэшируются на диске и загружаются `loadBankFile` по пути; feature-банки загружаются/выгружаются on-demand. 3D-звук привязывать нативно через `AttachInstanceToGameObject`, зоны используют Snapshots и global parameters. Шины: Master, SFX, Music, Voice, Ambience, UI. FMOD-проект: `FodinaeAudio/FodinaeAudio.fspro`.

Примеры: `Play2D`, `PlayAttached`, `PlaySnapshot`, `SetGlobalParameter`, `SetBusVolume`. `ServerAudioEventManager` принимает `SFXPacket`, проигрывает 3D-звук и создаёт визуальное событие.

### Программатор

`ProgrammatorGrid` — визуальный редактор робота: список программ → сетка → Save/Run/Stop. Данные программ сессионные (`_programItems` в RAM); единственный файл — `programmator.json`, сохраняемый через `JsonUtility`. Run/Stop пока визуальные. Сетка 16×12, `CELLSIZE=32`, `CELL_GAP=2`, контейнер 608 px, панель 648 px. `_popup` содержит `dimmer`, `_programListPanel`, `_panel`; `_createDialog` — абсолютный overlay. ESC: grid → список с сохранением, список → закрытие, dialog закрывается только ×/Отмена.

### Lighting и terrain invariants

- Единственный источник emission — серверный `CellConfigProperties.Glowing` (в Dummy выставлять тот же флаг), не `CellType` и не клиентские allow/deny-листы. Цвет можно брать из `CellConfigurationPacket.Color`.
- Используются world-anchored emissive clusters 2×2 и per-tile списки источников; нельзя возвращать CPU sweep или глобальный цикл «каждый пиксель × все источники». Mesh получает один `RequiredTerrainPadding` под viewport, радиус света и safe border.
- Visibility строится height-aware SDF cone tracing с интеграцией optical thickness: opaque блок закрывает свет, alpha пропускает пропорционально. Высота отвечает за длину тени, cone radius — за penumbra, coverage/density — за пропускание.
- Receiver self-skip разрешён только внутри исходной клетки; после выхода из неё соседние opaque samples снова поглощают свет. Ambient добавляется ровно один раз.
- `OcclusionCoverage` в `Terrain.shader` — единственный источник формы окклюдера; CPU-чтение сырого alpha запрещено. Coverage/SDF проходят через `ToOcclusionGrid` и `_OcclusionYFlip = SystemInfo.graphicsUVStartsAtTop`; spatial mismatch исправлять не коэффициентами.
- SDF: `InitializeSdfSeeds → JumpFloodSdf → FinalizeSdf`, кэш по региону, revision карты/атласа и lighting settings. `FilterLighting` — только edge-aware reconstruction direct light; AO хранится в alpha, ambient/AO не смешивать с direct visibility. Eigengrau не часть lighting reconstruction.
- Профили `Low/Medium/High/Ultra` меняют только разрешение, лимит источников, шаги SDF и частоту обновления. Профиль хранится в `PlayerPrefs` под `WorldLightingQuality`, источник пресетов — `TerrariaLightingEngine.ApplyQualityPreset`; Ultra: 8 texel/cell, до 2048, 64 шага. Normal map/Lambert пока не реализованы.

## 4. Unity, YAML и код

- `.prefab`, `.unity`, `.asset` нельзя редактировать текстом. Менять их только через Unity Editor API/Inspector; сохранять GUID и `.meta`.
- Имя файла Unity-скрипта должно совпадать с классом. После создания/переименования проверять `MonoScript.GetClass()` в Editor: `dotnet build` этого не проверяет.
- `VolumeProfile.Add<T>()` создаёт component только в памяти; editor-код обязан вызвать `AssetDatabase.AddObjectToAsset()` до `SaveAssets()`.
- `Renderer2D.asset` содержит ровно одну активную `PostProcessRendererFeature`. Post-process применяется к базовой камере; world-space UI на слое `UI` рисуется отдельной Overlay `WorldUICamera` без post-process; UI Toolkit/Screen Space Overlay идёт позже.
- Внутренний URP HDR (`supportsHDR`) включён для lighting/bloom, HDR display отключён через `SdrOutputEnforcer`. Не отключать URP HDR ради SDR.
- Motion blur строит velocity только для удалённых `Robot` с `MotionBlurTag`; локального игрока исключать. Передавать реальные sprite texture и GPU matrices; teleport delta сбрасывать.
- Terrain material не затемнять relief/connectivity или `u-v`/`u+v` градиентами; затемнение только через `_WorldLightTexture`.
- DI — VContainer; singleton-паттерн только `Instance + Awake`, без `SingletonMonoBehaviour`. Асинхронность — UniTask, связь — `Action`.

Именование: типы/публичные члены/константы — `PascalCase`, private поля — `_camelCase`, параметры/локальные — `camelCase`. FMOD events, сетевые теги и CDN-пути — lowercase/snake_case. `docs/` содержит только автономные HTML с inline `<style>`, без Markdown и внешних зависимостей.

## 5. Критические нюансы

1. Рендеринг ждёт `MapStorage.IsReady`, которое становится true после `WorldInitPacket`.
2. `DummyConnection._cellConfigs` и тестовые конфигурации создать **до** `WorldInitPacket`, иначе `MapManager` не сможет ломать клетки.
3. Инверсия Y — частый баг; проверять Top-Left server coordinates и `WorldHeight`.
4. Текстуры не в Resources; билд вручную копирует `Textures/`.
5. `RegisterInstance` не инжектит вручную созданные экземпляры; для объектов с `[Inject]` использовать регистрацию через VContainer или явный `resolver.Inject()`.
6. `FPSCounter.OnDestroy()` должен удалить созданный им `_ownedCanvas`.
7. `ProgrammatorGrid` ширина контейнера: `COLS * (CELLSIZE + CELL_GAP * 2 + 2f)`; `+2f` обязателен из-за border.
8. UI Toolkit не поддерживает `calc()`: использовать готовое число или inline-стиль из C#.

## 6. Workflow и диагностика

- Основная сцена — `Assets/Scenes/MainGame.unity`; offline режим даёт `DummyConnection`.
- Сборка: `BuildScript.BuildMacOS` из `Assets/Editor/`; стандартный Build Settings не копирует текстуры.
- Сцена должна содержать/инициализировать `TerrainMesh`, `SingleMeshTerrainRenderer`, `UIDocument`, `Main Camera`, `Global Light 2D`, `SceneSetup`, `MapManager`, `GameLifetimeScope`.
- F12 пишет runtime snapshot в `diagnostic.txt`, F11 повторно сканирует `[Inject]` в `inject_diagnostic.txt`. Edit Mode: `Fodinae/Diagnostics/Validate Injections`. Статический анализ — `scripts/inject_analysis.py`.

## 7. Линтинг C# (обязательно)

Используются Roslyn-анализаторы StyleCop (`SA`), NetAnalyzers (`CA`), Roslynator (`RCS`), Sonar (`S`) и Unity (`UNT`). После генерации/изменения C# запускать:

```bash
dotnet build Assembly-CSharp.csproj -maxcpucount -p:UseSharedCompilation=true -nodeReuse:true -clp:NoSummary 2>&1
```

Все предупреждения `SA`, `CA`, `RCS`, `S`, `UNT` исправить до финального ответа. Не использовать `--no-verify` и не обходить pre-commit hooks; при зависании разобраться с причиной.

`Directory.Build.props` подключает анализаторы, `.stylecop.json` отключает нерелевантные Unity-правила, `.editorconfig` задаёт severity. Для `[Inject]` полей использовать `= null!;`.

## 8. Правила пользователя

- Не добавлять фичи без явного запроса; при вопросе об отсутствии чего-либо сначала уточнить требуемое поведение.
- Проверки (build/lint) запускать осмысленно, не без необходимости.
- Не выбирать ленивые фоллбеки и временные решения.
- **Запрещено вручную менять префабы, ассеты и сцены.**

## 9. Принцип No Implicit Defaults и Fail-Fast

- **Никаких неявных дефолтов и тихих фоллбеков**: Запрещено подставлять неявные дефолтные позиции `(0,0)`, генерировать случайные/дефолтные текстуры или тестовые карты при отсутствии оригинальных файлов, а также гасить ошибки тихими `try/catch`.
- **Fail-Fast**: При отсутствии/повреждении `.mapb` файлов, конфигов или сетевых данных немедленно прерывать процесс (`TriggerDisconnect`/исключение), не пытаясь разворачивать временные структуры.
- **Синхронизация загрузки мира**: При старте игры `MainMenu` удерживает защитный экран загрузки (`LoaderContainer`) и скрывает его только после события `GameManager.OnWorldLoaded` (когда `MapStorage.IsReady == true`, каскады и меши террейна готовы, а камера позиционирована на спавн).
- **Моментальное позиционирование камеры**: При инициализации спавна `CameraFollow` выполняет немедленный мгновенный сдвиг (`SnapToTarget()`), исключая слепой полёт камеры через карту из точек по умолчанию.
- **Запрет O(Heap) обходов в рантайме**: Запрещено использовать `Resources.FindObjectsOfTypeAll`, глобальное сканирование рефлексией по всем скриптам сцены или некэшированные вызовы `GetComponent` в `Update`/`LateUpdate`/`PostProcessRenderPass`.
