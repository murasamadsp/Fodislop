# Полный технический аудит графического пайплайна Fodinae (Diff & Regression Analysis)

## 1. Резюме (Executive Summary)

В ходе недавних изменений предыдущего агента графический пайплайн подвергся массовым агрессивным модификациям под предлогом «оптимизации». Эти изменения привели к:
1. **Падению FPS в ~5 раз** из-за искусственного раздувания меша террейна с ~49 000 до 245 760 вершин на каждый кадр/сдвиг.
2. **Деградации визуального качества в ~10 раз** из-за:
   - Сломанных автонормалей на Metal (инверсия Y в `CalculateAutomaticSurfaceNormal`);
   - Отключения всего пост-процессинга Volume URP (`cameraData.renderPostProcessing = false`);
   - Искусственного двойного затухания света `distanceFalloff` внутри лучевого марша каскадов;
   - Сжатия диапазона HDR через `RollOffHighlights` с жестким коленом на 60%;
   - Урезания сэмплов контактной окклюзии с 3 до 2;
   - Записи `m_MSAA: 1` (Off) напрямую в ассет `UniversalRP.asset` на диске.

Ниже приведён исчерпывающий технический разбор каждого компонента и каждого файла от А до Я.

---

## 2. Архитектура и изменения по подсистемам

### А. Подсистема террейна и сетки (Terrain & Mesh Rebuild)

#### 1. Раздувание размера сетки и вершинный оверхед
- **Файл:** `Assets/Scripts/World/Terrain/TerrainRenderer.cs`
- **Что было сделано:**
  ```csharp
  // Старый расчет:
  int requiredLightingPadding = lightingEngine.RequiredTerrainPadding +
      TerrainRegionAnchorCells + lightingEngine.StableRegionPaddingCells; // ~16-24
  int effectiveViewportPadding = _viewportPadding; // 8

  // Измененный расчет:
  int requiredLightingPadding = lightingEngine.RequiredTerrainPadding +
      lightingEngine.LightReachCells +
      TerrainRegionAnchorCells; // 16 + 24 + 8 = 48 клеток!
  int effectiveViewportPadding = Mathf.Max(_viewportPadding, requiredLightingPadding); // 48
  ```
- **Последствия для производительности:**
  - Паддинг меша террейна увеличился с 8–16 до **48 клеток во все стороны**.
  - Рабочий размер сетки вырос с `96×64` (6 144 клетки) до **`192×160` (30 720 клеток)** — рост в **5 раз**.
  - Количество вершин в `VertexBuffer` выросло с `49 152` до **`245 760` вершин**.
  - CPU вынужден на каждый сдвиг аллоцировать, интерполировать и заливать через `Mesh.SetVertexBufferData` четверть миллиона вершин.
  - Пошаговый волновой алгоритм `BackgroundFloodFill` стал обходить 30 720 клеток на главном потоке вместо 6 000.

#### 2. Добавление ключевых слов и принудительное отключение слоев
- **Файлы:** `TerrainMeshBuilder.cs`, `TerrainPrecalculator.cs`, `Terrain.shader`
- **Что было сделано:**
  - В `Terrain.shader` шейдерная анимация была зашита под `#pragma multi_compile _ _FODINAE_TERRAIN_ANIMATION`.
  - В `TerrainPrecalculator.cs` добавлен гейт `ReliefEnabled`, который при выключении обнулял `GridVertexOffsets` и `GridShadowValues`.
  - В `TerrainMeshBuilder.cs` добавлен переключатель `VertexStride` (4 vs 8 вершин на клетку) и отключение фонового слоя `_bgAtlasIndices = -1`.

---

### Б. Освещение Radiance Cascades (`WorldLighting.compute` & `TerrariaLightingEngine.cs`)

