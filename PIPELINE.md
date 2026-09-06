# PIPELINE.md — Графический пайплайн Fodinae

Графика Fodinae — это 2D-рендеринг на Unity URP с процедурным террейном,
глобальным освещением Radiance Cascades на GPU Compute и собственным
постпроцессингом. Документ описывает движение кадра и границы между
доменами; числа и имена сверены с кодом.

---

## 1. Схема движения кадра

```text
[1. Сеть / Чанки]   →  MapStorage (32×32 чанки, RLE)
       ↓
[2. CPU геометрия]  →  TerrainCellCache → TerrainPrecalculator
                       → BackgroundFloodFill → TerrainMeshBuilder
       ↓
[3. Растеризация]   →  Pass "LightingMaterialField"
                       → _MaterialField (окклюзия + альбедо)
                       → _EmissionField (свечение блоков)
       ↓
[4. GPU освещение]  →  WorldLighting.compute, пять ядер:
                       Normals → DynamicEmission → Cascades (статика | динамика)
                       → ResolveDirect → DiffuseBounce → Composite
       ↓
[5. Отрисовка]      →  Terrain.shader (сэмплирует _WorldLightTexture)
                       + WorldEntityBatchRenderer + роботы
       ↓
[6a. Сцена]         →  PostProcess.compute / CompositeFinal
                       (Bloom → CA → грязь линзы → грейдинг → временная стабилизация)
       ↓
[6b. Дисплей]       →  PostProcess.compute / DisplayFinal
                       (виньетка → зерно → paper white → HDR-кодирование)
       ↓
[6c. Приборы]       →  Scopes.compute (только когда открыто рабочее место)
       ↓
[7. Интерфейс]      →  UI Toolkit (UIDocument, ScreenToPanel)
```

Шаги 6a и 6b — **два разных прохода URP**, а не два блока одного ядра.
Первый встаёт на `BeforeRenderingPostProcessing`, второй на
`AfterRenderingPostProcessing`. Между ними URP владеет выводом на дисплей.

---

## 2. Домены пайплайна

### 1. Данные мира

- **Файлы:** `World/Storage/MapStorage.cs`, `World/Persistence/WorldLayer.cs`
- Клетки мира в чанках 32×32 (`ProjectRuntimeContracts.World.ChunkSize`),
  RLE-сжатие на диске (`.mapb`) и в памяти. Размер клетки — `1f`.

### 2. Сборка меша

- **Файлы:** `World/Terrain/Core/TerrainRenderer.cs`,
  `World/Terrain/Cache/TerrainCellCache.cs`,
  `World/Terrain/Mesh/TerrainPrecalculator.cs`,
  `World/Rendering/BackgroundFloodFill.cs`,
  `World/Terrain/Mesh/TerrainMeshBuilder.cs`

Порядок:

1. Кэш клеток квантуется вокруг камеры и переиспользуется при сдвиге.
2. `TerrainPrecalculator` считает 47-битный автотайлинг и рельеф.
3. `BackgroundFloodFill` волной строит заднюю стену в пустотах.
   Есть два пути: `ComputeFull` и `ComputeScrolled(dx, dy)` — второй
   переносит прошлый кадр и досчитывает только открывшуюся полосу.
   **Полосу нельзя засевать одной ею:** клетки внутри камня остаются без
   источника, и фон вымывается по одной полосе за сдвиг. Поэтому в
   границу дополнительно кладётся уже разрешённая внутренняя линия.
4. `TerrainMeshBuilder` собирает один меш. Вершин —
   `meshWidth × meshHeight × 2 × 4` (два слоя, фон и перёд, по квадру на
   клетку). Это **порядок миллиона вершин** при типичной области, а не
   десятки тысяч; вершина упакована в 84 байта (`TerrainVertex`),
   семь UV-каналов, часть в Float16.
5. Правки идут точечно: `TerrainMeshBuilder` отдаёт узкий диапазон
   `DirtyVertexStart`/`DirtyVertexCount`, `TerrainRenderer` объединяет
   диапазоны всех грязных прямоугольников кадра, и
   `TerrainMeshManager.UploadDirectVertexBuffer` заливает только его.
   Заливать весь буфер на каждую заплатку — заметная цена при ходьбе.

