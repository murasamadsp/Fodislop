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

CompositionRoot и `SingletonMonoBehaviour` удалены. Регистрация двухуровневая: `BootstrapLifetimeScope` (DontDestroyOnLoad, `DefaultExecutionOrder -30000`) держит менеджеры, переживающие переходы сцен (`ConnectionManager`, `NetworkService`, `AudioSystem`, `ClientConfigManager`, `ClientAssetLoader`), а `GameLifetimeScope` (в MainGame, `-20000`) — игровые. `RegisterManager<T>` ищет объект строго в своей сцене (`gameObject.scene == _ownScene`), регистрирует найденный экземпляр через `RegisterComponent(existing)` или — если менеджера нет в сцене — через `RegisterComponentOnNewGameObject<T>(Lifetime.Singleton).UnderTransform(transform)`.

**Запрещено создавать менеджеров вручную через `AddComponent` внутри `Configure`.** `Configure` выполняется до сборки контейнера: `AddComponent` мгновенно дёргает `Awake`/`OnEnable` в момент, когда ServiceLocator ещё указывает на Bootstrap-скоуп, сцена не активна, а `[Inject]`-поля не заполнены — это даёт весь класс гонок (резолв `UIDocument` из Bootstrap, захват меню-камеры в `Awake`, NRE в `Start`). `RegisterComponentOnNewGameObject` делегирует создание `NewGameObjectProvider`: неактивный GO → `AddComponent` (Awake не вызывается) → инъекция → активация. Создание происходит при первом резолве — в `GameBootstrap.PostStart`.

Инициализация графа выполняется НЕ в build-callback, а в `GameBootstrap` (`IPostStartable.PostStart`):

1. `ServiceLocator.Initialize(resolver)`;
2. `SceneManager.SetActiveScene(_ownScene)` — иначе лениво создаваемые менеджеры попадут в сцену, загрузившую нас (например, меню);
3. инъекция scene-MonoBehaviours с `[Inject]` (`InjectSceneBehaviours`);
4. **явный резолв ВСЕХ менеджеров** в детерминированном порядке: ConnectionManager → NetworkService → MapManager → PacketHandler → IAssetLoader → AudioSystem → IPlayerStats → PlayerMovementController → CameraFollow → TerrainRenderer → TentacleBatchRenderer → UI-сервисы (GlobalChatUI, FloatingChatManager, FPSCounter, DiagnosticRunner, IInputBlocker, MinimapController, DisplayManager, UIInputManager, PlayerHUDView, InventoryView, PauseMenu) → PostProcessController.EnsureVolumeSetup → GameManager → ServerConfig → TerrariaLightingEngine.EnsureInitialized → SurfaceRenderer → TextureStorageManager → WorldTextureManager → ServerAudioEventManager → VFXPool → PackManager → RobotManager;
5. `gameManager.EnsureUISetup()` — ПОСЛЕ резолвов всех UI-сервисов, иначе `SetupUI` не найдёт их через `FindAnyObjectByType` и создаст дубликаты без регистрации в контейнере;
6. `TerrainRenderer.EnsureSubscriptions()`;
7. `ValidateStartup()` (критические поля: `PacketHandler`, `PauseMenu`, `PlayerHUDView`, `InventoryView`, `PlayerMovementController`, `MapManager`, `WorldTextureManager`, `ClientAssetLoader`, `AudioSystem`, `TerrainRenderer`).

Порядок резолвов в `PostStart` — **контракт, а не деталь реализации**: ленивые синглтоны создаются в порядке первого резолва, поэтому добавление нового `RegisterManager<T>` без резолва в `PostStart` означает, что менеджер вообще не создастся (или создастся недетерминированно при первом обращении). Любой новый менеджер обязан быть зарезолвлен в `PostStart` в правильной позиции.

Регистрируются `MapStorage`, `InventoryModel`, `PlayerStatsModel` как instances; managers: `MapManager`, `TerrainRenderer`, `ClientAssetLoader`, `AudioSystem`, `WorldTextureManager`, `ServerAudioEventManager`, `ConnectionManager`, `PacketHandler`, `NetworkService`, `GameManager`, `VFXPool`, `PackManager`, `RobotManager`, `TentacleBatchRenderer`, `ServerConfig`, `TextureStorageManager`, `GlobalChatUI`, `UIInputManager`, `FPSCounter`, `FloatingChatManager`, `PlayerHUDView`, `InventoryView`, `PauseMenu`, `MinimapController`, `DisplayManager`, `CameraFollow`, `PostProcessController`, `TerrariaLightingEngine`, `SurfaceRenderer` — с соответствующими интерфейсами из `Core/Interfaces`.

