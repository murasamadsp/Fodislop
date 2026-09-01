- тексты написать (без иишки)
- компонентно-солид mvp рефакторинг
- тонна блять синхронных (серийных) процессов и гонок определений
- [ ] добавить пимпочку справа снизу которое показывает состояние загрузки ассетов и туда вынести и версию билда и фпс и пинг и т.п.
- [x] Восстановить полноценный локальный чат в активном `GlobalChatUI`: отдельный local-tab, открытие клавишей T, общий lifecycle/input blocking, отправка `SendLocalChatMessagePacket`, приём `LocalMessageReceived` и отдельная история канала. Старый неподключённый `LocalChatPopup` не возвращать.
- физику вырезать, матрицы коллизий??? чо это?? тоже вырезать.... слои юнити сделать
- [ ] реализовать Render Governor / Frame Budget Coordinator (кадрирование и разделение тяжелых задач рендера: terrain remesh, batch sprite rebuild, minimap/worldmap pixel sampling и UI painter во избежание микро-статтеров в одном кадре)

## Реестр огромных production C# файлов

Критерий: production-файл больше 500 строк должен быть разбит по ответственностям,
а не механически превращён в `partial`. Новые файлы больше 500 строк запрещены;
текущий конечный debt-list охраняется архитектурным линтером и сокращается до нуля.

- [ ] `World/Lighting/Core/LightingEngine.cs` (2015): coordinator/resources/scheduling/pipelines.
- [ ] `World/Persistence/WorldLayer.cs` (1081): format/index/cache/IO/compaction.
- [x] `Networking/Connection/Client/DummyConnection.cs` (393, было 1154): разделён на session/auth/player+world simulation/movement/gameplay+chat+inventory+window+asset responders.
- [ ] `World/Terrain/Core/TerrainRenderer.cs` (874): lifecycle/coverage/mesh/material updates.
- [ ] `UI/Overlays/InGameDebugOverlay.cs` (837): state/sampling/presenter/view binding.
- [ ] `AssetPipeline/Animation/GifAnimationDecoder.cs` (774): parser/LZW/compositing/output.
- [ ] `UI/Chat/GlobalChatUI.cs` (727): state/presenter/view binding.
- [ ] `Rendering/PostProcessing/PostProcessRenderPass.cs` (708): resources/scheduling/effect passes.
- [ ] `Game/Entities/Robot.cs` (692): state/visual loading/presentation.
- [ ] `UI/Menu/Core/MainMenu.cs` (685): state/presenter/view binding/transition flow.
- [ ] `World/Textures/WorldTextureManager.cs` (661): loading/cache/atlas orchestration.
- [ ] `AssetPipeline/Loading/ClientAssetLoader.cs` (656): requests/cache/batching/decoding.
- [ ] `UI/Programmator/Model/ProgrammatorData.cs` (650): model/serialization/commands.
- [ ] `UI/Gateway/GatewayController.cs` (646): auth state/presenter/view binding.
- [ ] `UI/Map/WorldMapRenderer.cs` (638): sampling/texture updates/presentation.
- [ ] `World/Rendering/BackgroundFloodFill.cs` (627): traversal/cache/output.
- [ ] `UI/Programmator/Grid/ProgrammatorClipboardController.cs` (627): clipboard/selection/commands.
- [ ] `AssetPipeline/Cache/AssetCacheEntry.cs` (624): entry state/download/decode/lifecycle.
- [ ] `UI/HUD/Player/View/PlayerHUDView.cs` (621): binding/presenter/subviews.
- [ ] `Game/Audio/ServerAudioEvent.cs` (599): lifecycle/audio/VFX/loading.
- [ ] `UI/Settings/PauseMenu.cs` (596): state/presenter/view binding.
- [ ] `World/Terrain/Mesh/TerrainMeshBuilder.cs` (587): topology/attributes/output.
- [ ] `World/Lighting/Core/LightingResourceManager.cs` (577): allocation/ownership/destruction.
- [ ] `Player/Controllers/PlayerMovementController.cs` (567): state/input/network movement.
- [ ] `World/Textures/TextureAtlas.cs` (561): packing/storage/upload.
- [x] `World/Lighting/Config/LightingConfigHolder.cs` (491, было 557): ClientConfig mapping/normalization вынесены в `LightingRuntimeConfigMapper`.
- [x] `UI/Menu/Scenery/MenuSceneryController.cs` (498, было 554): viewport projection и occlusion вынесены в `MenuSceneryProjection`.
- [x] `World/Terrain/Cache/TerrainCellCache.cs` (469, было 547): сдвиг coverage-массивов вынесен в `TerrainCacheArrayScroller`.
- [x] `Game/Rendering/WorldEntityBatchRenderer.cs` (463, было 547): texture atlas packing/upload/ownership вынесены в `WorldEntityTextureAtlas`.
- [x] `Rendering/PostProcessing/PostProcessController.cs` (493, было 545): profile validation/component override setup вынесены в `PostProcessDefaults`.
- [x] `UI/Map/MinimapController.cs` (498, было 541): UXML binding, координаты и видимость вынесены в `MinimapView`.
- [x] `World/Rendering/SurfaceRenderer.cs` (454, было 518): mesh/component/lighting lifecycle вынесен в `SurfaceMeshUtilities`.

## Программа оздоровления клиента (6–9 месяцев)

Цель: воспроизводимые macOS ARM64 / Windows x64 релизы, отсутствие потери
локальных данных и управляемый lifecycle без скрытых фоновых операций.