### 3. Растеризация полей

- **Файл:** `Assets/Shaders/Terrain.shader`, проход `LightingMaterialField`
  (`LightMode = FodinaeLightingMaterialField`)
- `_MaterialField` (RGBA): альбедо в RGB, окклюзия в A.
- `_EmissionField` (RGBA): свечение блоков.

Динамические источники (лампы роботов) в этот проход **не входят** — они
приходят отдельной стадией на шаге 4.

### 4. GPU Radiance Cascades

- **Файлы:** `World/Lighting/Core/LightingEngine.cs`,
  `Assets/Resources/Shaders/Lighting/WorldLighting.compute`
- **Ядра:** `SolveAutomaticNormals`, `SolveCascade`, `ResolveDirect`,
  `SolveDiffuseBounce`, `CompositeLighting`.
- **Стадии C#:** `World/Lighting/Pipeline/Stages/` — `MaterialFieldStage`,
  `DynamicEmissionCompositionStage`, `AutomaticNormalsStage`,
  `DiffuseBounceStage`, `CompositeStage`. Стадия умеет записать свой
  диспатч и не знает, когда её вызывать; решение о вызове остаётся в
  `LightingEngine.UpdateLighting`.

#### Разделение на половины

Ключевое устройство, без которого ходьба стоила бы полного решения на кадр:

- **Статическая половина** — свет террейна. Пересчитывается только когда
  меняется геометрия, от которой она зависит, и **не** когда сдвинулась
  лампа (`staticRadianceChanged = rebuildFields || !_hasStaticRadianceState`).
  Решается по всем каскадам, результат живёт в `_staticDirectTexture`.
- **Динамическая половина** — лампы. Пересчитывается при движении
  источников, но урезана до `maxCascades: min(3, _cascades.Count)`:
  дальний каскад лампам не нужен.

Отсюда следует правило, которое легко нарушить: при решении половины
`hasFarCascade` считается от **числа продиспатченных** каскадов, а не от
общего их числа. Иначе динамическая половина читает из атласа каскад,
который принадлежит статической, и свет террейна учитывается дважды.

#### Цена

Луч каскада 0 — самый дорогой участок кадра. Записанный замер: порядка
46.9 млн шагов луча и 69.8 млн выборок атласа за одно решение.
Билинейная поправка (`_EnableBilinearFix`) заменяет один марш
шестнадцатью (`4 направления × 2 × 2 угла`), каждый длиннее обычного, —
это выводит счёт за 700 млн и было подтверждённой причиной падения
кадра в двадцать раз при ходьбе. Поэтому поправка ограничена каскадом 0,
а углы с нулевым весом пропускаются до чтения атласа.

#### Область решения

`LightingRegionCalculator`: якорь 8 клеток, квант размера 32,
**отступ 16 клеток** в каждую сторону плюс гистерезис. Свет считается по
площади заметно больше видимой, и отступ входит в цену квадратом — это
авторский рычаг, а не константа реализации.

### 5. Отрисовка мира

- **Файлы:** `Assets/Shaders/Terrain.shader`,
  `Game/Entities/Robot.cs`, `World/Rendering/WorldEntityBatchRenderer.cs`
- Террейн интерполирует `_WorldLightTexture` (глобали публикует
  `LightingEngine.PublishLightingGlobals`), накладывает анимации лавы,
  мерцание и UV-сдвиги.
- Режим «свет выключен» — не альтернативная реализация: ключевое слово
  шейдера гасится, и вариант фрагмента возвращает единицу без выборки.

### 6. Постпроцессинг

- **Файлы:** `Rendering/PostProcessing/`,
  `Assets/Resources/Shaders/PostProcessing/PostProcess.compute`,
  `ColorGrading.hlsl`, `Scopes.compute`
- Ядра: `BloomPrefilter`, `BloomDownsample`, `BloomUpsample`,
  `CompositeFinal`, `DisplayFinal`.

`PostProcessRendererFeature` ставит в очередь **два** прохода одного и
того же класса `PostProcessRenderPass`: обычный выбирает ядро
`CompositeFinal`, флаг `displayPass: true` — `DisplayFinal`. Оба
работают только для базовой игровой камеры без `targetTexture`.