`ServiceLocator` содержит только `Initialize(IObjectResolver)` и `Resolve<T>()`. Для старого монолитного кода допустимы `Instance`; новый код использует `[Inject]`.

### Сеть и UI

- `NetworkService`/`PacketHandler`/`ConnectionManager` — сервисы подписок, диспетчеризации, авторизации и реконнекта. `DummyConnection` — offline transport; при невалидном токене отправляет `OpenWindowPacket('auth')` — это штатный флоу первого входа (токены хранятся в `temporaryCachePath/server_tokens.json`, клиентский — в PlayerPrefs `AuthToken6`), и его окно обязано быть видимым поверх лоадера меню (см. слои UIDocument).
- Процессоры обрабатывают world, map, chat, clan, audio, windows, inventory, stats, player, robots, packs, missions и config. Packet UI строится из `OpenWindowPacket` через `PacketUIBuilderFactory`.
- Единственный источник обычного UI — `GameManager.SetupUI()` под выключенным `_uiRoot`; `AuthorizeUI()` его активирует. `FindAnyObjectByType` не видит inactive объекты.
- Сервер авторитетен при закрытии packet-окон: ESC/кнопка отправляют запрос, локально окно не скрывать; закрытие происходит только после `CloseWindowPacket`.
- `PacketHandler.IsInputBlocked` вычисляется как `ChatInput.IsFocused || HasOpenWindows || IsModalShowing || PauseMenu.IsMenuOpen || ProgrammatorGrid.IsOpen`. Фокус чата входит в блокировку: пока пользователь печатает, движение, копка, геймплейные клавиши и камера заблокированы (это единственный источник блокировки ввода для `PlayerMovementController`/`PlayerInteractionController`/`CameraFollow`). `PauseMenu` сначала отдаёт ESC программатору, затем отправляет серверный close верхнего окна. Enter в чате отправляет сообщение даже при `IsInputBlocked` — условие в `GlobalChatUI.Update` учитывает `ChatInput.IsFocused`.
- `WindowBinding` использует SmartFormat. Inventory — модель/presenter/view, 9×6 + хотбар; HUD включает HP, энергию, баффы, авто-копку и Programmator. Chat состоит из global/local/floating компонентов.
- `MainMenu` загружает `Resources/UI/MainMenu.uxml`; после сборки UI фиксированный `PanelSettings` нужно восстановить, иначе элементы могут отображаться, но не принимать события.
- **Слои UIDocument — часть контракта.** `MainMenu` UIDocument: `sortingOrder 100` (полноэкранный лоадер «спуска»); игровой `MainGame` UIDocument: `sortingOrder 0` (ПОД лоадером — во время загрузки игровой UI не должен быть виден, иначе на экране загрузки мелькают появляющиеся по одному элементы HUD/миникарты). Серверные окна (`OpenWindowPacket` — auth, кланы, миссии) открываются в игровом UIDocument, поэтому при открытии окна `MainMenu.DismissDescentIfServerWindowOpened()` скрывает свой полный слой (лоадер уступает окну, сцена меню при этом НЕ выгружается — финальный teardown остаётся за `OnWorldLoaded`). Не поднимать игровой sortingOrder выше меню и не скрывать игровой UI иначе — это ломает видимость окна.

#### Идиоматика UI Toolkit (обязательна для нового/переписываемого кода)

