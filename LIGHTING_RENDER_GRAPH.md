# Fodinae lighting render graph

Этот документ описывает фактический runtime-граф освещения клиента на текущем
состоянии исходников. Это не план и не желаемая архитектура. Если поведение
расходится с этим документом, сначала нужно обновить описание или исправить
реализацию — нельзя объяснять расхождение «кэшем Unity».

## 1. Точка входа

`TerrariaLightingEngine.UpdateLighting(...)` вызывается из
`TerrainRenderer.LateUpdate()` для подготовки и немедленного исполнения
command buffer до начала рендера камеры. Lighting не исполняется из unsafe
RenderGraph pass: такой pass менял render target и matrices и мог протекать в
следующий terrain draw.

На входе:

- видимый прямоугольник мира в серверных координатах Top-Left;
- `MapStorage.IsReady` и состояние `MapManager`;
- ревизия геометрии terrain и дополнительных lighting contributors;
- список динамических источников от роботов и других объектов;
- runtime-конфигурация lighting;
- активный debug view.

На выходе подготовительной стадии:

- pending Unity `CommandBuffer` с нужными GPU-операциями;
- dirty-состояние и метаданные текущего solve.

После исполнения URP pass на выходе рендера:

- `_WorldLightTexture` — итоговое поле света;
- `_WorldLightRect` — мировая область, соответствующая этой текстуре;
- `_WorldLightTextureSize` и debug/emission globals.

Terrain и world shaders читают только опубликованные globals. Они не запускают
lighting graph самостоятельно.

## 2. Dirty-состояние

Граф запускается, если истинно хотя бы одно условие:

```text
_fieldDirty
regionChanged
terrainGeometryRevisionChanged
lightingContributorGeometryRevisionChanged
_externalLightsDirty
_ambientOcclusionDirty
_compositeDirty
_bounceDirty
```

Основные флаги:

| Флаг | Что означает | Какие стадии затрагивает |
|---|---|---|
| `_fieldDirty` | изменились размеры/регион/материал world field | material, static emission, normals, AO, cascades, direct, bounce, composite |
| `_externalLightsDirty` | изменились динамические источники | dynamic emission, cascades, direct, bounce, composite |
| `_ambientOcclusionDirty` | изменилась геометрия/настройки AO | contact AO, composite; при геометрии также material и cascades |
| `_bounceDirty` | изменился bounce или его параметр | diffuse bounce, composite |
| `_compositeDirty` | изменились только параметры финального вывода | composite |

После успешного прохода `RememberDynamicLightState()` сбрасывает только
`_externalLightsDirty` и помечает состояние источников отрисованным.

## 3. Формирование области и ресурсов

1. Проверяется готовность карты, камеры, storage и terrain renderer.
2. Из видимой области вычисляется стабильный lighting region с anchor и
   квантованием размера.
3. При изменении размера или региона вызывается `EnsureResources()`.
4. Создаются или переиспользуются:

   - `_materialField` — albedo в RGB и occupancy в A;
   - `_staticEmissionField` — emission от статической геометрии;
   - `_emissionField` — static emission плюс dynamic lights;
   - `_automaticNormalField`;
   - `_ambientOcclusionTexture`;
   - `_radianceAtlas` — cascades;
   - `_directTexture`;
   - `_bounceTexture`;
   - `_lightmapTexture` — итоговый результат.

Размеры поля соответствуют lighting region и `EffectivePixelsPerCell`, а не
размеру одного terrain tile.

## 4. Источники полей

### 4.1 Material field

`TerrainRenderer.RenderLightingMaterialFields(...)` растеризует фактический
terrain mesh в `_materialField` и `_staticEmissionField`.

`LightingGeometryRegistry` может дорисовать дополнительные геометрические
contributors в те же поля без очистки уже отрисованного terrain.

Смысл каналов:

```text
MaterialField.rgb = surface albedo
MaterialField.a   = physical occupancy
EmissionField     = emitted radiance
```

Альфа визуальной текстуры, blending, анимация тайла и декоративный внешний вид
не должны менять физическую occupancy.

### 4.2 Automatic normals

`SolveAutomaticNormals` читает occupancy из `_MaterialField` и записывает
`_AutomaticNormalField`. Нормали используются для directional Lambert response
в lighting reconstruction. Этот этап запускается при перестроении material
field, а не при каждом движении dynamic light.

### 4.3 Static и dynamic emission

Статическая emission создаётся во время растеризации terrain/contributors.

