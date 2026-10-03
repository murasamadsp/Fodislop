# TODO

## Системные оптимизации и графический пайплайн

- [ ] **1. Compute Shader 2D Signed Distance Field (SDF) & Jump Flooding (JFA) вместо пошагового DDA**
  - Генерация точного 2D SDF за $\log_2(N)$ проходов Jump Flooding compute-шейдера.
  - Трассировка каскадов освещения (Radiance Cascades) через Sphere Tracing (Ray Marching) по полю расстояний вместо пошагового воксельного DDA.
  - Subgroup/Wavefront intrinsics (`WaveReadLaneFirst`, `WaveActiveBitOr`) для когерентности лучей в варпах на Metal/Vulkan.

- [x] **2. Burst / SIMD / C# Job System для сборки геометрии террейна**
  - Перевод `TerrainQuadBuilder`, `TerrainMeshManager` и генерации геометрии/деформаций на `[BurstCompile]` и C# Jobs.
  - Устранение managed-аллокаций при перестроении квадов террейна (использование `NativeArray`, `UnsafeUtility`).
  - SIMD-векторизация расчёта смещений вершин, дисторшна и упаковки half-texel координат.

- [x] **3. GPU Driven Rendering & DrawMeshIndirect для динамических объектов (мобы / кристаллы / дроп)**
  - Хранение параметров инстансов (позиция, UV/кадр, цвет, масштаб) в `ComputeBuffer` / `GraphicsBuffer` (StructuredBuffer).
  - Отрисовка всех динамических визуальных объектов через `Graphics.RenderMeshIndirect` / `DrawMeshInstancedIndirect` за 1 Draw Call.
  - Освобождение CPU от обхода GameObject/Transform и матричных трансформаций Unity.

## Провисы кадра в игре

- [ ] Прогнать `Kern.Tests.PlayMode.FrameStallPlayModeTests` (PlayMode): тест стоит и ходит 15 с, пишет все маркеры движка и при провисе падает со списком маркеров, выросших в провисших кадрах. Чинить по этому списку.
- [ ] Разобрать маркеры, которые сейчас растут в провисах (по `[FrameStall]` 23.09):
  - `GenerateTextMesh` +29 мс — генерация текста UI Toolkit (TextCore). Найти текст, который перестраивается.
  - `CopyChannels` +5…26 мс — копирование вершинных данных меша (`Runtime/Graphics/Mesh/VertexData.cpp`). Найти меш.
  - `WaitForJobGroupID` +30…50 мс — главный поток ждёт джобы движка (наш код Unity-джобов не запускает).
  - `Loading.ReadObject` / `Loading.LoadFileHeaders` / `AsyncReadManager.SyncRequest` — синхронная загрузка ассетов на главном потоке в игре.
  - `Material.SetPassFast`, `GarbageCollector.CollectIncremental`.
- [ ] План кадра террейна стоит 10–25 мс каждый кадр (`[TerrainStall] план 10.6 / 24.9`): найти, что в `TerrainFramePlanner.Plan` так дорого (пробы резидентности, `TerrainWindowAdvance`).
- [ ] Обработчики пакетов при входе в мир: `WorldInitPacket` 155–745 мс, `RobotInfoPacket` 58 мс, `RobotPositionPacket` 39 мс, `SkillProgressPacket` 14 мс.
- [ ] Создание окна террейна при входе: «размеры» 155–425 мс (пересоздание меша клеток и текстур).
- [ ] Полный статический расчёт света при загрузке мира замораживает загрузочный экран на ~0.7 с (`[FrameStall]` 03.10, кадр 3746). Оценка `estimatedStaticRayWork=243712000` при лимите 200 000 000 (`[Lighting] Budget diagnostic`, поле 160×128 при PerPixel). Расчёт идёт один раз: регион света с 03.10 мерится кадром максимального отдаления. Нужно, чтобы этот кадр не стоял: расчёт за загрузочным экраном, не на кадре его анимации.

## HDR

- [x] Вывод переключается HDR→SDR→HDR→SDR на старте: Unity включает HDR (в Player Settings стоит Allow HDR Display Output), `HDROutputReconciler` выключает по настройке игрока, Unity включает обратно. Каждое переключение пересоздаёт swapchain — провисы в сотни мс. Решить: выключить Allow HDR Display Output (HDR в игре станет невозможен) или не бороться с движком при старте и смене фокуса.
  - Решено 03.10: в редакторе `HDROutput.SetEnabled` пишет выбор игрока в `PlayerSettings.useHDRDisplay`, Unity стартует и восстанавливает вывод в нужном режиме; сборка запекает `DefaultHDREnabled` (`BuildScript`). В собранной игре игрок с SDR при HDR по умолчанию получает одно переключение на старте — стартовый режим в рантайме не задаётся.