1. **Один источник стилей** — `PanelSettings.themeUss` → `FodinaeTheme.tss`, который `@import`'ит все `Resources/Styles/*.uss`. Запрещено в контроллерах делать `element.styleSheets.Add(Resources.Load<StyleSheet>(...))` и дублировать уже импортированные стили.
2. **Структура в UXML, не в коде.** Статическая разметка (контейнеры, кнопки, панели) — в `.uxml` в `Resources/UI/`; код через `VisualTreeAsset.CloneTree()` и `tree.Q<T>("Name")` только привязывает обработчики и data-bound контент. Конструкция `new VisualElement()` в C# — только для динамических списков/сеток.
3. **Размер панели — дело PanelSettings, не кода.** Запрещено выставлять `root.style.width/height` из `Screen.width/height` и полагаться на абсолютные `top/left` координаты. Рут и контейнеры растягиваются через `position:absolute; left/right/top/bottom:0` или `flex-grow:1` в USS; центрирование — flexbox (`align-items/justify-content`).
4. **Переключение видимости — `display:Flex/None`** через класс или inline-стиль на существующем элементе UXML. Не добавлять/удалять оверлеи из иерархии на каждом кадре и не играться `pickingMode` для «прозрачности» — модальные/фоновые слои задаются z-порядком в UXML и USS.
5. **Сборка UI один раз** (guard по флагу в `OnEnable`/`Start`), а не при каждом показе. `clicked`/`RegisterCallback` подписывать один раз; при `OnDisable` — отписываться.
6. **`Screen.width` ненадёжен в редакторе** — до первого layout панель может дать NaN; причина некликабельности. Диагностировать по `root.layout` (конечный размер ≠ NaN) и `element.worldBound`. После `CloneTree` не модифицировать `root` кроме `Add(tree)`.
7. **Клавиатурная навигация по UI вырезана насовсем.** EventSystem/`InputSystemUIInputModule` не создаются ни в одном скоупе (проект 100% UI Toolkit, uGUI нет). `PlayerHUDView.InitializeHUD` безусловно подавляет навигационные события панели (`NavigationMoveEvent`, `NavigationSubmitEvent`, Tab через `KeyDownEvent`) в TrickleDown — стрелки/WASD/Enter/Tab не двигают фокус по кнопкам и не активируют их. Всё UI-взаимодействие — мышью; геймплейные клавиши читаются напрямую через `Keyboard.current` в `PlayerInputHandler`.
8. **Перевод координат экран→панель только через `RuntimePanelUtils.ScreenToPanel(root.panel, screenPos)`.** `PanelSettings` — `ScaleWithScreenSize` (reference 1200×800); ручной флип Y (`new Vector2(x, Screen.height - y)`) не учитывает масштаб и мажет тем сильнее, чем дальше точка от верхнего левого угла. При 1920×1080 у миникарты (низ слева) `Pick` промахивался на сотни пикселей — клики по кнопкам проваливались в мир, и `PlayerInteractionController` слал `ClickCellPacket`, из-за чего робот «сам» шёл копать.

### Мир и координаты

- Серверные координаты: левый верхний угол `(0, 0)`, X вправо, Y вниз. Все преобразования — через `CoordinateUtils`; всегда учитывать `MapManager.WorldHeight`.
- `MapManager` получает `WorldInitPacket`/`MapRegionPacket`; `MapStorage` хранит чанки 32×32, `persistentDataPath/*.mapb`, и уведомляет renderer через `OnCellChanged()`.
- `WorldLayer<T>` — дисковый streaming, LRU RAM-кэш, RLE и append-only запись с компактификацией. Текстуры загружаются из файловой системы, не из Resources/Addressables.
- `TerrainRenderer` рисует видимый мир одним mesh (7 UV-каналов, sorting order `-1000`) и обновляет изменения дифференциально. `SurfaceRenderer` обслуживает закартовые поверхности и обязан регистрироваться/разрешаться через `GameLifetimeScope` до startup validation. `SceneSetup` только загружает его обязательные текстуры; вручную создавать второй `SurfaceRenderer` запрещено.
- `TerrainCellCache` привязан к мировой сетке шагом 8: при движении сохранять пересечение и заполнять только новые полосы. Zoom-кэш квантовать по 32 клеткам, уменьшать через 0.4 с после стабилизации; не аллоцировать ресурсы каждый кадр.
- `AnimationContainerDecoder` поддерживает PNG/GIF/WebP; анимация тайла не меняет его окклюзию или emission.
- `CellConfigurationPacket.Animation` задаёт shader-анимацию и **не означает**, что PNG является frame-atlas. Только `FrameOffset > 0` задаёт высоту вертикального кадра в клетках; `FrameOffset == 0` легитимен для UV/color-анимаций. В частности, Lava использует animation type `4`, серверную скорость и намеренный `FrameOffset=0`: она скроллит UV единого tiled sheet. Не выводить frame count из `Animation != None` и не обнулять `AnimationSpeed` при отсутствии кадров.
- Геометрия верхней закартовой поверхности фиксирована авторским контрактом: от верхней границы мира идут `Transit` высотой `2` world cells и шириной горизонтального тайла `32`, затем `Perspective` высотой `2` и шириной тайла `5`; выше остаётся фон/небо. Обе текстуры повторяются только по X и clamp'ятся по Y. Красноскал бесконечен только слева, справа и снизу карты, но не сверху. Эти размеры нельзя заменять размерами PNG или границей камеры.
- Production runtime-текстуры создаются/декодируются только через `RuntimeTextureFactory`: канонический `RGBA32`, без mipmaps, с явно заданными color space, filter и wrap. Прямые `new Texture2D(...)` и `LoadImage(...)` вне фабрики запрещены. Terrain atlas copy предварительно проверяет точные размеры и совместимый graphics format; диагностическая случайная texture отсутствующего terrain-ассета — сознательная обязательная функция, не удалять.

