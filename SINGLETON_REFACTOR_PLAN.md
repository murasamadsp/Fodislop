# План рефакторинга синглтонов в Fodinae

## Цель
Устранить все статические синглтоны и перейти на чистый VContainer DI.

## Статус: Этап 1-4 + Этап 5 (частично) завершены. Остались мосты в Model-слоях.

## Этап 1: Подготовка

### 1.1 Аудит синглтонов
- [x] Составить полный список всех синглтонов (15+ классов)
- [x] Проанализировать все точки доступа к `.Instance` и `.InstanceIfExists`
- [x] Выявить циклические зависимости
- [x] Составить матрицу зависимостей

### 1.2 Подготовка инфраструктуры
- [x] `ServiceLocator` как bridge → VContainer (уже есть)
- [x] `GameLifetimeScope.RegisterManager<T>()` для MonoBehaviour-синглтонов
- [x] `RegisterManager<T>` теперь вызывает `resolver.Inject(existing)` для existing-экземпляров
- [x] `InjectDiagnostic` — авто-скан [Inject] полей в Start()
- [x] `InjectValidator` — Editor-инструмент (Fodinae → Diagnostics → Validate Injections)

## Этап 2: Рефакторинг Model-слоя — ГОТОВО

### 2.1 PlayerStatsModel — ГОТОВО
- [x] Класс `sealed class`, не MonoBehaviour
- [x] Статическое `Instance`/`InstanceIfExists` заменены на ServiceLocator bridge
- [x] Все обращения `PlayerStatsModel.Instance` удалены из:
  - `ClanProcessor`, `MissionProcessor`, `StatusProcessor`, `PlayerStatsProcessor`, `PlayerInfoProcessor`
  - `DummyConnection`, `PlayerHUDPresenter`, `PlayerHUDView`

### 2.2 InventoryModel — ГОТОВО
- [x] Статическое `Instance` удалено
- [x] `InventoryProcessor` → `ServiceLocator.Resolve<IInventoryModel>()`
- [x] `InventoryView` → `ServiceLocator.Resolve<IInventoryModel>()`
- [x] `InventoryPresenter` уже имеет `[Inject] IInventoryModel`

### 2.3 ServerConfig — ГОТОВО
- [x] Статическое `_instance`, `Instance`, `Awake()`, `OnDestroy()` удалены
- [x] `PlayerMovementController` → `[Inject] IServerConfig _serverConfig`
- [x] `GlobalChatUI`, `LocalChatPopup` → `ServiceLocator.Resolve<IServerConfig>()`
- [x] Регистрация в `GameLifetimeScope` с `.AsImplementedInterfaces().AsSelf()`

## Этап 3: Сетевой слой — ГОТОВО

### 3.1 NetworkService — ГОТОВО
- [x] Все обращения `NetworkService.Instance` удалены
- [x] `PacketHandler` → `ServiceLocator.Resolve<INetworkService>()`
- [x] `DummyConnection` → `ServiceLocator.Resolve<INetworkService>()`
- [x] `ConnectionManager` → `ServiceLocator.Resolve<INetworkService>()`

### 3.2 ConnectionManager — ГОТОВО
- [x] Статическое `_instance`, `Instance`, `InstanceIfExists`, `Awake()`, `OnDestroy()` удалены
- [x] Все обращения `ConnectionManager.Instance` удалены
- [x] `GameManager.InstanceIfExists` → `ServiceLocator.Resolve<GameManager>()`

### 3.3 PacketHandler — ГОТОВО
- [x] Статическое `Instance` удалено
- [x] `IsInputBlocked` и `TopWindowTag` ставятся instance-членами `IInputBlocker`
- [x] `PacketHandler` реализует `IInputBlocker`
- [x] `[Inject] IMapDataProvider _mapManager` добавлен
- [x] Все `MapManager.Instance`, `MapStorage.Instance` удалены
- [x] `_isSubscribed` для идемпотентности подписок
- [x] `GameManager.Instance` → `ServiceLocator.Resolve<GameManager>()`

## Этап 4: Игровые менеджеры — ЧАСТИЧНО ГОТОВ

### 4.1 MapManager — ГОТОВО (мост остаётся временно)
- [x] `IMapDataProvider` расширен методом `GetMoveCooldown()`
- [x] `PlayerMovementController` → `[Inject] IMapDataProvider _mapDataProvider`
- [x] Все 40+ ссылок `MapManager.Instance` устранены:
  - `SingleMeshTerrainRenderer`, `WorldTextureManager`, `TextureAtlas`, `Robot`
  - `DummyConnection`, `PacketHandler`, `GameLifetimeScope`, `WorldInitProcessor`
  - `PackManager`, `WorldAudioController`, `WorldMapController`, `WorldMapRenderer`, `MinimapController`
- [ ] `InstanceIfExists` bridge остаётся до полной миграции остальных Processors

