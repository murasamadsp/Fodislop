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

1. **File-Scoped Namespaces**: Все файлы используют `namespace Fodinae.Domain;` вместо вложенных фигурных скобок.
2. **Nullable Reference Types**: Глобально включен `#nullable enable`. Все поля, свойства и параметры явно размечаются (`string?`, `null!`).
3. **Primary Constructors & Record Structs**: Легковесные структуры и хендлы используют `readonly record struct` и первичные конструкторы.
4. **Collection Expressions**: Использование `[]` вместо `new List<T>()` или `new T[]`.
5. **Global Usings**: В `Fodinae.Core.GlobalUsings` объявлены глобальные usings для всех основных пространств имён: `Fodinae.Core`, `Fodinae.AssetPipeline`, `Fodinae.Audio.*`, `Fodinae.Networking.*`, `Fodinae.World.*`, `Fodinae.Game.*`, `Fodinae.Player.*`, `Fodinae.UI.*`, `Fodinae.Effekseer`.

### 1.2 Архитектурная концепция клиенто-серверного взаимодействия

Архитектура Fodinae построена на четком разделении тяжелого рендеринга и легкого сетевого состояния:

1. **Примитивы рендеринга на клиенте**: Клиент содержит готовые системы отрисовки и воспроизведения (тайловый мир `SingleMeshTerrainRenderer`, сущности роботов `RobotManager`, предметы на земле `PackManager`, эффекты `ServerAudioEventManager`).
2. **Легковесный сетевой поток данных**: Сервер передает клиенту только чистые координаты и идентификаторы состояний (где стоят роботы, какие предметные паки лежат, какие ячейки попадают в радиус прорисовки, какие звуки вызваны).
3. **Ленивая однократная загрузка тяжелых ассетов (On-Demand Fetching)**: При первом появлении ранее неизвестного объекта (новый блок, скин робота, иконка пака, аудио-банк) клиент запрашивает бинарные ассеты (текстуры, спрайты, `.bank`) с CDN/сервера один раз.
4. **Кэширование и локальный рендер**: Все полученные ассеты сохраняются в стойком дисковом кэше `PersistentAssetCache` (с ETag/MD5 валидацией) и ОЗУ (`CellTextureCache`, `AssetCache`). В дальнейшем клиент выполняет тяжелый рендеринг исключительно из локального кэша без повторных сетевых запросов.


## 2. Структура проекта