### Игрок

- Единственный источник позиции — `PlayerMovementController.Position` (`Vector2Int`, server Top-Left). `ClientPosition` и `ServerPosition` не возвращать.
- `IPlayerInput → PlayerInputHandler`: WASD/стрелки — движение, Space — копка, E — авто-копка, L — агрессия, Shift — бег. Валидация — `Passable` на клиенте и `MovePacket` на сервере. `PlayerInputHandler` поллит `Keyboard.current` глобально и НЕ знает про UI: блокировка ввода при печати в чате/окнах обеспечивается через `PacketHandler.IsInputBlocked` (включает `ChatInput.IsFocused`), а не внутри обработчика.
- Клик мышью по миру (`ClickCellPacket` — копка/взаимодействие) шлётся из `PlayerInteractionController.HandleMouseClick` только если `IsPointerOverUI` вернул false. `IsPointerOverUI` обязан использовать `RuntimePanelUtils.ScreenToPanel` (см. идиоматику UI Toolkit п. 8) — иначе клики по UI в нижней части экрана проваливаются в мир и двигают робота.
- `DigCooldown = 0.3f` блокирует и повторную копку, и движение. Направление — `_lastSentDirection`, по умолчанию `Direction.Down`. Dummy отправляет SFX пустой клетки до проверки `Empty`.

### FMOD

`AudioSystem`/`FmodAudioBackend` используют FMOD Studio C++ Engine. Банки скачиваются через `ClientAssetLoader`, кэшируются на диске и загружаются `loadBankFile` по пути; feature-банки загружаются/выгружаются on-demand. 3D-звук привязывать нативно через `AttachInstanceToGameObject`, зоны используют Snapshots и global parameters. Шины: Master, SFX, Music, Voice, Ambience, UI. FMOD-проект: `FodinaeAudio/FodinaeAudio.fspro`.

Примеры: `Play2D`, `PlayAttached`, `PlaySnapshot`, `SetGlobalParameter`, `SetBusVolume`. `ServerAudioEventManager` принимает `SFXPacket`, проигрывает 3D-звук и создаёт визуальное событие.

### Программатор

`ProgrammatorGrid` — визуальный редактор робота: список программ → сетка → Save/Run/Stop. Данные программ сессионные (`_programItems` в RAM); единственный файл — `programmator.json`, сохраняемый через `JsonUtility`. Run/Stop пока визуальные. Сетка 16×12, `CELLSIZE=32`, `CELL_GAP=2`, контейнер 608 px, панель 648 px. `_popup` содержит `dimmer`, `_programListPanel`, `_panel`; `_createDialog` — абсолютный overlay. ESC: grid → список с сохранением, список → закрытие, dialog закрывается только ×/Отмена.

### Lighting и terrain invariants