### 1. Честная сборка и зависимости

- [x] Заменить фиктивный Linux `dotnet build` gate на Unity EditMode/PlayMode jobs.
- [x] Добавить обязательные macOS ARM64 и Windows x64 IL2CPP builds.
- [x] Закрепить Git UPM-зависимости конкретными commit SHA.
- [x] Валидировать Build Settings без автоматического изменения авторских данных.
- [ ] Подключить лицензированные self-hosted runners с меткой `fodinae-unity`.
- [ ] Зафиксировать performance baseline и бюджеты регрессий.

### 2. Async lifecycle и сохранность мира

- [x] Не удалять dirty chunk из RAM до успешного завершения eviction-save.
- [x] Ввести единый `IAsyncLifetime` и владельца фоновых задач.
- [x] Запретить голый `.Forget()` вне task supervisor.
  - [x] Перевести batch-loop ассетов, FMOD/feature-банки, мировые/packet-текстуры, post-connect, переходы сцен, HUD/chat delays и surface setup под supervisor.
  - [x] Удалить неиспользуемый `LocalChatPopup` (нет C#-потребителей и сериализованных GUID-ссылок).
  - [x] Перевести загрузку визуалов `Robot`/`Building` под supervisor и объединить связанные robot-assets через structured `WhenAll`.
  - [x] Перевести динамическую загрузку `ServerAudioEvent` VFX под supervisor.
  - [x] Перевести offline connect/disconnect, world init, packet responses, pathing и dummy simulation loops под supervisor.
  - [x] Запретить новые `.Forget()` во всём production-коде; старый долг зафиксировать конечным allowlist.
  - [x] Перевести оставшиеся allowlist-владельцы; единственное исключение — внутренний запуск самого supervisor.
- [x] Ожидать остановку сети и durable flush перед выгрузкой `MainGame`.
- [x] Добавить `FlushAsync`, `DisposeAsync` и сериализацию persistence-операций.
- [x] Разделить состояния чтения чанка: available/loading/missing/failed.
- [x] Версионировать world-layer format и мигрировать v0→v1 атомарно с backup.
- [x] Версионировать config/cache formats и выполнять атомарные миграции с backup.
  - [x] `client_config.json`: schema v15, последовательные миграции, durable atomic replace и `.vN.backup`.
  - [x] `AssetCache`: schema v1 marker, metadata-only v0 backup и atomic marker commit/recovery без копирования или повторной загрузки payload-файлов.

### 3. Границы модулей

- [x] Оставить в `Fodinae.Contracts` только интерфейсы, DTO и value types.
- [x] Перенести `WorldLayer<T>` и файловый формат в `Fodinae.Persistence` assembly.
- [ ] Разбить `Fodinae.Runtime` на Core/Application/Infrastructure/Presentation.
- [ ] Закрыть implementation types через `internal` и тестировать граф asmdef.

### 4. Декомпозиция god-object'ов

- [ ] Разделить `LightingEngine` на coordinator, resources, scheduling и pipelines.
- [x] Разделить `DummyConnection` на session, auth, simulation и responders.
  - [x] Вынести generation-based lifecycle/status в `DummyConnectionSession`.
  - [x] Вынести offline identity и token resolution в `DummyAuthSession`.
  - [x] Вынести mutable player state (position/direction/HP/toggles/basket/geology) в `DummyPlayerSimulationState`.
  - [x] Вынести `WorldLayer`, cell-configs, sent-chunk cache и single-flight gate в `DummyWorldSimulationState`.
  - [x] Вынести стартовый world/player/status/inventory snapshot и bot/ping/online loops в `DummyWorldStartupResponder`.
  - [x] Разнести packet responders из центрального `SendAsync`.
    - [x] Вынести state/history/local/global chat packets в `DummyChatResponder`.
    - [x] Вынести selection/use-item и inventory state в `DummyInventoryResponder`.
    - [x] Вынести routing daily bonus/teleport/clan/missions/test windows в `DummyWindowResponder`.
    - [x] Вынести move/rotate/click-path, cancellation и position snapshots в `DummyMovementResponder`.
    - [x] Вынести dig/suicide/geology/heal/build в `DummyGameplayActionResponder`.
    - [x] Вынести runtime asset responses в `DummyAssetResponder`.
- [x] Разделить config repository, migration и runtime settings.
  - [x] Вынести чтение, legacy-key normalization и durable atomic save/backup в `ClientConfigRepository`.
  - [x] Вынести последовательные schema migrations из `ClientConfigManager` в `ClientConfigMigration`.
  - [x] Вынести default construction и validation; оставить в runtime manager lifecycle, состояние и применение пользовательских настроек.
- [ ] Разделить крупные UI-классы на view binding, presenter и state.

### 5. Системная проверка релиза

- [ ] Покрыть reconnect, pause/resume, disk failures и UI input PlayMode-тестами.
- [ ] Добавить GPU lifecycle integration tests для lighting.
- [ ] Сделать dummy-сценарии детерминированными через virtual clock.
- [ ] Добавить nightly soak: 50 переходов сцен, reconnect storm и streaming карты.
- [ ] Проверять миграцию двух предыдущих форматов и clean/upgrade install.

Критерии завершения: обе production-сборки запускаются из чистого checkout;
50 циклов Menu/Game не оставляют задач, подписок и объектов; fault-injection не
теряет dirty chunks; p95 frame time и память не ухудшаются более чем на 5%.