При наличии dynamic lights:

1. `_staticEmissionField` копируется в `_emissionField` через `CopyTexture`;
2. `DynamicEmission.shader` рисует procedural triangles — по 6 вершин на
   источник;
3. вызывается `GenerateMips(_emissionField)`;
4. cascades читают `_emissionField` через mip levels.

Позиции источников переводятся из клеточных координат в мировые пиксели.
Источники вне текущего lighting region не загружаются в dynamic buffer.

## 5. Contact AO

Если включён AO и изменилась геометрия/область/настройки, запускается
`SolveContactOcclusion`.

Он пишет отдельное полноразрешённое `_ambientOcclusionTexture`. AO не является
поглощением света и не должен менять direct radiance или emission. В текущем
графе AO передаётся в composite как отдельный contribution.

## 6. Radiance Cascades

`DispatchRadianceCascades()` проходит cascades от дальнейшей к ближней:

```text
for cascade = last down to 0:
    DispatchRadianceCascade(cascade)
```

Каждая cascade:

1. выбирает probe layout и interval;
2. запускает `SolveCascade`;
3. трассирует направления лучей;
4. семплирует occupancy и emission;
5. накапливает radiance и длину пути;
6. записывает packed radiance/path в `_radianceAtlas`.

Дальняя cascade является входом для предыдущей cascade через atlas offsets и
far-cascade параметры. Поэтому порядок нельзя менять на ближняя → дальняя.

`SolveCascade` выполняется thread group size `64x1x1`. Количество dispatch groups
зависит от `CascadeEntryCount`, а не просто от размера экрана.

## 7. Direct, diffuse bounce и composite

После cascade solve запускается `DispatchResolveAndBounce()`.

### Direct

`ResolveDirect` читает `_radianceAtlas` и пишет `_directTexture`.

### Diffuse bounce

Если включён bounce и `BounceStrength > 0`:

```text
_directTexture -> SolveDiffuseBounce -> _bounceTexture
```

Bounce имеет собственный размер `_bounceWidth x _bounceHeight` и потому может
быть дешевле полноразмерного direct field.

### Composite

`CompositeLighting` читает:

- `_directTexture`;
- `_bounceTexture`;
- `_ContactOcclusionTexture`;
- `_MaterialField`/emission и runtime-параметры.

Он пишет `_lightmapTexture`. Именно этот texture публикуется как
`_WorldLightTexture`.

Полный базовый граф:

```text
Terrain mesh + contributors
          │
          ├──> MaterialField (albedo, occupancy)
          ├──> StaticEmissionField
          │
          └──> AutomaticNormals

StaticEmissionField + DynamicEmission.shader
          │
          └──> EmissionField

MaterialField + EmissionField
          │
          └──> Radiance Cascades (far -> near)
                         │
                         └──> RadianceAtlas

RadianceAtlas ──> ResolveDirect ──> DirectTexture
                                      │
                                      └──> DiffuseBounce ──> BounceTexture

MaterialField + DirectTexture + BounceTexture + ContactAO
          │
          └──> CompositeLighting ──> LightmapTexture
                                             │
                                             └──> _WorldLightTexture
```

## 8. Dynamic-light ветка и текущая причина скачков FPS

Движение робота вызывает `SetDynamicLight()`. При изменении позиции, цвета или
интенсивности:

```text
_externalLights[id] = source
_externalLightsDirty = true
_externalLightsRevision++
```

Для dynamic-only изменений включается `phasedDynamicSolve`. Он не выполняет
все cascades в одном command buffer. Вместо этого он выполняет одну cascade за
вызов `UpdateLighting`:

```text
кадр N:   PrepareEmission + cascade K
кадр N+1: cascade K-1
кадр N+2: cascade K-2
...
последний: ResolveDirect + Bounce + Composite
```

Это не является бесплатной оптимизацией. Каждая cascade всё равно запускает
GPU dispatch, а итоговый lightmap появляется только после последней cascade.
Если dynamic sources меняются чаще, чем завершается такой solve, граф может
постоянно проводить дорогие промежуточные dispatches. Это согласуется с
профилем «200 FPS между пересчётами и около 20 FPS на фазах», но само по себе
не доказывает, что именно эта ветка является единственной причиной скачков.

Важно: изменение порога позиции источника лишь уменьшает число запусков. Оно
не исправляет стоимость одного solve и не должно использоваться как замена
архитектурному исправлению.

## 9. Throttling