### 4.2 MapStorage — ГОТОВО
- [x] `_instance` с lock → ServiceLocator bridge (`_pendingInstance` для GameLifetimeScope)
- [x] Все обращения заменены:
  - `PlayerMovementController`, `SingleMeshTerrainRenderer` → `_storage` (уже инжектирован)
  - `DummyConnection`, `PacketHandler`, `MapRegionProcessor`, `WorldTextureManager`
  - `StandaloneWorldInitializer`, `MinimapController`, `WorldMapRenderer`, `WorldMapController`
  - `MapManager.WorldStorage` → `ServiceLocator.Resolve<IWorldDataStorage>()`

### 4.3 GameManager — ОСТАВЛЕН (MonoBehaviour сцен-связан)
- [x] `_instance` сохранён — жизненный цикл UI создаётся из SetupUI()
- [x] `Instance`/`InstanceIfExists` — легитимный сценарный синглтон

### 4.4 RobotManager, PackManager, ServerAudioEventManager, VFXPool
- [x] Остаются MonoBehaviour-синглтонами с DI через `RegisterManager<T>`

## Этап 5: Аудио и ассеты — ЧАСТИЧНО ГОТОВ

### 5.1 AudioSystem — ЧАСТИЧНО
- [x] `PauseMenu` → `[Inject] IAudioSystem _audioSystem`
- [ ] Осталось: 11+ ссылок в других местах

### 5.2 ClientAssetLoader — НЕ НАЧАТ
- [ ] Осталось: 19+ ссылок `ClientAssetLoader.Instance`

### 5.3 WorldTextureManager — ЧАСТИЧНО
- [x] Все `MapManager.Instance` устранены (null-guards добавлены)
- [ ] Осталось: 6+ прямых `ServiceLocator.Resolve<MapManager>()` в WorldTextureManager

## Этап 6: UI и SingleMeshTerrainRenderer — ЧАСТИЧНО ГОТОВ

### 6.1 SingleMeshTerrainRenderer — ЧАСТИЧНО
- [x] `PauseMenu` → `[Inject] SingleMeshTerrainRenderer _terrainRenderer`
- [ ] Осталось: 1 ссылка `Instance` в других местах

### 6.2 PauseMenu — ГОТОВО
- [x] `AudioSystem.Instance` → `[Inject] IAudioSystem`
- [x] `SingleMeshTerrainRenderer.Instance` → `[Inject] SingleMeshTerrainRenderer`
- [x] `PacketHandler.IsInputBlocked` → `[Inject] IInputBlocker`

### 6.3 CameraFollow — ГОТОВО
- [x] `PacketHandler.IsInputBlocked` → `[Inject] IInputBlocker`

### 6.4 Остальные UI
- [ ] `GlobalChatUI`, `FloatingChatManager`, `FPSCounter` — MonoBehaviour-синглтоны, остаются

## Этап 7: Финальная очистка — НЕ НАЧАТ

## Изменения в this session

### Файлы изменены:
- `Core/GameLifetimeScope.cs` — `RegisterManager<T>` теперь inject existing; `ValidateStartup()` с reflection; bulk inject всех сценарных MonoBehaviour
- `Networking/PacketHandler.cs` — удалён `Instance`; `IInputBlocker` instance-члены; `[Inject] IMapDataProvider`; `_isSubscribed`
- `Networking/Processors/WorldInitProcessor.cs` — null-guard для MapManager
- `Networking/Connection/Client/DummyConnection.cs` — все `.Instance` → ServiceLocator; null-guards
- `Networking/Processors/WindowPacketProcessor.cs` — null-guards для UIInputManager и element
- `UI/PauseMenu.cs` — `[Inject] IAudioSystem`, `[Inject] SingleMeshTerrainRenderer`, `[Inject] IInputBlocker`
- `Player/CameraFollow.cs` — `[Inject] IInputBlocker`
- `Player/PlayerInteractionController.cs` — guard на null Keyboard
- `Player/Logic/PlayerMovementController.cs` — guard на null `_inputBlocker`, `_mapManager`
- `World/SingleMeshTerrainRenderer.cs` — guard в `BuildMeshDataIncremental`; `canScroll` condition исправлен
- `World/StandaloneWorldInitializer.cs` — `Update()` фолбэк на `MapStorage.IsReady`
- `World/WorldTextureManager.cs` — null-guards для MapManager resolve
- `World/TextureAtlas.cs` — null-guards для MapManager resolve
- `Game/Managers/MapStorage.cs` — guard в `GetCell`, `SetCell`
- `UI/HUD/Player/View/PlayerHUDView.cs` — guard на null `_inputBlocker`
- `UI/HUD/Inventory/View/InventoryView.cs` — guard на null `_inputBlocker`
- `InjectDiagnostic.cs` — авто-скан в Start(); F11 ручной рескан; Error на null fields
- `Editor/InjectValidator.cs` — новый файл: меню Fodinae → Diagnostics → Validate Injections

## Правила рефакторинга

1. **Каждый этап должен компилироваться** — проверяется `dotnet build`
2. **Сначала интерфейсы, потом реализации** — `[Inject]` перед удалением `.Instance`
3. **Не менять логику работы** — только менять способ доступа к сервисам
4. **MonoBehaviour-синглтоны** сцен-связанные (GameManager, MapManager) допустимы
5. **ServiceLocator bridge** — временный мост для компонентов без DI (UI, Processors)