- Активный lighting-пайплайн — GPU Radiance Cascades из `WorldLighting.compute`: `LightingMaterialField`/`EmissionField → SolveCascade → ResolveDirect → SolveDiffuseBounce → CompositeLighting`. Не возвращать старые SDF/raymarch/AO-neighbour/blur проходы, CPU sweep, GPU readback или runtime fallback.
- Единственный источник emission — серверный `CellConfigProperties.Glowing` (в Dummy выставлять тот же флаг), не `CellType` и не клиентские allow/deny-листы. Цвет можно брать из `CellConfigurationPacket.Color`.
- `MaterialField.rgb` — surface albedo для одного diffuse bounce, `MaterialField.a` — физическая occupancy; `EmissionField` содержит излучение. Альфа атласа, visual blending, анимации и песок поверх валуна не меняют физическую массу. Соседние `DropsShadow`-клетки образуют единый контур без внутренних границ.
- Beer–Lambert extinction — **ослабление света**, итоговая surviving fraction — **пропускание света**. Direct radiance, transmission и AO — разные величины; не называть поглощение или visibility «AO». Receiver self-skip разрешён только внутри исходной клетки, после выхода соседняя масса снова ослабляет свет.
- Новый обязательный трек описан в корневом `LIGHTING_AO_PLAN.md`: удалить legacy `nearSolidPath` pseudo-AO и добавить отдельный полноразрешённый contact/cavity AO из occupancy. AO даёт слабую тень у открытой границы, более сильную в 90° углах/щелях, не создаёт швов внутри массива и влияет только на ambient и diffuse bounce, но не на direct radiance/emission.
- AO хранится отдельно в persistent `RHalf`, публикуется в alpha итоговой `_WorldLightTexture` и пересчитывается только при geometry revision, смене региона/размера поля или AO-настроек. Изменение источников света AO не пересчитывает. Ambient добавляется ровно один раз; Eigengrau не относится к lighting reconstruction.
- Статическое поле геометрии растеризуется фактическим terrain mesh одним command buffer; динамические источники добавляются GPU draw. Незагруженные и выходящие за границы мира клетки не должны попадать в submesh indices или подставлять cell type `0`.
- Сохранять внешний контракт `TerrariaLightingEngine`: `_WorldLightTexture`, `_WorldLightRect`, `InvalidateCell`. Профили `Low/Medium/High/Ultra` меняют только качество/стоимость существующего алгоритма; отдельные визуальные пресеты и скрытые fallback-коэффициенты запрещены. Normal map/Lambert пока не реализованы.

## 4. Unity, YAML и код

- `.prefab`, `.unity`, `.asset` нельзя редактировать текстом. Менять их только через Unity Editor API/Inspector; сохранять GUID и `.meta`.
- Имя файла Unity-скрипта должно совпадать с классом. После создания/переименования проверять `MonoScript.GetClass()` в Editor: `dotnet build` этого не проверяет.
- `VolumeProfile.Add<T>()` создаёт component только в памяти; editor-код обязан вызвать `AssetDatabase.AddObjectToAsset()` до `SaveAssets()`.
- `Renderer2D.asset` содержит ровно одну активную `PostProcessRendererFeature`. Post-process применяется к базовой камере; world-space UI на слое `UI` рисуется отдельной Overlay `WorldUICamera` без post-process; UI Toolkit/Screen Space Overlay идёт позже.
- Внутренний URP HDR (`supportsHDR`) включён для lighting/bloom, HDR display отключён (`PlayerSettings.allowHDRDisplaySupport = false`). Не отключать URP HDR ради SDR.
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
5a. Никогда не резолвить зависимости через `ServiceLocator` из `Awake`/`OnEnable`/`Start` до того, как объект получил `[Inject]`-поля (в ленивых синглтонах это гарантирует `NewGameObjectProvider`: инъекция до активации). Если менеджер требует готовности другой системы — использовать `Update`-ретрай или явный гейт готовности (`IsInitialized`/`EnsureInitialized`), а не `throw`.
5b. При teardown (`Dispose`/`OnDestroy`) серверных окон допускается гонка с выгрузкой сцены: `UIDocument` может быть уже уничтожен. Очистку окна (`rootVisualElement.Remove`) оборачивать в null-проверку и `try/catch` — teardown не должен ронять `OnDestroy`.
6. `FPSCounter` использует UI Toolkit `UIDocument` и не создаёт legacy `Canvas`.
7. `ProgrammatorGrid` ширина контейнера: `COLS * (CELLSIZE + CELL_GAP * 2 + 2f)`; `+2f` обязателен из-за border.
8. UI Toolkit не поддерживает `calc()`: использовать готовое число или inline-стиль из C#.
9. Shader-анимация terrain и frame-atlas — независимые признаки: `AnimationSpeed` применяется и при одном texture frame; `FrameOffset=0` нельзя превращать в скрытую высоту кадра.
10. Контракт ввода: нет EventSystem, клавиатурная навигация UI подавлена, `IsInputBlocked` включает фокус чата, экран→панель — только `ScreenToPanel`. Нарушение любого пункта даёт класс багов «робот двигается/копает, когда не должен» (см. разделы «Сеть и UI» и «Игрок»).

