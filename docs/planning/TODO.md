# TODO

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

## HDR

- [ ] Вывод переключается HDR→SDR→HDR→SDR на старте: Unity включает HDR (в Player Settings стоит Allow HDR Display Output), `HDROutputReconciler` выключает по настройке игрока, Unity включает обратно. Каждое переключение пересоздаёт swapchain — провисы в сотни мс. Решить: выключить Allow HDR Display Output (HDR в игре станет невозможен) или не бороться с движком при старте и смене фокуса.