Для статического и dynamic-only графа используются отдельные времена:

```text
_nextLightingUpdateTime
_nextDynamicLightingUpdateTime
```

Обычный solve блокируется до следующего разрешённого времени, если нет
geometry/AO/composite/bounce обязательной причины. Уже начатый phased dynamic
solve имеет отдельный путь `continueDynamicSolve` и продолжает работу между
кадрами.

Следствие: throttling ограничивает старт нового solve, но не стоимость уже
идущего phased solve.

## 10. Публикация и потребители

После выполнения command buffer в `LightingRenderPass`:

```text
Graphics.ExecuteCommandBuffer(commandBuffer)
PublishLightingGlobals()
```

`PublishLightingGlobals()` задаёт параметры до camera render, а
`LightingRenderPass` гарантирует, что pending command buffer исполняется до
terrain sprites. `PublishLightingGlobals()` задаёт:

- `_WorldLightTexture`;
- `_WorldLightRect`;
- `_WorldLightTextureSize`;
- `_WorldLightDebugView`;
- `_WorldEmissionScale`.

Terrain/world surface shaders используют `_WorldLightTexture` по мировому
положению. UI Toolkit и world-space UI не должны попадать под эту текстуру или
под post-processing world camera.

## 11. Инварианты для дальнейшего профилирования

Нельзя считать граф исправным, если:

1. dynamic light запускает полный cascade graph на каждом render frame;
2. static material field пересобирается из-за движения источника;
3. AO пересчитывается из-за изменения интенсивности света;
4. direct/bounce пересчитываются из-за изменения только UI/debug state без
   соответствующего dirty flag;
5. `CompositeLighting` вызывается до готовности direct/bounce inputs;
6. `_WorldLightRect` не соответствует координатам terrain mesh;
7. dynamic light вне region попадает в buffer или меняет его revision;
8. промежуточный phased atlas публикуется как готовый итоговый lightmap;
9. один источник вызывает несколько одинаковых `SetDynamicLight` за кадр;
10. граф компенсируется скрытым fallback, снижением разрешения или
    квантованием позиции без явного решения и настройки.

## 12. Гипотеза и требования к доказательству

Наблюдаемый факт: текущая система — это единый радиансный граф с переиспользованием static
material/emission fields, но dynamic lights всё ещё входят в тот же cascade
solve. Поэтому динамический источник не является дешёвым additive overlay:
изменение его позиции может потребовать повторного решения cascades, direct,
bounce и composite.

Это важный кандидат на причину, но не доказанная причина скачков FPS. До
изменения архитектуры нужно одновременно измерить CPU, render thread и GPU:
длительность каждого lighting dispatch, число dispatches за кадр, размер поля,
число источников, `GenerateMips`, `Graphics.ExecuteCommandBuffer`, post-process,
GC/allocations и ожидание VSync/GPU.

Только если трассировка покажет корреляцию spike с этой веткой, следующий
архитектурный шаг — отделить
динамическое radiance contribution от статического atlas и композить их в
одном финальном проходе. Пока этого нет, нельзя честно обещать одновременно
полное попиксельное обновление dynamic lights каждый кадр и стабильный FPS на
текущей стоимости cascade graph.

## 13. Нативные Unity markers

Для доказательной профилировки в `TerrariaLightingEngine` добавлены только
штатные Unity-инструменты:

### CPU Profiler markers

```text
Fodinae.Lighting.UpdateLighting.CPU
Fodinae.Lighting.DynamicLights.Upload.CPU
Fodinae.Lighting.Emission.Record.CPU
Fodinae.Lighting.Cascades.Record.CPU
Fodinae.Lighting.Resolve.Record.CPU
Fodinae.Lighting.Composite.Record.CPU
```

Это время подготовки command buffer на main thread, а не время выполнения GPU.

### GPU command-buffer samples

```text
Fodinae.RadianceCascades
Fodinae.Lighting.MaterialField
Fodinae.Lighting.DynamicEmission
Fodinae.Lighting.ContactOcclusion
Fodinae.Lighting.RadianceCascades
Fodinae.Lighting.RadianceCascade
Fodinae.Lighting.ResolveAndBounce
Fodinae.Lighting.Composite
```

Эти samples записаны через `CommandBuffer.BeginSample/EndSample` и должны
сопоставляться с GPU timeline Unity Profiler/Frame Debugger. Самодельные
`Time.realtimeSinceStartup`, логирование каждого кадра и ручные усреднения для
диагноза не используются.
