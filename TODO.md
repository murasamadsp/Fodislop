- тексты написать (без иишки)
- компонентно-солид mvp рефакторинг
- тонна блять синхронных (серийных) процессов и гонок определений
- [ ] добавить пимпочку справа снизу которое показывает состояние загрузки ассетов и туда вынести и версию билда и фпс и пинг и т.п.
- [x] Восстановить полноценный локальный чат в активном `GlobalChatUI`: отдельный local-tab, открытие клавишей T, общий lifecycle/input blocking, отправка `SendLocalChatMessagePacket`, приём `LocalMessageReceived` и отдельная история канала. Старый неподключённый `LocalChatPopup` не возвращать.
- физику вырезать, матрицы коллизий??? чо это?? тоже вырезать.... слои юнити сделать
- [ ] реализовать Render Governor / Frame Budget Coordinator (кадрирование и разделение тяжелых задач рендера: terrain remesh, batch sprite rebuild, minimap/worldmap pixel sampling и UI painter во избежание микро-статтеров в одном кадре)

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
- [ ] Разделить `DummyConnection` на session, auth, simulation и responders.
- [ ] Разделить config repository, migration и runtime settings.
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