```text
Assets/
  Editor/              # BuildScript.cs (сборка билдов), CsProjFix.cs (csproj постпроцессор), FmodBankBuilder.cs (синк FMOD-банков), MapbConverter.cs (конвертер серверных карт)
  Plugins/             # Vendored DLL
    UniTask/           # Vendored UniTask (полный пакет)
    SharpCompress, ZstdSharp, K4os.Compression.LZ4  # Сжатие
    NetCoreServer      # Сеть
    Genumerics, ExtendedNumerics.BigDecimal          # Математика
    SmartFormat, NCalc, Parlot, ZString              # UI/шаблоны
    System.*, IsExternalInit                         # Системные заглушки
  Scenes/              # SampleScene.unity, TextureStorageTestScene.unity
  Scripts/
    # Ассет-пайплайн
    AssetPipeline/
      ClientAssetLoader.cs        # Загрузка ассетов с сервера/локально
      PersistentAssetCache.cs     # Стойкий кэш ассетов (ETag, MD5)
      ETagCalculator.cs           # MD5-хэш для ETag-валидации
      DynamicImage.cs             # Компонент Image, загружающий спрайты с сервера
      AssetCache.cs               # RAM-кэш декодированных ассетов
      AnimatedSpriteData.cs       # Данные анимированного спрайта

    # Аудио
    Audio/
      Backend/
        AudioSystem.cs            # Синглтон-контроллер (Play, PlayAttached, PlaySnapshot, SetGlobalParameter, SetBusVolume)
        FmodAudioBackend.cs       # Низкоуровневый FMOD API: loadBankFile, AttachInstanceToGameObject, шины, снэпшоты
      Core/
        AudioLayer.cs             # Параметры звука: шина (SFXDefault/UIDefault/etc), volume, pitch, IsSpatial
        AudioPlaybackHandle.cs    # Обёртка над FMOD.Studio.EventInstance (Stop, SetPosition, SetVolume, SetParameter)
      Spatial/
        AudioSpatial.cs           # Компонент: нативная привязка 3D-звука к трансформу
        AudioZone.cs              # Триггерная зона: запускает FMOD Snapshots и выставляет Global Parameters
        WorldAudioController.cs   # Управление фоновым аудио мира

    # Системная инфраструктура
    Core/
      Interfaces/             # IWorldDataStorage, IMapDataProvider, IPlayerInput, IAssetLoader, IAudioSystem, IPlayerStats, IInventoryModel, IConnectionService, INetworkService, IInputBlocker, IRobotService, IPackService, IVFXService, IServerAudioService, IServerConfig
      DI/
        IServiceLocator.cs    # Интерфейс IServiceLocator (не используется — мёртвый код, не удалять)
      ServiceLocator.cs       # Тонкий bridge → VContainer (только Initialize + Resolve)
      GameLifetimeScope.cs    # LifetimeScope для сцены: регистрация DI + BuildCallback с инжекцией через reflection
      GameConstants.cs        # Игровые константы
      ObjectPool.cs           # Пул объектов
      SharedMaterialCache.cs  # Кэш общих материалов (Sprites/Default по текстуре — используется и TentacleBatchRenderer)
      GlobalUsings.cs         # Глобальные using'и
    VContainer/
      Runtime/                # Vendored VContainer 1.19 (81 файлов)

    # Эффекты (Effekseer)
    Effekseer/
      RuntimeEffekseerLoader.cs   # Загрузчик эффектов Effekseer в рантайме

    # Диагностика (runtime)
    DiagnosticRunner.cs          # F12 — снимок состояния в diagnostic.txt (каждую секунду heartbeat)
    InjectDiagnostic.cs          # Авто-скан [Inject] полей на старте + F11 для ручного рескана → inject_diagnostic.txt

    # Игровые сущности и менеджеры
    Game/
      Pack.cs                     # Игровой предмет (пак на земле)
      Robot.cs                    # Робот (NPC/игрок в мире)
      RobotHeadlight.cs           # Фары/освещение робота
      Tentacle.cs                 # Симуляция щупал хвоста (пружинная цепь)
      TentacleBatchRenderer.cs    # Батч-рендер всех щупал: 1 меш на текстуру хвоста (1 draw call вместо 4/робота)
      ServerAudioEvent.cs         # Серверный аудио-эффект (SFXPacket → FMOD + VFX)
      VFXPool.cs                  # Пул визуальных эффектов
      Managers/
        GameManager.cs            # Точка входа: инициализация UI-сцены и подсистем
        MapManager.cs             # Жизненный цикл мира (WorldInit, MapRegion), конфиги ячеек
        MapStorage.cs             # Хранилище карты (чанки 32×32), кэш в .mapb
        RobotManager.cs           # Управление роботами (спавн, движение, деспавн)
        PackManager.cs            # Управление предметами на земле
        ServerAudioEventManager.cs # Принимает SFXPacket → запускает FMOD + VFX
        ItemRegistry.cs           # Реестр предметов: имена, иконки
        ServerConfig.cs           # Конфигурация с сервера (digCooldown и т.д.)

    # GIF-декодер
    MgGifDecoder/
      MgGifDecoder.cs             # GIF-декодер (MG.GIF)

    # Сеть
    Networking/
      NetworkService.cs           # Синглтон: подписка/отписка пакетов Subscribe<T>
      PacketHandler.cs            # Диспетчер пакетов → менеджеры (static IsInputBlocked, TopWindowTag)
      Processors/                 # WorldInitProcessor, MapRegionProcessor и др.
      Connection/
        ConnectionManager.cs      # Синглтон: управление подключением (DummyConnection или TCP, авторизация, реконнект)
        Client/
          DummyConnection.cs      # Заглушка для офлайн-режима (пребейкеная карта или генерация)
          TextureStorageManager.cs # Менеджер хранения текстур на сервере

    # Игрок
    Player/
      Interfaces/
        IPlayerInput.cs           # MoveInput, WantsToDig, WantsToToggleAutoDig, WantsToToggleAggression, IsShiftPressed, SetMovementInput(Vector2)
      Input/
        PlayerInputHandler.cs     # Реализация IPlayerInput (New Input System + Keyboard прямая)
      Logic/
        PlayerMovementController.cs   # Ввод, движение, копка, автокопка
        PlayerInteractionController.cs # Обработка кликов и клавиш (копка, использование)
      CameraFollow.cs               # Следование камеры за игроком

    # UI
    UI/
      Builders/
        PacketUIBuilderFactory.cs # Фабрика UI-билдеров пакетов
        PacketUIBuilderBase.cs    # Базовый класс билдера
        PacketUIBuilder.cs        # Базовый интерфейс билдера
        CanvasPacketBuilder.cs, PanelPacketBuilder.cs, GridPacketBuilder.cs,
        TextPacketBuilder.cs, TextBoxPacketBuilder.cs, ImagePacketBuilder.cs,
        SelectablePacketBuilder.cs, SliderPacketBuilder.cs,
        IntDropdownPacketBuilder.cs, StringDropdownPacketBuilder.cs,
        ScrollViewerPacketBuilder.cs, LinePacketBuilder.cs, DockPanelPacketBuilder.cs
      Controls/
        Selectable.cs             # Кастомный Selectable (UI Toolkit)
        RegexTextField.cs         # Текстовое поле с валидацией по regex
        UILine.cs                 # Кастомный VisualElement для линий
        ChatInputBlinker.cs       # Анимация курсора в поле чата
      Binding/
        WindowBinding.cs          # SmartFormat-привязка данных для окон GUI
        LogiCalcFormatter.cs      # Форматтер вычислений для SmartFormat
      Programmator/
        ProgrammatorData.cs          # Данные программатора
        ProgrammatorGrid.cs          # Сетка программатора + список программ + save/load (static IsOpen)
        ProgrammatorTextureRegistry.cs # Реестр текстур программатора
        RadialMenu.cs                # Радиальное меню программатора
        ObserverJoystick.cs          # Джойстик для Observer-операторов
      ChatInput.cs                # Управление фокусом чата (блокировка управления)
      ClickContextResolver.cs     # Разрешение clickContext-путей в VisualElement
      FloatingChatBubble.cs       # Всплывающее сообщение над персонажем
      FloatingChatManager.cs      # Менеджер всплывающих чат-сообщений
      FPSCounter.cs               # Счётчик FPS (с OnDestroy — уничтожает FPSCanvas)
      GlobalChatUI.cs             # Глобальный чат (ввод, история)
      ItemData.cs                 # Данные предмета (тип, количество)
      UIInputManager.cs           # Менеджер модальных UI-окон
      HUD/
        Inventory/
          Interfaces/
            IInventoryModel.cs    # Интерфейс инвентаря (в UI/HUD/Inventory/Interfaces/)
          Model/
            InventoryModel.cs     # Модель данных инвентаря
          View/
            InventoryView.cs      # Окно инвентаря (сетка 9×6 + хотбар)
          Presenter/
            InventoryPresenter.cs # Презентер инвентаря
        Player/
          Model/
            PlayerStatsModel.cs   # Модель статистики игрока
          View/
            PlayerHUDView.cs      # HUD игрока
          Presenter/
            PlayerHUDPresenter.cs # Презентер HUD
      LocalChatPopup.cs           # Popup локального чата
      MainMenu.cs                 # Главное меню
      MinimapController.cs        # Контроллер миникарты
      ModalWindowHandler.cs       # Обработчик модальных окон
      PauseMenu.cs                # Меню паузы (static IsMenuOpen, ESC через ProgrammatorGrid)
      StyleApplicator.cs          # Применение стилей к UI-элементам
      WorldMapController.cs       # Полноэкранная карта мира (управление)
      WorldMapRenderer.cs         # Рендеринг карты мира (текстура из MapStorage)

    # Мир и рендеринг
    World/
      SingleMeshTerrainRenderer.cs  # Один меш на весь террейн, 7 UV-каналов
      CoordinateUtils.cs            # Прямая конвертация координат 1:1 (сервер↔Unity)
      FodinaeGizmos.cs              # Визуальные Gizmos отладки мира
      WorldTextureManager.cs        # Загрузка тайлов в TextureAtlas
      TextureAtlas.cs               # Упаковка текстур в атлас
      SurfaceRenderer.cs            # Transit + Perspective поверхности (доп. меши)
      CellTextureCache.cs           # ConcurrentDictionary-кэш текстур ячеек
      AtlasCoordinate.cs            # Координаты ячейки в текстурном атласе
      AnimationContainerDecoder.cs  # Декодинг PNG/GIF/WebP в спрайты
      WorldBackgroundSetup.cs       # Настройка фона сцены
      WorldLayer.cs                 # Дисковый стриминг чанков (RLE + LRU кэш)
      TileMaskConverter.cs          # Битмаски авто-тайлинга
      SceneSetup.cs                 # Инициализация сцены при старте
      StandaloneWorldInitializer.cs # Тестовый мир без сервера
      RenderingConstants.cs         # Константы рендеринга
      Extensions/
        WorldLayerTextureExtensions.cs # Расширения WorldLayer для текстур

    # Редактор
    Editor/
      InjectValidator.cs            # MenuItem для валидации [Inject] полей в Edit Mode

  Settings/            # URP и Renderer2D конфиги
  Textures/            # Cells/, Clan/, Crystals/, Exported/, Items/,
                       #   Pack/, Skin/, Tail/, UI/, VFX/ — тайлы, UI, экипировка
  UI Toolkit/          # PanelSettings.asset, темы (.tss)

scripts/
  inject_analysis.py               # Python-скрипт для статического анализа покрытия [Inject] полей
  pre-commit-lint.sh               # Прекоммит-хук: Roslyn-анализаторы
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
   - Инжектит `SingleMeshTerrainRenderer` и вызывает `EnsureSubscriptions()`.
   - **Сканирует ВСЕ активные MonoBehaviour** в сцене через reflection, находит те что имеют `[VContainer.InjectAttribute]` на полях, и вызывает `resolver.Inject(mb)`.
   - Вызывает `ValidateStartup()`.
3. **`ValidateStartup()`** — проверяет что ни одно критическое `[Inject]` поле не осталось null (PacketHandler, PauseMenu, PlayerHUDView, InventoryView, PlayerMovementController, MapManager, WorldTextureManager, ClientAssetLoader, AudioSystem, SingleMeshTerrainRenderer).

#### Таблица регистрации сервисов

| Регистрация | Интерфейсы | Тип |
|---|---|---|
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
|---|---|---|
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

- **Прямое редактирование**: Предпочтительно редактирование `.prefab` и `.unity` как текстовых YAML-файлов.
- **Мета-файлы**: У каждого ассета ДОЛЖЕН быть `.meta` файл. При перемещении/удалении через CLI — обрабатывать оба.
- **GUID**: Не ломайте связи между ассетами, сохраняйте GUID.

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
|---|---|---|
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