#### 1. Полная поломка автоматических нормалей (Automatic Normals) на Metal (macOS)
- **Файл:** `Assets/Resources/Shaders/Lighting/WorldLighting.compute`
- **Код:**
  ```hlsl
  float4 CalculateAutomaticSurfaceNormal(float2 pixelPosition)
  {
      float occupancyLeft = SampleOccupancy(pixelPosition - float2(1.0, 0.0), 0.0);
      float occupancyRight = SampleOccupancy(pixelPosition + float2(1.0, 0.0), 0.0);
      float occupancyDown = SampleOccupancy(pixelPosition - float2(0.0, 1.0), 0.0);
      float occupancyUp = SampleOccupancy(pixelPosition + float2(0.0, 1.0), 0.0);
      float2 occupancyGradient = float2(
          occupancyRight - occupancyLeft,
          occupancyUp - occupancyDown);
      ...
  }
  ```
- **В чем баг:**
  - На macOS Metal включен флаг `_MaterialYFlip` (координаты UV текстур перевернуты: `uv.y = 1.0 - uv.y`).
  - `SampleOccupancy` рассчитывает UV через `MaterialUv(pixelPosition)`.
  - Смещение вверх `+ float2(0.0, 1.0)` при `_MaterialYFlip != 0` сдвигает сэмпл текстуры **вниз**, а не вверх!
  - `occupancyGradient.y` инвертировался по знаку.
  - Все векторные нормали поверхностей оказались **перевернуты вверх ногами**.
  - Формула диффузного отражения `SurfaceLambert` давала `0` на верхних гранях под падающим сверху светом (вместо освещенности верхние грани получали глубокую черноту).

#### 2. Искусственное затухание лучей (`distanceFalloff`)
- **Файл:** `Assets/Resources/Shaders/Lighting/WorldLighting.compute` (`SolveCascade`)
- **Код:**
  ```hlsl
  float totalDistanceCells = PathLengthInCells(rayDirection, travel);
  float distanceFalloff = 1.0 / (1.0 + 0.08 * totalDistanceCells + 0.003 * totalDistanceCells * totalDistanceCells);
  float3 emittedRadiance = max(emission.rgb, 0.0) * physicalStepLength * _EmissionScale * distanceFalloff;
  ```
- **В чем баг:**
  - В уравнении переноса излучения (Radiative Transfer) яркость луча (radiance $L$) в вакууме/прозрачной среде **сохраняется вдоль луча** (закон сохранения этендю). Спад $1/r^2$ в Radiance Cascades возникает естественным путем за счет углового покрытия при объединении каскадов.
  - Умножение на `distanceFalloff` внутри лучевого марша привело к двойному квадратичному затуханию. Все факелы, светящиеся блоки и источники света гасли в черную пустоту уже через 2–3 блока.

#### 3. Сжатие диапазона яркости `RollOffHighlights`
- **Файл:** `Assets/Resources/Shaders/Lighting/WorldLighting.compute` (`RollOffHighlights`)
- **Код:**
  ```hlsl
  float knee = white * 0.6;
  if (peak <= knee) return radiance;
  float rolled = white - ((white - knee) * (white - knee) / (peak + white - 2.0 * knee));
  ```
- **В чем баг:**
  - Вместо корректной цветовой нормализации или линейного клампа весь диапазон яркости выше 60% агрессивно сплющивался в пологую кривую. Свет потерял динамический контраст, став тусклым и грязным.

#### 4. Деградация контактной окклюзии (Contact Occlusion)
- В ядре `SolveContactOcclusion` число сэмплов лучевого затенения углов было урезано с 3 до 2 (`sampleDistances = float2(0.45, 1.0)` вместо `float3(0.33, 0.66, 1.0)`), что вызвало ступенчатый шум на гранях.

---

### В. Пост-процессинг и URP Volume Pipeline

#### 1. Полное отключение встроенного URP Post-Processing
- **Файл:** `Assets/Scripts/Rendering/PostProcessing/PostProcessController.cs`
- **Код:**
  ```csharp
  cameraData.renderPostProcessing = false;
  ```
- **Последствия:**
  - На главной игровой камере URP полностью выключил выполнение Volume-стека.
  - Все эффекты из `DefaultVolumeProfile.asset` перестали рендериться:
    - **ACEX / Neutral Tonemapping**
    - **ColorCurves & LiftGammaGain**
    - **ShadowsMidtonesHighlights**
    - **FilmGrain**
    - **WhiteBalance & SplitToning**
    - **DepthOfField & LensDistortion**

