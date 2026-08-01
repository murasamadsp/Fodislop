# AGENTS.md - Инструкции для работы с клиентом Fodinae

## 1. Обзор проекта

**Fodinae** — 2D-клиент игры на Unity (URP) с тайловым рендерингом мира и сетевым обменом данными. MMORPG песочница с программатором.

- **Язык & Ядро**: Modern C# (C# 12, `<LangVersion>12.0</LangVersion>`, `#nullable enable` включены глобально).
- **Движок**: Unity 6 (`6000.5.0f1`).
- **Рендер**: Universal Render Pipeline 2D (`com.unity.render-pipelines.universal` 17.5.0).
- **Сетевое взаимодействие**: Пакеты `darkar25.fodinae.*` (data, networking, connection) — подключены как Git-зависимости из [MinesReborn/MinesServerNetworking](https://github.com/MinesReborn/MinesServerNetworking).
- **Интерфейс**: UI Toolkit — пакетная сборка окон из `OpenWindowPacket` через `PacketUIBuilderFactory` (Canvas, Panel, Grid, Text, TextBox, Image, Selectable, Slider, Dropdown, ScrollView, Line, DockPanel), Binding (SmartFormat), Programmator (визуальное программирование), кастомные контролы (Selectable, RegexTextField, UILine, ChatInputBlinker).
- **Асинхронность**: `UniTask` (vendored в `Assets/Plugins/UniTask/`).

### 1.1 Стандарты C# 12 и Архитектурные Правила

1. **Namespaces**: File-scoped namespace (`namespace Fodinae.Domain;`) используется для обычных C#-типов. **Исключение:** любой Unity-сериализуемый тип, наследующий `MonoBehaviour` или `ScriptableObject` (включая `ScriptableRendererFeature` и `VolumeComponent`), обязан использовать block namespace `namespace Fodinae.Domain { ... }`. В Unity `6000.5.0f1` file-scoped namespace для таких типов может успешно пройти Roslyn-компиляцию, но оставить `MonoScript.GetClass() == null`, что приводит к `None (Script)` и `No script asset`.
2. **Nullable Reference Types**: Глобально включен `#nullable enable`. Все поля, свойства и параметры явно размечаются (`string?`, `null!`).
3. **Primary Constructors & Record Structs**: Легковесные структуры и хендлы используют `readonly record struct` и первичные конструкторы.
4. **Collection Expressions**: Использование `[]` вместо `new List<T>()` или `new T[]`.
5. **Global Usings**: В `Fodinae.Core.GlobalUsings` объявлены глобальные usings для всех основных пространств имён: `Fodinae.Core`, `Fodinae.AssetPipeline`, `Fodinae.Audio.*`, `Fodinae.Networking.*`, `Fodinae.World.*`, `Fodinae.Game.*`, `Fodinae.Player.*`, `Fodinae.UI.*`, `Fodinae.Effekseer`.

### 1.1.1 Форматирование и правила StyleCop

1. **Allman Style скобки**: Каждая открывающая фигурная скобка `{` пишется с новой строки (`csharp_new_line_before_open_brace = all`). Ключевые слова `else`, `catch`, `finally` также начинаются с новой строки.
2. **Обязательность скобок `{}`**: Однострочные `if`, `else`, `foreach` блоки обязательно обволакиваются фигурной скобкой (SA1503).
3. **Пустые строки вокруг скобок**: После закрывающей скобки `}` обязательна пустая строка (SA1513). Перед закрывающей скобкой `}` пустая строка запрещена (SA1508).
4. **Trailing Comma**: Многострочные списки, массивы и инициализаторы завершаются запятой `,` (SA1413).
5. **Комментарии**: Перед строчным комментарием `//` обязательна пустая строка (SA1515).

### 1.2 Архитектурная концепция клиенто-серверного взаимодействия

Архитектура Fodinae построена на четком разделении тяжелого рендеринга и легкого сетевого состояния:

1. **Примитивы рендеринга на клиенте**: Клиент содержит готовые системы отрисовки и воспроизведения (тайловый мир `SingleMeshTerrainRenderer`, сущности роботов `RobotManager`, предметы на земле `PackManager`, эффекты `ServerAudioEventManager`).
2. **Легковесный сетевой поток данных**: Сервер передает клиенту только чистые координаты и идентификаторы состояний (где стоят роботы, какие предметные паки лежат, какие ячейки попадают в радиус прорисовки, какие звуки вызваны).
3. **Ленивая однократная загрузка тяжелых ассетов (On-Demand Fetching)**: При первом появлении ранее неизвестного объекта (новый блок, скин робота, иконка пака, аудио-банк) клиент запрашивает бинарные ассеты (текстуры, спрайты, `.bank`) с CDN/сервера один раз.
4. **Кэширование и локальный рендер**: Все полученные ассеты сохраняются в стойком дисковом кэше `PersistentAssetCache` (с ETag/MD5 валидацией) и ОЗУ (`CellTextureCache`, `AssetCache`). В дальнейшем клиент выполняет тяжелый рендеринг исключительно из локального кэша без повторных сетевых запросов.

## 2. Структура проекта

```text
Assets/
  Editor/                        # Скрипты редактора и билдеры (BuildScript, FmodBankBuilder, MapbConverter)
    BuildScript.cs               # Сборка билдов
    CsProjFix.cs                 # csproj постпроцессор
    FmodBankBuilder.cs           # Синк FMOD-банков
    MapbConverter.cs             # Конвертер серверных карт
    SdrOutputEnforcer.cs
  Effekseer/
    Resources/
  Materials/
    CornerMask.mat
    SurfaceMaterial.mat
  Plugins/                       # Vendored DLL (UniTask, NetCoreServer, ZstdSharp, SmartFormat)
    FMOD/                        # Vendored пакет
    UniTask/                     # Vendored пакет
    ExtendedNumerics.BigDecimal.dll
    Genumerics.dll
    IsExternalInit.System.Runtime.CompilerServices.dll
    K4os.Compression.LZ4.dll
    NCalc.Core.dll
    NCalc.Sync.signed.dll
    NetCoreServer.dll
    Parlot.dll
    SharpCompress.dll
    SmartFormat.dll
    System.Collections.Immutable.dll
    System.Reflection.Emit.ILGeneration.dll
    System.Reflection.Emit.Lightweight.dll
    ZString.dll
    ZstdSharp.dll
  Prefabs/                       # Префабы сущностей (Player.prefab)
    Player.prefab
  Resources/
    Programmator/                # 166 ассетов/изображений
    Skills/                      # Иконки скиллов
    Styles/                      # USS стили UI
    UI/
      MainMenu.uxml
    EffekseerSettings.asset
    GraphicsQualityProfile.asset
  Scenes/                        # Игровые сцены (MainGame.unity, Tests/TextureStorageTestScene.unity)
    Tests/                       # Тестовые сцены
      TextureStorageTestScene.unity
    MainGame.unity
  Scripts/
    AssetPipeline/               # Ассет-пайплайн (ClientAssetLoader, PersistentAssetCache)
      AnimatedSpriteData.cs
      AssetCache.cs
      ClientAssetLoader.cs       # Загрузка ассетов с сервера/локально
      DynamicImage.cs
      ETagCalculator.cs
      PersistentAssetCache.cs    # Стойкий кэш ассетов (ETag, MD5)
    Audio/                       # Аудио подсистема FMOD
      Backend/                   # Низкоуровневый FMOD API и AudioSystem
        AudioSystem.cs           # Синглтон-контроллер аудио
        FmodAudioBackend.cs      # Низкоуровневый FMOD API
      Core/                      # Аудио-слои и хендлы воспроизведения
        AudioLayer.cs
        AudioPlaybackHandle.cs
      Spatial/                   # Пространственное 3D-аудио и аудио-зоны
        AudioSpatial.cs
        AudioZone.cs
        WorldAudioController.cs
    Core/                        # Системная инфраструктура и VContainer LifetimeScope
      DI/
        IServiceLocator.cs
      Interfaces/                # Интерфейсы сервисов
        IAssetLoader.cs
        IAudioSystem.cs
        IConnectionService.cs
        IInputBlocker.cs
        IMapDataProvider.cs
        INetworkService.cs
        IPackService.cs
        IPlayerStats.cs
        IRobotService.cs
        IServerAudioService.cs
        IServerConfig.cs
        ITextureService.cs
        ITextureStorageService.cs
        IVFXService.cs
        IWorldDataStorage.cs
      GameConstants.cs
      GameLifetimeScope.cs       # LifetimeScope для сцены: регистрация DI
      GlobalUsings.cs
      ObjectPool.cs
      ServiceLocator.cs
      SharedMaterialCache.cs
      TentaclePool.cs
    Editor/
      FixPlayerPrefabUtility.cs
      FixRenderer2DFeaturesUtility.cs
      GuidTruthDump.cs
      InjectValidator.cs
      PostProcessVolumeAssetCreator.cs
    Effekseer/                   # Эффекты Effekseer
      RuntimeEffekseerLoader.cs
    Game/                        # Игровые сущности и менеджеры
      Managers/                  # Менеджеры игры (MapManager, RobotManager, PackManager)
        GameManager.cs           # Точка входа в игру
        ItemRegistry.cs
        MapManager.cs            # Жизненный цикл мира
        MapStorage.cs            # Хранилище карты (.mapb)
        PackManager.cs
        RobotManager.cs          # Управление роботами
        ServerAudioEventManager.cs
        ServerConfig.cs
      Pack.cs
      Robot.cs
      RobotHeadlight.cs
      ServerAudioEvent.cs
      Tentacle.cs
      TentacleBatchRenderer.cs
      VFXPool.cs
      VFXType.cs
    Networking/                  # Сетевой слой и диспетчер пакетов
      Auth/
        AuthTokenManager.cs
      Connection/
        Client/
          DummyConnection.cs
          TextureStorageManager.cs
        ConnectionManager.cs
      Processors/
        AudioPacketProcessor.cs
        ChatProcessor.cs
        ClanProcessor.cs
        ClientConfigProcessor.cs
        ConnectionProcessor.cs
        IPacketProcessor.cs
        InventoryProcessor.cs
        MapRegionProcessor.cs
        MissionArrowProcessor.cs
        MissionProcessor.cs
        OpenURLProcessor.cs
        PackProcessor.cs
        PlayerInfoProcessor.cs
        PlayerStateProcessor.cs
        PlayerStatsProcessor.cs
        RobotInfoProcessor.cs
        RobotPositionProcessor.cs
        StatusProcessor.cs
        WindowPacketProcessor.cs
        WorldInitProcessor.cs
      Tests/
      NetworkService.cs          # Подписка/отписка пакетов
      PacketHandler.cs           # Диспетчер сетевых пакетов
    Player/                      # Логика игрока и камера
      Input/
        PlayerInputHandler.cs
      Interfaces/
        IPlayerInput.cs
      Logic/
        PlayerMovementController.cs # Ввод, движение, копка
      CameraFollow.cs
      PlayerInteractionController.cs
      PlayerMovementBoundaryTests.cs
    Rendering/
      PostProcessing/
        Components/
          BloomComponent.cs
          ChromaticAberrationComponent.cs
          ColorGradingComponent.cs
          EigengrauComponent.cs
          MotionBlurComponent.cs
          VignetteComponent.cs
        MotionBlurTag.cs
        PostProcessController.cs
        PostProcessRenderPass.cs
        PostProcessRendererFeature.cs
        PostProcessVolumeRegistration.cs
      GraphicsQualityProfile.cs
    Tests/
      Core/
      UI/
        InventoryModelTests.cs
        PlayerStatsModelTests.cs
      World/
        CoordinateUtilsTests.cs
        TileBitmaskConverterTests.cs
    UI/                          # UI Toolkit контроллеры, окна и программатор
      Binding/
        LogiCalcFormatter.cs
        WindowBinding.cs
      Builders/                  # UI-билдеры сетевых пакетов
        CanvasPacketBuilder.cs
        DockPanelPacketBuilder.cs
        GridPacketBuilder.cs
        ImagePacketBuilder.cs
        IntDropdownPacketBuilder.cs
        LinePacketBuilder.cs
        PacketUIBuilder.cs
        PacketUIBuilderBase.cs
        PacketUIBuilderFactory.cs
        PanelPacketBuilder.cs
        ScrollViewerPacketBuilder.cs
        SelectablePacketBuilder.cs
        SliderPacketBuilder.cs
        StringDropdownPacketBuilder.cs
        TextBoxPacketBuilder.cs
        TextPacketBuilder.cs
      Controls/
        ChatInputBlinker.cs
        RegexTextField.cs
        Selectable.cs
        UILine.cs
      HUD/                       # HUD и инвентарь
        Controllers/
        Inventory/
          Interfaces/
            IInventoryModel.cs
          Model/
            InventoryModel.cs
            ItemData.cs
          Presenter/
            InventoryPresenter.cs
          View/
            InventoryView.cs
        Player/
          Model/
            PlayerStatsModel.cs
            StatusLineEntry.cs
          Presenter/
            PlayerHUDPresenter.cs
          View/
            PlayerHUDView.cs
      Programmator/
        ObserverJoystick.cs
        ProgrammatorData.cs
        ProgrammatorGrid.cs
        ProgrammatorTextureRegistry.cs
        RadialMenu.cs
      AssetLoadingIndicator.cs
      ChatInput.cs
      ClickContextResolver.cs
      Dock.cs
      FPSCounter.cs
      FloatingChatBubble.cs
      FloatingChatManager.cs
      GlobalChatUI.cs
      LocalChatPopup.cs
      MainMenu.cs
      MinimapController.cs
      MissionArrowUI.cs
      ModalWindowHandler.cs
      PauseMenu.cs
      ReconnectUI.cs
      StyleApplicator.cs
      Tooltip.cs
      UIAnimator.cs
      UIGizmosController.cs
      UIInputManager.cs
      UIStack.cs
      WorldMapController.cs
      WorldMapRenderer.cs
    VContainer/                  # Vendored VContainer 1.19
    World/                       # Мир и тайловый рендеринг (SingleMeshTerrainRenderer)
      Extensions/
        WorldLayerTextureExtensions.cs
      Lighting/
        TerrariaLightingEngine.cs
      Terrain/
        TerrainCellCache.cs
        TerrainMeshBuilder.cs
        TerrainMetadata.cs
        TerrainPrecalculator.cs
        TerrainRenderer.cs
        TerrainVertex.cs
      AnimationContainerDecoder.cs
      AtlasCell.cs
      AtlasCoordinate.cs
      BackgroundFloodFill.cs
      CellTextureCache.cs
      CoordinateUtils.cs
      FodinaeGizmos.cs
      Rectangle.cs
      RenderingConstants.cs
      SceneSetup.cs
      SingleMeshTerrainRenderer.cs # Один меш на весь террейн, 7 UV-каналов
      StandaloneWorldInitializer.cs
      SurfaceRenderer.cs
      TextureAtlas.cs
      TileBitmaskConverter.cs
      WorldBackgroundSetup.cs
      WorldLayer.cs
      WorldTextureManager.cs
    DiagnosticRunner.cs
    InjectDiagnostic.cs
  Settings/                      # URP и Renderer2D конфиги
    Scenes/
      URP2DSceneTemplate.unity
    DefaultVolumeProfile.asset
    InputSystem_Actions.inputactions
    Lit2DSceneTemplate.scenetemplate
    PostProcessVolumeProfile.asset
    Renderer2D.asset
    UniversalRP.asset
    UniversalRenderPipelineGlobalSettings.asset
  Shaders/                       # URP 2D Шейдеры
    Lighting/
      WorldLighting.compute
    PostProcessing/
      MotionBlur.compute
      PostProcess.compute
      Velocity.shader
    BackgroundCompositor.shader
    Terrain.shader
    WorldObjectWithBackground.shader
  StreamingAssets/               # FMOD банки и локальные карты
  TextMesh Pro/                  # TMP шрифты и шейдеры
  Textures/                      # Текстуры тайлов, сущностей, UI и эффектов
  UI Toolkit/                    # UI Toolkit темы и PanelSettings

scripts/                         # Вспомогательные Python и Bash скрипты
  pre-commit-lint.sh             # Прекоммит-хук: Roslyn-анализаторы
  setup-hooks.sh
  update-agents-structure.js     # Авто-обновление структуры в AGENTS.md

.agents/                         # Правила и навыки для AI-ассистентов Antigravity / Codex
  skills/
    fmod-sync/
      SKILL.md
    run-linter/
      SKILL.md
```

## 3. Архитектура систем

### 3.0 DI и сервисы (VContainer)

**CompositionRoot и SingletonMonoBehaviour полностью удалены.** DI построен на VContainer с единой точкой регистрации в `GameLifetimeScope`.

#### Lifecycle

`GameLifetimeScope` наследует `LifetimeScope` (VContainer) и выполняется в `BeforeSceneLoad`:

1. **`Configure(IContainerBuilder)`** — регистрация сервисов (详见 таблица ниже). Для каждого типа `T` вызывается `RegisterManager<T>`, который:
   - Ищет существующий объект в сцене через `FindAnyObjectByType<T>()`.
   - Если найден — регистрирует через `RegisterInstance(existing)` + `resolver.Inject(existing)` в BuildCallback.
   - Если не найден — создаёт новый GameObject через `RegisterComponentOnNewGameObject<T>(Lifetime.Singleton)`.
2. **`BuildCallback`** (после построения контейнера):
   - Инициализирует `ServiceLocator.Initialize(resolver)`.
   - Явно резолвит все сервисы для принудительной инстанциации (ConnectionManager → NetworkService → MapManager → PacketHandler → ... → FloatingChatManager).
   - `RegisterComponentOnNewGameObject<T>` ленивый: одной регистрации недостаточно. Каждый критический runtime-компонент без гарантированного потребителя обязан явно резолвиться здесь. В частности, `PostProcessController` должен быть разрешён до первого кадра, иначе custom post-process работает, но `UI` остаётся в Main Camera и получает bloom/eigengrau/color grading.
   - Инжектит `SingleMeshTerrainRenderer` и вызывает `EnsureSubscriptions()`.
   - **Сканирует ВСЕ активные MonoBehaviour** в сцене через reflection, находит те что имеют `[VContainer.InjectAttribute]` на полях, и вызывает `resolver.Inject(mb)`.
   - Вызывает `ValidateStartup()`.
3. **`ValidateStartup()`** — проверяет что ни одно критическое `[Inject]` поле не осталось null (PacketHandler, PauseMenu, PlayerHUDView, InventoryView, PlayerMovementController, MapManager, WorldTextureManager, ClientAssetLoader, AudioSystem, SingleMeshTerrainRenderer).

#### Таблица регистрации сервисов

| Регистрация | Интерфейсы | Тип |
| --- | --- | --- |
| `RegisterInstance(MapStorage)` | `IWorldDataStorage` | Синглтон |
| `RegisterInstance(InventoryModel)` | `IInventoryModel` | Синглтон |
| `RegisterInstance(PlayerStatsModel)` | `IPlayerStats` | Синглтон |
| `RegisterManager<MapManager>` | `IMapDataProvider`, `MapManager` | MonoBeh |
| `RegisterManager<SingleMeshTerrainRenderer>` | (self) | MonoBeh |
| `RegisterManager<ClientAssetLoader>` | `IAssetLoader`, `ClientAssetLoader` | MonoBeh |
| `RegisterManager<AudioSystem>` | `IAudioSystem`, `AudioSystem` | MonoBeh |
| `RegisterManager<WorldTextureManager>` | `ITextureService`, `WorldTextureManager` | MonoBeh |
| `RegisterManager<ServerAudioEventManager>` | `IServerAudioService`, `ServerAudioEventManager` | MonoBeh |
| `RegisterManager<ConnectionManager>` | `IConnectionService`, `ConnectionManager` | MonoBeh |
| `RegisterManager<PacketHandler>` | `PacketHandler` | MonoBeh |
| `RegisterManager<NetworkService>` | `INetworkService`, `NetworkService` | MonoBeh |
| `RegisterManager<GameManager>` | (self) | MonoBeh |
| `RegisterManager<VFXPool>` | `IVFXService`, `VFXPool` | MonoBeh |
| `RegisterManager<PackManager>` | `IPackService`, `PackManager` | MonoBeh |
| `RegisterManager<RobotManager>` | `IRobotService`, `RobotManager` | MonoBeh |
| `RegisterManager<TentacleBatchRenderer>` | (self) | MonoBeh |
| `RegisterManager<ServerConfig>` | `IServerConfig`, `ServerConfig` | MonoBeh |
| `RegisterManager<TextureStorageManager>` | `ITextureStorageService`, `TextureStorageManager` | MonoBeh |
| `RegisterManager<GlobalChatUI>` | (self) | MonoBeh |
| `RegisterManager<UIInputManager>` | `IInputBlocker`, `UIInputManager` | MonoBeh |
| `RegisterManager<FPSCounter>` | (self) | MonoBeh |
| `RegisterManager<FloatingChatManager>` | (self) | MonoBeh |

#### ServiceLocator

`ServiceLocator` — тонкий bridge-прослойка (только `Initialize` + `Resolve<T>`), делегирует VContainer. Старый `ConcurrentDictionary`, `Register`, `Unregister`, `TryResolve` удалены.

```csharp
// Только эти два метода:
public static void Initialize(IObjectResolver resolver)
public static T Resolve<T>() where T : class
```

Для резолва предпочтительны прямые обращения через `.Instance` синглтонов (монолитный код) или `[Inject]` (после миграции на LifetimeScope).

### 3.1 Сетевой слой (Networking)

- **NetworkService**: Синглтон с `Awake()` инициализацией (`_instance = this`). Подписка: `Subscribe<T>` / `Unsubscribe<T>`.
- **PacketHandler**: Диспетчеризация пакетов через подписки (Processors). **Static свойства**: `IsInputBlocked` (вычисляет из `HasOpenWindows || IsModalShowing || PauseMenu.IsMenuOpen || ProgrammatorGrid.IsOpen`), `TopWindowTag`. `Instance` — стандартный синглтон через `Awake()`.
- **ConnectionManager**: Синглтон. Управление подключением, авторизация, реконнект. При `Connect()` создаёт `DummyConnection` или TCP-соединение из Git-пакета `MinesServer.Networking.Connection.Client`.
- **DummyConnection**: Оффлайн-режим. `ConnectAsync()` минимальный — `await UniTask.Yield()`, установка статуса, `OnConnected`. **Не создаёт UI-объекты** — это ответственность `GameManager.SetupUI()`.
- **TextureStorageManager**: Загрузка и кэширование текстур с сервера.
- **Processors**: `WorldInitProcessor`, `MapRegionProcessor`, `ClanProcessor`, `AudioPacketProcessor`, `WindowPacketProcessor`, `ChatProcessor`, `RobotPositionProcessor`, `RobotInfoProcessor`, `PackProcessor`, `StatusProcessor`, `MissionProcessor`, `PlayerInfoProcessor`, `InventoryProcessor`, `PlayerStatsProcessor`, `PlayerStateProcessor` и др.
- **Пакетный UI**: Динамическая сборка UI из `OpenWindowPacket` через `PacketUIBuilderFactory`.

### 3.2 Мир и Рендеринг (World & Rendering)

- **MapManager**: Жизненный цикл мира (`WorldInitPacket`, `MapRegionPacket`), конфигурации ячеек, тайл-группы. Реализует `IMapDataProvider`.
- **MapStorage**: Хранилище данных карты (чанки 32x32). Кэширует в `persistentDataPath/*.mapb`. Реализует `IWorldDataStorage`. `SetCell()` оповещает `SingleMeshTerrainRenderer.OnCellChanged()`.
- **WorldLayer\<T\>**: Дисковый стриминг с LRU-кэшем в RAM. RLE-сжатие. Append-only запись с компактификацией.
- **WorldTextureManager**: Загружает тайл-текстуры из файловой системы (не Resources/Addressables), упаковывает в `TextureAtlas`.
- **SingleMeshTerrainRenderer**: Один меш на весь видимый террейн. 7 UV-каналов (атлас, тайлинг, анимация, тени, рельеф). `Sorting Order = -1000`. `OnCellChanged` + `LateUpdate` для дифференциального обновления. Синглтон — самоуничтожается при дубликате.
- **SurfaceRenderer**: Дополнительные меши для Transit (переходы между слоями) и Perspective (перспективные блоки). Два материала, отдельные Sorting Orders.
- **CellTextureCache**: ConcurrentDictionary-кэш текстур ячеек для быстрой загрузки из файловой системы. Хранит `Texture2D` по `CellType`.
- **AtlasCoordinate**: Структура координат ячейки в текстурном атласе.
- **AnimationContainerDecoder**: Декодирование PNG/GIF/WebP-файлов в массивы спрайтов для анимированных тайлов и эффектов.
- **Координаты**: Левый верхний угол карты — это серверные координаты `(0, 0)`. Ось X растет вправо, ось Y растет вниз (вглубь шахты). Все пространственные конвертации централизованы в утилите `CoordinateUtils`.

### 3.3 Игрок и Управление

- **PlayerMovementController**: Ввод через New Input System. Единственный источник истины позиционирования игрока — свойство `Position` (`Vector2Int` в серверных координатах Top-Left `0:0`). Устаревшие псевдонимы `ClientPosition` и `ServerPosition` полностью устранены. Клиентская валидация по `Passable` + серверная через `MovePacket`.
- **PlayerInteractionController**: Обработка кликов и клавиш (копка, использование предметов). Отправляет `DigRequestPacket`, `ItemUsePacket` и т.д.
- **CameraFollow**: Следование камеры за игроком.
- **Ввод**: `IPlayerInput` → `PlayerInputHandler`. WASD/стрелки — движение, **Пробел** — копка (`spaceKey.isPressed`), E — авто-копка, L — агрессия, Shift — бег.
- **Dig-механика**:
  - Кулдаун копания (`DigCooldown = 0.3с`) блокирует и повторную копку, **и движение**.
  - `ApplyMovement()` проверяет `_input.WantsToDig || Time.time - _lastDigTime < DigCooldown`.
  - Направление копки — `_lastSentDirection` (инициализируется `Direction.Down`).
  - Звук копания пустоты играется, клетка не ломается (DummyConnection шлёт SFX до проверки на Empty).

### 3.4 Аудио-домен (Audio)

Аудио-домен построен полностью идиоматично под **FMOD Studio C++ Engine**.

**Архитектура:**

```
Audio/
  Core/                         # Ядро и типы
    AudioBusType.cs             # Enum шин: Master, SFX, Music, Voice, Ambience, UI
    AudioLayer.cs               # Параметры звука: шина (SFXDefault/UIDefault/etc), volume, pitch, IsSpatial
    AudioPlaybackHandle.cs      # Прямая обёртка над FMOD.Studio.EventInstance (Stop, SetPosition, SetVolume, SetParameter)
  Backend/                      # FMOD Studio Бэкенд
    FmodAudioBackend.cs         # Низкоуровневый FMOD API: loadBankFile, AttachInstanceToGameObject, Snapshots, Global Parameters
    AudioSystem.cs              # Синглтон: API Play, PlayAttached, PlaySnapshot, SetGlobalParameter, SetBusVolume
  Spatial/
    AudioSpatial.cs             # Компонент на GameObject: нативная привязка 3D-звука к трансформу (AttachInstanceToGameObject)
    AudioZone.cs                # Триггерная зона: запускает FMOD Snapshots (snapshot:/...) и выставляет Global Parameters
```

**FMOD интеграция (MMO & Zero-RAM Waste):**

1. Банки `.bank` скачиваются с игрового CDN через `ClientAssetLoader.GetAssetPathAsync` (ETag-кеширование на диск)
2. Загрузка в FMOD выполняется через `loadBankFile` с дискового пути (без напрасного дублирования банков в RAM)
3. **Нативное 3D-позиционирование**: `FMODUnity.RuntimeManager.AttachInstanceToGameObject()` транслирует координаты и повороты объектов на C++ стороне FMOD без C#-поллинга в кадрах.
4. Динамические фиче-банки подгружаются на лету через `AudioSystem.Instance.EnsureBankLoadedAsync("Zone_Name.bank")` и выгружаются через `UnloadBank()`
5. **FMOD Snapshots**: `AudioZone` активирует нативные Snapshots микшера (настройки акустики/фильтров), не затирая пользовательские настройки громкости.
6. FMOD проект: `FodinaeAudio/FodinaeAudio.fspro` (в корне репозитория)
7. 6 шин FMOD мапятся на `AudioBusType` (bus:/, bus:/sfx, bus:/music, bus:/voice, bus:/ambience, bus:/ui).

**Примеры использования:**

```csharp
AudioSystem.Instance.Play2D("ui/click");
AudioSystem.Instance.PlayAttached("robot_engine", gameObject);
var snapshot = AudioSystem.Instance.PlaySnapshot("snapshot:/cave_ambient");
AudioSystem.Instance.SetGlobalParameter("Depth", 450f);
AudioSystem.Instance.SetBusVolume(AudioBusType.SFX, 0.8f);
```

- **ServerAudioEventManager**: Принимает `SFXPacket` от сервера, запускает 3D-звук в FMOD через `AudioSystem.Instance.PlayAt()` и создаёт `ServerAudioEvent` для рендеринга спрайтов/Effekseer.

### 3.5 Ассеты и кэширование (Asset Loading)

- **ClientAssetLoader**: Загрузка ассетов с сервера (GET-запросы) или локально из файловой системы.
- **PersistentAssetCache**: Стойкий кэш в `persistentDataPath`. Хранит ETag + MD5 для валидации, пропускает повторную загрузку неизменных файлов.
- **AssetCache**: Вспомогательный кэш ассетов в оперативной памяти (RAM).
- **ETagCalculator**: MD5-хэш данных для ETag-заголовка.
- **DynamicImage**: `MonoBehaviour` с `UnityEngine.UI.Image`, загружающий спрайт с сервера по URL. Работает через `ClientAssetLoader` + `PersistentAssetCache`.
- **Пайплайн загрузки ассетов (Локальный CDN)**:
  1. Запрос ассета (`GetTextureAsync`, `GetAudioAsync` и т.д.) поступает в RAM-кэш `AssetCache`. При промахе опрашивается дисковый кэш `PersistentAssetCache`.
  2. Если ассет есть локально на диске, отправляется HTTP-запрос с ETag. При ответе `304 Not Modified` ассет считывается с диска. Если файл обновился или отсутствует, скачивается новый поток байт.
  3. Параллельные запросы к одному файлу объединяются (coalescing) через `TaskCompletionSource`, предотвращая дублирование сетевого трафика.

### 3.6 UI-системы

- **Пакетный UI** (см. 3.1): Динамическая сборка окон из `OpenWindowPacket` — фабрика `PacketUIBuilderFactory` и несколько типовых билдеров (Canvas, Panel, Grid, Text, Slider, Dropdown, ScrollView, Line, DockPanel...).
- **GameManager.SetupUI()**: Создаёт все UI-объекты (MinimapController, InventoryView, PlayerHUDView, PauseMenu, GlobalChatUI + LocalChatPopup + FloatingChatManager) под выключенным `_uiRoot`. `_uiRoot` активируется через `GameManager.AuthorizeUI()`. **Единственный источник создания UI — DummyConnection не создаёт UI.**
- **Принцип авторитета сервера при закрытии окон (ESC)**: Нажатие клавиши `ESC` или клик по кнопке закрытия окна **НЕ ДОЛЖНЫ** принудительно скрывать пакетное окно локально на клиенте. Клиент отправляет пакет закрытия окна на сервер. Только сервер решает, закрывается ли окно, и отправляет клиенту подтверждающий пакет закрытия (`CloseWindowPacket`). Локальное самовольное закрытие окна клиентом ломает состояние `PacketHandler.IsInputBlocked`, из-за чего игрок вечно застревает без возможности движения.
- **PacketHandler.IsInputBlocked (static)**: Вычисляется как `HasOpenWindows || IsModalShowing || PauseMenu.IsMenuOpen || ProgrammatorGrid.IsOpen`. Нет отдельного поля `_inputBlocker` — логика инлайн в static property.
- **PauseMenu**: Меню паузы с настройкой всех 6 шин громкости FMOD. `IsMenuOpen` — static property. ESC-логика: сначала проверяет `ProgrammatorGrid.IsOpen` (программатор обрабатывает ESC сам), затем `PacketHandler.IsInputBlocked` для отправки `ElementClickPacket` на закрытие верхнего окна.
- **Binding**: `WindowBinding` привязывает данные через `SmartFormat`. Сканирует VisualElement-дерево, ищет именованные поля ввода (источники) и Label с SmartFormat-шаблонами (потребители), пересчитывает при любом изменении.
- **Инвентарь**: `InventoryView` (сетка 9×6 + хотбар 9 ячеек), `InventoryModel` (данные), `InventoryPresenter` (презентер), `ItemData` (тип/количество).
- **HUD**: Хотбар, HP, энергия, баффы, кнопки (включая авто-копку и программатор).
- **Карта**: `WorldMapController` (управление, переключение режима), `WorldMapRenderer` (рендеринг текстуры из `MapStorage`).
- **Чат**: `GlobalChatUI` (история + ввод), `LocalChatPopup`, `FloatingChatManager`/`FloatingChatBubble` (всплывающие сообщения над персонажами), `ChatInput` (блокировка управления при фокусе).
- **FPSCounter**: Счётчик FPS + Ping + Online. Создаёт standalone `FPSCanvas` если нет существующего Canvas. `OnDestroy()` уничтожает `_ownedCanvas` для предотвращения утечек.
- **MainMenu**: Загружает `MainMenu.uxml` из Resources. Имеет фикс PanelSettings — перезапись `panelSettings` после сборки UI для предотвращения бага UI Toolkit с нерегистрируемыми ивентами (UI рендерится но кнопки не кликаются, проявляется рандомно ~каждый второй запуск).

### 3.7 Программатор (Programmator)

Программатор — визуальный редактор алгоритмов поведения робота. Открывается через кнопку в HUD игрока (`PlayerHUDView.cs:988`). `static bool IsOpen` проверяется `PacketHandler.IsInputBlocked` и `PauseMenu` для блокировки ESC.

**Навигация:**

```
HUD [Программатор]
  ↓ Show()
  [Список программ] → клик по программе → [Сетка редактора]
                    → [+ Создать] → модальное окно с TextField → [Сетка редактора]
  × или ESC в списке → Hide()
  × или ESC в сетке → CloseProgram() → сохранение данных → [Список программ]
```

**Компоненты:**

| Компонент | Файл | Назначение |
| --- | --- | --- |
| `ProgrammatorGrid` | `ProgrammatorGrid.cs` | Главный UI: список программ, сетка, сохранение, run/stop |
| `ProgrammatorData` | `ProgrammatorData.cs` | Статические данные: Codes/Labels/Values, категории, имена операторов, undo/redo |
| `ProgrammatorTextureRegistry` | `ProgrammatorTextureRegistry.cs` | Загрузка текстур операторов из `Resources/Programmator/{id}` |
| `RadialMenu` | `RadialMenu.cs` | Двухкольцевое радиальное меню выбора категории → оператора |
| `ObserverJoystick` | `ObserverJoystick.cs` | 8-направленный джойстик для операторов Observer (drag-to-shift) |

**Структура UI сетки (`ProgrammatorGrid.cs`):**

```
_popup (absolute, fullscreen, center)
  dimmer
  _programListPanel (column, ~400px, shown при ShowProgramList())
    header: "Программы" + × close
    _listScroll (ScrollView): строки программ (клик → открыть, × удалить)
    _createContainer: [+ Создать программу] (показывает _createDialog)
  _panel (grid panel, shown при OpenProgram())
    headerRow (row)
      topRow (column, flexGrow: 1)
        buttonsRow (row): [имя программы] [<][Стр. 1/1][>][+][−][↑][↓][←][→]
        actionRow (row): [💾 Save][▶ Run][■ Stop]
      closeBtn (×) — прижат вправо
    gridRow (row, justifyCenter)
      gridScroll (VisualElement)
        _gridContainer (608px, 16×12 ячеек)
  _createDialog (absolute overlay) — модальное окно создания программы
    panel: "Новая программа" + TextField + [Отмена] [Создать]
```

**Ключевые детали реализации:**

- **Список программ сессионный**: `List<ProgramItem> _programItems` в памяти. Создание/удаление без сохранения на диск.
- `ProgramItem` содержит `Name`, `Codes/Labels/Values` как `List<>`
- При открытии программы → данные копируются в статический `ProgrammatorData`, при закрытии — копируются обратно
- **Save (💾)** сохраняет текущую программу в `programmator.json` через `JsonUtility` (единственный файл)
- **Run/Stop** — чисто визуальные (зелёная рамка панели), без логики выполнения
- **`_createDialog`**: Modal dialog with `position: Absolute` overlay, dark-styled TextField (inner `unity-text-input` darkened via `AttachToPanelEvent`), Enter confirms
- **Название программы** отображается в шапке сетки через `_programTitle` Label
- `CELLSIZE = 32f`, `CELL_GAP = 2f`, сетка `16×12 = 192` ячеек
- Ширина контейнера: `16 * (32 + 4 + 2) = 608px`
- Ширина панели: `648px` (608 + 20px padding с каждой стороны)
- CloseBtn в headerRow (не в buttonsRow) с `flexGrow: 1` на topRow для прижатия вправо

### 3.8 Диагностика

- **DiagnosticRunner** (`Assets/Scripts/DiagnosticRunner.cs`): Runtime-диагностика. Каждую секунду пишет heartbeat в `diagnostic.txt` (состояние MapManager, MapStorage, позиция игрока, ввод, соединение, роботы, террейн, камера). **F12** — полный снимок. Создаётся автоматически `GameLifetimeScope.Start()`.
- **InjectDiagnostic** (`Assets/Scripts/InjectDiagnostic.cs`): Автоскан `[Inject]` полей при старте. F11 — ручной рескан. Пишет `inject_diagnostic.txt` — список всех MonoBehaviour с `[Inject]` полями и их значения (OK/null).
- **InjectValidator** (`Assets/Scripts/Editor/InjectValidator.cs`): Edit Mode. MenuItem `Fodinae/Diagnostics/Validate Injections`. Сканирует все MonoBehaviour (включая inactive) и пишет `inject_diagnostic_editmode.txt`.
- **inject_analysis.py** (`scripts/inject_analysis.py`): Внешний Python-скрипт статического анализа. Сканирует C#-файлы на `[Inject]` поля, проверяет регистрацию в VContainer и наличие явных `resolver.Inject()` вызовов. Пишет `inject_analysis.txt`.

## 4. Стандарты разработки

### Unity & YAML

- **Прямое редактирование запрещено**: Никогда не изменять `.prefab`, `.unity` и `.asset` как текстовый YAML. Все изменения ссылок, GUID, renderer features, сцен и sub-assets выполнять только через Unity Editor API (`SerializedObject`, `AssetDatabase`, `EditorSceneManager`) или вручную в Inspector.
- **Мета-файлы**: У каждого ассета ДОЛЖЕН быть `.meta` файл. При перемещении/удалении через CLI — обрабатывать оба.
- **GUID**: Не ломайте связи между ассетами, сохраняйте GUID.
- **Unity script assets**: У класса, наследующего `MonoBehaviour`/`ScriptableObject`, имя файла обязано совпадать с именем класса. После создания или переименования проверять в Unity Editor не только сборку, но и `MonoScript.GetClass()`/Inspector: `dotnet build` не проверяет регистрацию ScriptAsset.
- **Volume Profile**: `VolumeProfile.Add<T>()` создаёт объект только в памяти. При создании профиля editor-скриптом каждый новый `VolumeComponent` обязательно сохранять через `AssetDatabase.AddObjectToAsset(component, profile)` до `AssetDatabase.SaveAssets()`. Иначе профиль сериализует `{fileID: 0}`, и настройки исчезают.
- **Custom post-processing (URP 2D)**: `PostProcessRendererFeature` обрабатывает только базовую камеру. World-space интерфейс (`Robot` nickname/clan badge, pack clan badge, chat bubble) использует Unity layer `UI`: он исключён из culling mask базовой камеры и рисуется отдельной `WorldUICamera` типа Overlay без post-processing. UI Toolkit/Screen Space Overlay рисуется ещё позже. `PostProcessController` обязательно принудительно резолвится в `GameLifetimeScope.BuildCallback` и восстанавливает camera stack только при нарушении инварианта. Не возвращать world-space UI на слой мира и не запускать custom pass для overlay-камер.
- **HDR rendering vs HDR display**: внутренний `UniversalRenderPipelineAsset.supportsHDR` обязан оставаться включённым — он сохраняет диапазон lighting/bloom до composite. При этом `PlayerSettings.allowHDRDisplaySupport` и `useHDRDisplay` выключены через `SdrOutputEnforcer`, потому что игра использует стабильный SDR output. Не пытаться отключить 10-bit/HDR Display через выключение внутреннего HDR URP: комбинация `supportsHDR=0` с активным HDR display вызывает переключения gamut и кислотные тона. Поскольку built-in URP post-processing выключен, `PostProcess.compute` сам сжимает HDR highlights единым RGB-множителем перед Eigengrau: значения `<= 1` остаются строго неизменными, чтобы tonemap не осветлял AO/тени, а значения `> 1` не клипались независимо по каналам и не теряли hue.
- **Renderer feature uniqueness**: в `Renderer2D.asset` должна существовать ровно одна активная `PostProcessRendererFeature`. Дубликат выполняет весь compute-pass повторно. `PostProcessVolumeAssetCreator` удаляет дубликаты через Editor API; не добавлять вторую feature для диагностики и не исправлять список вручную в YAML.
- **Motion Blur**: velocity-буфер строится внутри `PostProcessRenderPass` только для активных `MotionBlurTag` на удалённых `Robot`. Локальный игрок отсекается повторно через `Robot.IsLocalPlayer`; не подключать отдельный `VelocityBufferRendererFeature` и не применять blur ко всему кадру. Velocity-pass обязан явно задавать GPU view/projection matrices и реальную `sprite.texture` через `_VelocitySpriteTexture`: нельзя полагаться на implicit `_MainTex`, иначе Metal может взять белый fallback и превратить прозрачный sprite rect в движущуюся капсулу. Смещение ограничивается в физических пикселях, teleport delta сбрасывается, а compute накапливает только samples с согласованной velocity, чтобы не размазывать фон внутри маски робота.
- **Eigengrau**: единственная настройка силы — `intensity`. `noiseScale` означает размер зерна в физических пикселях, `darknessThreshold` — предел воспринимаемой яркости, `animationSpeed` — частоту обновления зерна. Не добавлять второй `strength` и не использовать крупный/скроллящийся noise-pattern.
- **Terrain material**: relief/connectivity metadata разрешено использовать для топологии и выбора тайлов, но не для поквадрантного затемнения текстуры. Не возвращать в `Terrain.shader` маску на основе `u-v`/`u+v` и кубический `finalRgb *= grad³`: она рисует видимые тёмные треугольники/ромбы внутри клетки. Затемнение террейна выполняется только через `_WorldLightTexture`.
- **World lighting**: `TerrariaLightingEngine` не распространяет свет CPU-sweep'ами по клеткам. Соседние emissive-клетки сворачиваются в постоянные world-anchored кластеры 2×2, а per-tile списки ограничивают compute только источниками, пересекающими culling tile. Размер кластера нельзя динамически менять по числу видимых emissive-клеток: переход порога перестраивает все источники и вызывает резкий скачок освещения при движении. При превышении лимита сохраняются ближайшие к центру cached region кластеры с детерминированным tie-break по world key. Никогда не возвращать глобальный цикл `каждый пиксель × все источники`: сотни светящихся клеток делают его непригодным даже для мощных GPU. Единственный источник истины для emission — серверный `CellConfigProperties.Glowing`; клиент не должен угадывать свечение по `CellType`, имени или legacy-списку. Для офлайн-режима `DummyConnection.CreateTestCellConfigurations()` обязан выставлять тот же флаг на нужных типах. Terrain mesh расширяется на `RequiredTerrainPadding = viewport + максимальный радиус света + safe border`; lighting engine не должен добавлять второй скрытый margin поверх уже расширенного mesh. Преобразование Unity Y в server Y всегда получает явный `MapManager.WorldHeight`. Ambient добавляется ровно один раз в compute-шейдере.
- **Terrain/lighting cache movement**: terrain window привязан к мировой сетке шагом 8 клеток и получает отдельный padding под это смещение. При переходе окна `TerrainCellCache.ScrollAndFill` сохраняет пересечение и загружает только новые полосы; не возвращать `PopulateFull` на каждую клетку движения камеры. Изменение карты инвалидирует terrain и static lighting только когда клетка пересекает текущий cached region; глобальный `MapStorage.Revision` нельзя использовать для пересборки viewport, потому что сетевой `MapRegionPacket` меняет множество далёких клеток. Новый регион, смена world data и загрузка видимой cell texture по-прежнему выполняют полную корректную инвалидацию.
- **Zoom cache hysteresis**: плавный `CameraFollow` не должен менять размеры terrain/lightmap ресурсов каждый кадр. `TerrainRenderer` выделяет ширину/высоту квантами по 32 клетки, растёт только при выходе viewport за текущую capacity, а уменьшает cache один раз через 0.4 секунды после стабилизации zoom. Не возвращать точное `ceil(camera.orthographicSize)` как немедленный размер mesh: это вызывает повторные allocation, full mesh build, coverage и SDF на каждом кадре zoom.
- **Lighting visibility и высота**: плоский grid-DDA удалён — он создавал бесконечные прямоугольные тоннели за блоками. Height-aware SDF cone tracing интегрирует оптическую толщину по трассе вместо выбора единственного самого тёмного sample через `min()`: полностью непрозрачный блок перекрывает direct light, а PNG alpha пропускает его пропорционально. Высота отвечает только за длину тени, cone radius — только за penumbra, coverage/density — только за пропускание. Не лечить форму тени одновременным ослаблением этих трёх независимых параметров. Emissive-кластеры привязаны к абсолютным world-cell координатам, иначе zoom вызывает blinking.
- **Receiver self-skip**: cone trace может пропустить coverage только внутри исходной клетки receiver, чтобы поверхность не затеняла сама себя. Нельзя продолжать skip по всей связной непрозрачной массе: тогда соседние вплотную блоки не накапливают optical depth и свет проходит сквозь стены. После выхода ray из `receiverCell` любой следующий opaque/alpha sample снова поглощает свет.
- **Lighting reconstruction**: базовая мягкость получается из SDF/cone tracing; separable edge-aware `FilterLighting` выполняет только финальную реконструкцию direct light. AO хранится в alpha lightmap во время двух filter-pass и не вычисляется повторно для каждого tap; ambient и AO нельзя размывать вместе с direct-shadow visibility. Фильтр сравнивает coverage центрального и соседнего texel и не переносит свет через границу окклюдера. Настройки `Shadow Filter Strength` и `Shadow Filter Occlusion Sharpness` сериализованы на `TerrariaLightingEngine`. Eigengrau не является частью lighting reconstruction и не изменяется при исправлении теней.
- **GPU coverage, Metal Y и SDF cache**: единственный источник формы окклюдера — pass `OcclusionCoverage` в `Terrain.shader`, отрисованный тем же mesh/material. Он повторяет реальные UV, autotile transforms, непрерывный PNG alpha и roundable `finalAlpha`; запрещено возвращать CPU-чтение сырого alpha-канала. `GL.GetGPUProjectionMatrix(..., renderIntoTexture: true)` меняет Y-ориентацию rasterized RenderTexture на Metal/D3D-подобных API, тогда как compute/world lightmap остаётся в world order. Поэтому все чтения coverage и SDF обязаны проходить через `ToOcclusionGrid` с `_OcclusionYFlip = SystemInfo.graphicsUVStartsAtTop`; иначе тени возникают в зеркальных пустых местах. Никогда не пытаться исправлять такой spatial mismatch коэффициентами — сначала проверить `Debug View = Occlusion`. Coverage хранится в R8 с 8 texel/cell; SDF использует только достаточно плотную alpha-часть как hard seed, а более прозрачные texel учитываются трассировкой непрерывно. JFA строится в `InitializeSdfSeeds → JumpFloodSdf → FinalizeSdf` и кешируется по региону, revision карты/атласа и lighting-настройкам.
- **Animated terrain и lighting**: тип анимации тайла не меняет его окклюзию/светопроницаемость и не запускает искусственную анимацию emission в lighting engine. Анимация текстуры выполняется только `Terrain.shader`; emission статичен, пока отдельные данные источника явно не зададут иное поведение.
- **Emission policy**: не добавлять клиентские allow/deny-листы для отдельных типов. Обычные `Rock`, `RedRock`, `NiggerRock`, `LivingBlackRock`, пески и валуны не светятся потому, что сервер/Dummy не выставляет им `Glowing`. Живки, кристаллы, building/artificial blocks, boxes, lava/magma и все acid/slime-варианты светятся только при наличии этого флага. Цвет можно брать из серверного `CellConfigurationPacket.Color`, но сам факт emission — исключительно из `Properties`.
- **Lighting tuning и диагностика**: высоты света/окклюдера, cone tracing, реконструкция и AO сериализованы на `TerrariaLightingEngine`. `Debug View = Ambient Occlusion` показывает AO, `Occlusion` — настоящий GPU coverage, `Direct Light` — источники без ambient/AO. Использовать эти режимы для проверки геометрии до composite.
- **Lighting quality**: профили `Low / Medium / High / Ultra` меняют разрешение lightmap, лимит источников, максимум SDF cone-tracing шагов и частоту обновления. Они не меняют художественные коэффициенты AO/света. `Ultra` использует 8 texel/cell, размер до 2048 и 64 шага; не возвращать скрытый cap 512, который при расширенном mesh давал около 4 texel/cell и видимую пикселизацию. Профиль хранится в `PlayerPrefs` под ключом `WorldLightingQuality`. Единственный источник профилей — `TerrariaLightingEngine.ApplyQualityPreset`.
- **Terrain normals**: normal map / Lambert для террейна пока не реализованы. Текущий этап отвечает за распространение света, лучевые тени и AO; normal atlas будет отдельным последующим слоем и не должен подменять raymarched visibility.

### C# и Код

- **DI**: VContainer через `GameLifetimeScope`. `RegisterManager<T>` для поиска/создания сцен-объектов. `[Inject]` для полей. `ServiceLocator.Resolve<T>()` для быстрого доступа.
- **Синглтоны**: `Instance` property + `Awake()` инициализация (не через `SingletonMonoBehaviour` — он удалён). См. `SingleMeshTerrainRenderer`, `PacketHandler`, `AudioSystem`.
- **События**: `Action` для связи между компонентами (`OnWorldInitialized`, `OnWorldDataLoaded`).
- **UniTask**: Для асинхронных операций (загрузка текстур, сетевые запросы).
- **UI создание**: Только через `GameManager.SetupUI()` (под deactivated UIRoot). **DummyConnection НЕ создаёт UI** — `FindAnyObjectByType` не видит inactive объекты, приводя к дублированию.

### Стандарты именования (Casing Standards)

1. **Unity Файлы и C# Код (`PascalCase`)**:
   - Классы, структуры, интерфейсы, перечисления: `WorldTextureManager`, `CellType`.
   - Публичные методы, свойства, события: `GetCellTextureCoordinate()`, `ActiveVoiceCount`.
   - Константы: `MaxLifetime`.
   - Приватные/защищенные поля: `_camelCase` (`private float _volume;`).
   - Параметры и локальные переменные: `camelCase` (`int x, int y`).

2. **Сетевые ресурсы, CDN и FMOD (`lowercase` / `snake_case`)**:
   - Имена FMOD событий: `event:/sfx_bz`, `event:/dig_rock`.
   - Сетевые тэги окон и контексты: `"teleport"`, `"open_missions"`, `"join_clan"`.
   - CDN URL-пути: `/cells/1.png`, `/clan/4.png` (Linux CDN серверы регистрозависимы, поэтому сетевые URL строчные).

### Документация (`docs/`)

- **Формат**: Только HTML. Никакого Markdown, никаких генераторов (Jekyll, Hugo, Docusaurus).
- **Стили**: Инлайн `<style>` в каждом файле. Минимальные, короткие, читаемые. Без внешних CSS-файлов, без фреймворков.
- **Шаблон**: См. `docs/rendering.html` как эталон. Тёмная тема, `system-ui`, `max-width: 720px`, `code` с моноширинным шрифтом.
- **Правило**: Каждый документ должен быть автономным — открыл файл в браузере, всё читается без зависимостей.

## 5. Критические нюансы (Gotchas)

1. **Инициализация MapStorage**: Рендеринг не начнется, пока `MapStorage.IsReady` не станет `true`. Это происходит после `WorldInitPacket`.
2. **DummyConnection._cellConfigs**: Должен быть инициализирован ДО отправки `WorldInitPacket`. При быстрой авторизации (валидный токен PRESENT) `CreateTestCellConfigurations()` должен быть вызван до отправки WorldInit, иначе `MapManager._cellConfigurations` будет null и клетки не ломаются.
3. **Dig-кулдаун блокирует движение**: `ApplyMovement()` проверяет `_input.WantsToDig || Time.time - _lastDigTime < DigCooldown`. Пока зажат пробел движение заблокировано.
4. **Инверсия Y**: Самый частый источник багов. Всегда проверяйте систему координат входящих данных.
5. **Текстуры**: Пайплайн кастомный — файловая система, не Resources. Билд должен копировать `Textures/` вручную.
6. **UI Toolkit**: Темы привязаны к GUID. Missing Reference в `PanelSettings` = пустой UI. Интермиттентный баг: UI рендерится но не принимает клики — лечится перезаписью `panelSettings` в MainMenu.
7. **Сортировка**: `SingleMeshTerrainRenderer` рисуется на `Sorting Order = -1000` (под спрайтами роботов).
8. **DI injection**: VContainer инжектит `[Inject]` поля **только** в объекты которые он создаёт или в которые явно вызван `resolver.Inject()`. `BuildCallback` сканирует все MonoBehaviour (включая неактивные) через reflection и вызывает `resolver.Inject()` для каждого. Если поле остаётся null — это FATAL ERROR в логах.
9. **UI дублирование**: `GameManager.SetupUI()` создаёт UI под выключенным UIRoot. `FindAnyObjectByType<T>()` **не видит inactive объектов**. DummyConnection не должен создавать UI — иначе появляются дубли-сироты.
10. **FPSCounter утечка**: `FPSCounter.Awake()` создаёт отдельный корневой `FPSCanvas` GameObject. `OnDestroy()` уничтожает его через `_ownedCanvas`. Если OnDestroy не вызван — Canvas остаётся навсегда.
11. **CompositionRoot удалён**: DI полностью через `GameLifetimeScope` (VContainer). `CompositionRoot` и `SingletonMonoBehaviour` удалены. ВАЖНО: `RegisterInstance` НЕ инжектит вручную созданные экземпляры — для DI-объектов с `[Inject]` используй `Register<T>(Lifetime.Singleton)`.
12. **ProgrammatorGrid — ширина контейнера**: `_gridContainer.width = COLS * (CELLSIZE + CELL_GAP*2 + 2f)` — `+2f` обязателен из-за `borderWidth: 1` на каждой ячейке (UI Toolkit content-box модель).
13. **ProgrammatorGrid — два UI-слоя**: `_popup` содержит три элемента: `dimmer`, `_panel` (сетка) и `_programListPanel` (список). Показывается только один из панельных слоёв за раз; `_createDialog` — абсолютный оверлей поверх обоих.
14. **ESC-навигация в программаторе**: Если открыта сетка, ESC возвращает в список программ (с сохранением данных). Если открыт список, ESC закрывает программатор. Если открыт диалог создания, ESC его не закрывает (только × или Отмена).
15. **Список программ сессионный**: `_programItems` живёт только в RAM. Созданные программы теряются при перезапуске. Единственный файл на диске — `programmator.json` (через Save).
16. **USS не поддерживает `calc()`**: UI Toolkit молча игнорирует правило с `calc(...)` (лог при загрузке — syntax error). Позиционирование через `calc(10px + 240px + 6px)` схлопывается в авто-layout — элементы «слетают» со своих мест. Относительные смещения (например рядом с задаваемым блоком, как `.hud-bonus-button` рядом с `.hud-top-right-panel, .hud-top-left-panel`) переносить в CSS через `calc()` из инлайн-кода НЕЛЬЗЯ — либо готовое число (`left: 256px`), либо инлайн-стиль из C# (там весь расчёт позиции задаётся с поправкой на SHIFT).

## 6. Рабочий процесс (Workflow)

- **Открытие**: Unity Hub → папка проекта. Основная сцена: `Assets/Scenes/SampleScene.unity`.
- **Сборка**: Использовать `BuildScript.BuildMacOS` из `Assets/Editor/`. Стандартный Build Settings не копирует текстуры.
- **Автономный режим**: `DummyConnection` создаст тестовый мир без сервера.
- **Сцена содержит**: `TerrainMesh`, `SingleMeshTerrainRenderer`, `UIDocument`, `Main Camera`, `Global Light 2D`, `SceneSetup`, `AutoMapManager`, `MapManager`, `GameLifetimeScope`. `GameLifetimeScope` инициализирует DI до `BeforeSceneLoad`.
- **Диагностика**: F12 — снимок в `diagnostic.txt`. F11 — рескан [Inject] в `inject_diagnostic.txt`. Python: `python3 scripts/inject_analysis.py` → `inject_analysis.txt`.

## 7. Линтинг C# (обязательно для ИИ)

Проект использует 5 Roslyn-анализаторов:

| Анализатор | Префикс | Зона ответственности |
| --- | --- | --- |
| `StyleCop.Analyzers` | `SA` | Стиль, форматирование, именование |
| `Microsoft.CodeAnalysis.NetAnalyzers` | `CA` | Корректность, надёжность, безопасность |
| `Roslynator.Analyzers` | `RCS` | Упрощение кода, dead code |
| `SonarAnalyzer.CSharp` | `S` | Качество кода, баги, уязвимости |
| `Microsoft.Unity.Analyzers` | `UNT` | Unity-специфика (Update, Invoke, Message) |

### Обязательный хук после генерации C# кода

```bash
dotnet build Assembly-CSharp.csproj -maxcpucount -p:UseSharedCompilation=true -nodeReuse:true -clp:NoSummary 2>&1
```

Вывод содержит предупреждения вида:

```
MapManager.cs(42,13): warning SA1300: ...
WorldLayer.cs(88,5): warning CA1031: ...
MapStorage.cs(15,1): warning S3903: ...
```

**Правило**: все предупреждения с префиксами `SA`, `CA`, `RCS`, `S`, `UNT` — нарушения линтера. Исправляй до финального ответа пользователю.

### Запрет обхода Git Hooks

- **СТРОГО ЗАПРЕЩЕНО** использовать `--no-verify`, пропускать хуки проверки или насильно отменять их при коммитах.
- Если пре-коммит хук или сборка зависает или завершается ошибкой, необходимо дождаться завершения, разобраться с причиной (песочница, линтеры, `dotnet build` ошибки) и исправить проблему, а не обходить хуки.

### Настройка

- `Directory.Build.props` — подключает анализаторы через NuGet во все `.csproj`
- `.stylecop.json` — отключает нерелевантные для Unity правила (XML-доки, file headers)
- `.editorconfig` — severity для каждого правила (`none` / `warning` / `error`)
- `CS0649` — `[Inject]` поля инициализируются `= null!;` чтобы компилятор знал что VContainer заполнит их в рантайме.

## 8. Правила работы (от юзера)

- Никогда не делать ленивых решений, фоллбеков и т.п. — скупой платит дважды.
- Запускать проверки (билды, линтеры) реже — это может серьёзно нагружать систему.
- **НЕ добавлять фичи без явного запроса**. Если пользователь спрашивает про отсутствие чего-либо — уточнить что именно нужно, а не добавлять самовольно.

ЗАПРЕЩЕНО МЕНЯТЬ ВРУЧНУЮ ПРЕФАБЫ, АССЕТЫ И СЦЕНЫ - ЭТО ГАРАНТИРОВАННАЯ СМЕРТЬ ПРОЕКТА.