## 6. Workflow и диагностика

- **Кэш Unity никогда не считать причиной дефекта.** Не списывать ошибки на `Library/`, кэш импорта, кэш шейдеров или layout-кэш редактора. Причину искать в исходном коде, сериализованных данных, настройках проекта и фактическом runtime-состоянии; очистка кэша не является исправлением.
- **Перекомпиляция не является универсальным объяснением.** Нельзя объяснять визуальный или runtime-дефект только тем, что «Unity не перекомпилировал скрипты», «сборка старая» или «нужно обновить домен». Сначала обязательно проверить саму реализацию, сериализованные ссылки/настройки, фактические параметры объектов и диагностические логи. Перекомпиляцию можно указать только как отдельно подтверждённый blocker после доказательства, что исполняемый код действительно отличается от исходников.
- **Компиляция никогда не равна «игра работает».** Успешный `dotnet build`/отсутствие ошибок компиляции ничего не говорит о фактическом поведении игры в Play Mode — это лишь проверка синтаксиса и типов. Нельзя делать вывод об исправности или поломке проекта на основании одной лишь сборки, и нельзя останавливать диагностику на «билд прошёл» или «билд не прошёл». Фактическое поведение проверять только запуском/сценарием в Unity (Play Mode, Unity MCP), а не через компилятор.
- **Разрешение экрана и Retina никогда не считать оправданием дефектов производительности.** Запрещено оправдывать просадки FPS высоким разрешением экрана, плотностью пикселей Retina, размером окна Game View или характеристиками дисплея. 2D-песочница обязана обеспечивать высокий и стабильный FPS при любых стандартных экранных разрешениях; причину искать исключительно в архитектуре алгоритмов, объёме GPU/CPU работы и лишних операциях конвейера.
- **VSync и частоту монитора никогда не считать причиной дефектов производительности.** Запрещено оправдывать или объяснять просадки FPS, спайки и высокое время этапов кадра вертикальной синхронизацией (VSync), герцовкой монитора или троттлингом. Причину искать исключительно в фактическом времени выполнения алгоритмов, аллокациях, блокировках и структуре кода.

- Две production-сцены: `Assets/Scenes/MainMenu.unity` (build index 0, только UI главного меню, без DI-графа) и `Assets/Scenes/MainGame.unity` (build index 1, весь `GameLifetimeScope`/DI-граф и геймплей); offline режим даёт `DummyConnection`. По клику "Играть" `MainMenu` грузит `MainGame` аддитивно (`SceneManager.LoadSceneAsync`), ждёт `GameManager.OnWorldLoaded`, затем выгружает себя. `GameLifetimeScope.Configure` ищет свой `UIDocument` и инжектит MonoBehaviour строго в пределах своей сцены (`gameObject.scene`), чтобы не задевать объекты параллельно загруженной `MainMenu`. Переносить объекты и менять Build Settings только через Unity MCP/Editor API, не текстовой правкой YAML. "Выход в меню" (game → menu) реализован через `BootstrapLifetimeScope.ReturnToMainMenu()`: disconnect → выгрузка MainGame → `ServiceLocator.Initialize(Bootstrap.Container)` → повторная загрузка MainMenu. Смена активной сцены при аддитивной загрузке выполняется в `GameBootstrap.PostStart` (`SetActiveScene(_ownScene)`), а не в `Configure` (сцена ещё не загружена — SetActiveScene бросил бы).
- Сборка: `BuildScript.BuildMacOS` из `Assets/Editor/`; стандартный Build Settings не копирует текстуры.
- Сцена должна содержать/инициализировать `TerrainMesh`, `TerrainRenderer`, `UIDocument`, `Main Camera`, `Global Light 2D`, `SceneSetup`, `MapManager`, `GameLifetimeScope`.
- F12 пишет runtime snapshot в `diagnostic.txt`, F11 повторно сканирует `[Inject]` в `inject_diagnostic.txt`. Edit Mode: `Fodinae/Diagnostics/Validate Injections`. Статический анализ — `scripts/inject_analysis.py`.