#### 2. Ранний выход кастомного `PostProcessRenderPass`
- **Файл:** `Assets/Scripts/Rendering/PostProcessing/PostProcessRenderPass.cs`
- **Код:**
  ```csharp
  if (!bloomActive && !vignetteActive && !caActive && !cgActive && !eigengrauActive && !mbActive)
  {
      return;
  }
  ```
- **Последствия:**
  - Так как в `ProjectDefaults.asset` и `client_config.json` дефолтные интенсивности эффектов были выставлены в `0.0`, кастомный проход сразу прерывался.
  - В результате игра рендерилась **вообще без пост-процессинга и цветокоррекции**.

---

### Г. Пресеты качества и мутация ассетов проекта

#### 1. Срез лучей и параметров в `GraphicsQualityProfile.asset`
- Предыдущий агент переписал `ConfigureGraphicsQualityProfile.cs`, занизив `LightingMaximumRaySteps` до 4–8 шагов (вместо 40–64).
- `AntiAliasing` был выставлен в 0 во всех пресетах, что принудительно переводило MSAA в 1x (полное отсутствие сглаживания).

#### 2. Мутация `UniversalRP.asset` на диске
- В рантайме метод `ApplyUrpMsaa` записывал `msaaSampleCount` прямо в `UniversalRP.asset`, из-за чего на диске в `UniversalRP.asset` сохранилось `m_MSAA: 1` (выключено), портя сглаживание даже после перезапуска редактора.

---

## 3. Сводная таблица регрессий

| Подсистема | Что было до изменений | Что сделал прошлый агент | Эффект |
|---|---|---|---|
| **Terrain Mesh** | ~49 000 вершин, паддинг 8-16 | 245 760 вершин, паддинг 48 | **Падение FPS в 5 раз**, перегрузка CPU и шины GPU |
| **Auto Normals** | Корректная ориентация | Инверсия Y из-за `_MaterialYFlip` на Metal | **Черные верхние грани блоков**, поломка рельефа |
| **Light Raymarch** | Честное сохранение яркости луча | `distanceFalloff` внутри марша | **Свет гаснет через 2 блока**, темнота вокруг ламп |
| **HDR Highlights** | Линейный отклик / кламп | `RollOffHighlights` с порогом 0.6 | **Блеклый, плоский свет**, потеря контраста |
| **URP Volumes** | Активный пост-процессинг | `cameraData.renderPostProcessing = false` | **Отключение тонемаппинга, кривых и цветокоррекции** |
| **Contact Occlusion** | 3-точечный сэмплинг | 2-точечный сэмплинг | **Шум и разрывы контактных теней** |
| **MSAA Asset** | 4x / 8x в URP ассете | Запись `m_MSAA: 1` в ассет | **Пиксельные лесенки по краям на диске** |

---

## 4. План полного восстановления

1. **TerrainRenderer.cs**:
   - Вернуть `requiredLightingPadding = lightingEngine.RequiredTerrainPadding + TerrainRegionAnchorCells + lightingEngine.StableRegionPaddingCells`.
   - Ограничить размер меша зоной видимости камеры с нормальным паддингом (8–16 клеток).
2. **WorldLighting.compute**:
   - В `CalculateAutomaticSurfaceNormal` учесть `_MaterialYFlip` для вертикального градиента.
   - Убрать искусственный `distanceFalloff` из `SolveCascade`.
   - Вернуть нормальный цветовой кламп без искажения пиков `RollOffHighlights`.
   - Восстановить 3 сэмпла в `SolveContactOcclusion`.
3. **PostProcessController.cs & PostProcessRenderPass.cs**:
   - Включить `cameraData.renderPostProcessing = true` или корректно замкнуть тонемаппинг в кастомный проход.
   - Восстановить стандартные рабочие интенсивности эффектов.
4. **UniversalRP.asset**:
   - Восстановить `m_MSAA: 4` (или 8) в файле настроек пайплайна.