#### 6a. Сцена — `CompositeFinal`

Пирамида блума (префильтр в половину, вниз, вверх) — только когда блум
активен; иначе интенсивность обнуляется и подвязывается чёрная текстура.
Дальше: хроматическая аберрация, тепловое искажение, грязь на линзе,
цвет, временная стабилизация.

**Цветовой конвейер.** Кривой сжатия диапазона в проекте нет — коммит
`c653ee65` удалил её целиком. Порядок слоёв не произволен:

```text
сцен-линейный кадр
  → экспозиция (стопы) и цветофильтр   ┐ физика: описывает съёмку
  → баланс белого (temp/tint)          ┘
  → FodinaeLogEncode
  → CDL: slope / offset / power        ┐ вкус: шаг в логе равномерен
  → контраст с опорой 0.5              ┘ по восприятию
  → FodinaeLogDecode
  → насыщенность
```

Опора контраста 0.5 — средне-серый собственной лог-шкалы. Творческий
грейд независим от дисплейного слоя: обход одного не выключает другой,
и шторка сравнения `_CompareSplit` показывает именно одно отличие.

Пороги, зависящие от шкалы (`Bloom.Threshold`, порог бликов), заданы в
сцен-линейных величинах. Числа, настроенные под удалённую кривую,
осмысленными больше не являются — это известное открытое место.

#### 6b. Дисплей — `DisplayFinal`

Виньетка, плёночное зерно (`FilmGrain`, в физических пикселях экрана),
деление и умножение на `_DisplayPaperWhiteNits`, `RotateRec709ToOutputSpace`
под HDR. Всё это принадлежит экрану, а не сцене, и потому стоит после
URP, а не внутри композита.

#### 6c. Приборы и рабочее место

- `Scopes/` — гистограмма, waveform, вектороскоп. Считаются на GPU в
  маленькие RT, обратного чтения на CPU нет. Источник прореживается до
  ~65k выборок: прибор от полного разбора 4K не точнее, а кадр дороже.
- Проход приборов ставится в очередь **только** при `ScopesRenderPass.Enabled`.
- `Workbench/` — рабочее место колориста на UI Toolkit, клавиша `F5`:
  окна слоёв, приборов и зон. Инструмент автора, не игрока.
- `Zones/` — грейдинг по зонам мира (`ColorGradeZoneDriver`).
- `ColorGradeFile.cs` — грейд как JSON в `persistentDataPath`, мимо
  `ClientConfig`.

### 7. UI Toolkit

- **Файлы:** `UI/HUD/Player/View/PlayerHUDView.cs`, `UI/Chat/GlobalChatUI.cs`,
  `Player/Controllers/PlayerInteractionController.cs`
- 100% UI Toolkit: `EventSystem` и uGUI в коде отсутствуют полностью.
- Координаты мыши конвертируются только через
  `RuntimePanelUtils.ScreenToPanel` — из-за `ScaleWithScreenSize`.

---

## 3. Главные инварианты

1. **Не добавлять искусственное затухание в Radiance Cascades.** Интеграл
   сохранения энергии строгий; `distanceFalloff` убивает свет.
2. **Не считать `hasFarCascade` от общего числа каскадов.** Только от
   числа продиспатченных в текущей половине — иначе двойной учёт.
3. **Не заливать весь буфер вершин на точечную правку меша.** Диапазон
   уже посчитан, им и надо пользоваться.
4. **Не засевать при инкрементальной заливке фона только новую полосу.**
   Нужна и соседняя разрешённая линия, иначе фон вымывается.
5. **Не раздувать отступ освещения.** Он входит в цену квадратом.
6. **`renderPostProcessing` — решение уровня камеры, а не проекта.**
   `HDROutput.ConfigureCamera` включает его для камеры дисплея намеренно:
   URP владеет выводом, primaries и композицией UI. Для камеры мирового
   UI (`UnityRenderLayerContracts`) он выключен. Одного глобального
   запрета больше нет.
7. **Клики — только через `RuntimePanelUtils.ScreenToPanel`.**
8. **Индексы оверлея дверей переписывать всегда.** Равная длина буфера не
   значит равный состав: одна дверь на месте другой даёт то же число
   вершин и другие треугольники.