## 7. Линтинг C# (обязательно)

Используются Roslyn-анализаторы StyleCop (`SA`), NetAnalyzers (`CA`), Roslynator (`RCS`), Sonar (`S`) и Unity (`UNT`). После генерации/изменения C# запускать:

```bash
dotnet build Assembly-CSharp.csproj -maxcpucount -p:UseSharedCompilation=true -nodeReuse:true -clp:NoSummary 2>&1
```

`scripts/pre-commit-lint.sh` обязан собирать `Assembly-CSharp.csproj` раньше `Assembly-CSharp-Editor.csproj`: editor assembly ссылается на runtime DLL, и обратный/недетерминированный порядок создаёт ложные missing-member ошибки против старой DLL.

Все предупреждения `SA`, `CA`, `RCS`, `S`, `UNT` исправить до финального ответа. Не использовать `--no-verify` и не обходить pre-commit hooks; при зависании разобраться с причиной.

`Directory.Build.props` подключает анализаторы, `.stylecop.json` отключает нерелевантные Unity-правила, `.editorconfig` задаёт severity. Для `[Inject]` полей использовать `= null!;`.

## 8. Правила пользователя

- Не добавлять фичи без явного запроса; при вопросе об отсутствии чего-либо сначала уточнить требуемое поведение.
- Проверки (build/lint) запускать осмысленно, не без необходимости.
- Не выбирать ленивые фоллбеки и временные решения.
- **Запрещено вручную менять префабы, ассеты и сцены.**
- **Перед сценовыми правками через Unity MCP (create/update/reparent/duplicate GameObject, add_component и т.п.) обязательно проверить, что редактор не в Play Mode** (например, пробным `save_scene` — ошибка "cannot be used during play mode" значит редактор в Play Mode). Unity молча откатывает все изменения GameObject'ов в сцене при выходе из Play Mode — работа через MCP, сделанная в Play Mode, будет полностью потеряна без явной ошибки в момент правки. Скрипты (.cs) это не касается — они компилируются и сохраняются на диск независимо от Play Mode.
- **Запрещено «дрочить Unity» и команды:** не запускать Unity вручную через
  shell/batchmode, `osascript`, GUI automation или принудительный Refresh; не
  повторять build/restore/poll-команды без нового диагностического основания и
  не ждать зависшие процессы бесконечными циклами. Editor, Console, Play Mode,
  scene state и импорт проверять только через Unity MCP. Если Unity MCP не
  подключён или не экспонирует нужный инструмент, остановиться, назвать точный
  blocker и запросить восстановление MCP, а не обходить его ручным управлением.

## 9. Принцип No Implicit Defaults и Fail-Fast

- **Никаких неявных дефолтов и тихих фоллбеков**: Запрещено подставлять неявные дефолтные позиции `(0,0)`, генерировать случайные/дефолтные текстуры или тестовые карты при отсутствии оригинальных файлов, а также гасить ошибки тихими `try/catch`.
- **Fail-Fast**: При отсутствии/повреждении `.mapb` файлов, конфигов или сетевых данных немедленно прерывать процесс (`TriggerDisconnect`/исключение), не пытаясь разворачивать временные структуры.
- **Синхронизация загрузки мира**: При старте игры `MainMenu` удерживает защитный экран загрузки (`LoaderContainer`) и скрывает его только после события `GameManager.OnWorldLoaded` (когда `MapStorage.IsReady == true`, каскады и меши террейна готовы, а камера позиционирована на спавн).
- **Моментальное позиционирование камеры**: При инициализации спавна `CameraFollow` выполняет немедленный мгновенный сдвиг (`SnapToTarget()`), исключая слепой полёт камеры через карту из точек по умолчанию.
- **Запрет O(Heap) обходов в рантайме**: Запрещено использовать `Resources.FindObjectsOfTypeAll`, глобальное сканирование рефлексией по всем скриптам сцены или некэшированные вызовы `GetComponent` в `Update`/`LateUpdate`/`PostProcessRenderPass`.
