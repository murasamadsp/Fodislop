# Fodinae render graph inventory

Это инвентаризация фактического клиентского кадра по исходникам. Документ не
утверждает производительность: стоимость каждого участка должна подтверждаться
Unity Profiler/Frame Debugger на целевой сцене.

## Кадровый порядок

```text
Unity frame
├─ Update
│  ├─ game/network state
│  ├─ robots and dynamic contributors
│  └─ UI/controllers
├─ LateUpdate
│  ├─ TerrainRenderer: streaming/cache/mesh upload
│  ├─ SurfaceRenderer: visible boundary meshes
│  └─ PostProcessController: runtime volume/config sync
├─ URP 2D renderer / RenderGraph
│  ├─ lighting command buffer is prepared/executed by TerrainRenderer.LateUpdate
│  │  before camera rendering
│  ├─ 2D sprite/terrain rendering
│  ├─ world-space UI camera/render layers
│  └─ Fodinae.PostProcess
│     ├─ optional robot velocity target + DrawRenderer per remote robot
│     ├─ bloom prefilter
│     ├─ bloom downsample chain
│     ├─ bloom upsample chain
│     ├─ final post-process composite
│     └─ blit intermediate back to camera color
└─ presentation
```

## Terrain

`TerrainRenderer.LateUpdate` проверяет готовность мира, рассчитывает viewport,
перестраивает `TerrainCellCache` только при необходимости и обновляет один
меш. `UpdateVertexAttributes` выполняет cache/flood-fill/precalculation и
заполняет submesh indices. Сам terrain рисуется одним `MeshRenderer` с
несколькими atlas submeshes и `Universal2D` pass.

После mesh update lighting material field подготавливается в LateUpdate до
camera rendering:

```text
Terrain mesh
  └─ LightingMaterialField pass
     ├─ _materialField.rgb = albedo
     ├─ _materialField.a   = physical occupancy
     └─ _staticEmissionField = static emission
```

Mipmap generation для `_materialField` выполняется ровно один раз после
terrain/contributor geometry. Если contributors отсутствуют, generation делает
сам lighting engine; если присутствуют — registry делает его после дорисовки.

## Lighting

`TerrariaLightingEngine.UpdateLighting` только записывает GPU-команды в
pending buffer. Алгоритм сохраняет обязательный порядок:

```text
material/emission fields
  → automatic normals
  → contact AO
  → radiance cascades (far → near)
  → direct resolve
  → optional diffuse bounce
  → composite
  → _WorldLightTexture globals
```

Динамический свет загружается в GPU buffer и рисуется в emission field через
procedural draw. Изменение источника не должно пересобирать material field или
AO.

Ограничение текущей реализации: команды записываются в отдельный Unity
`CommandBuffer` и исполняются напрямую из `TerrainRenderer.LateUpdate()` через
`Graphics.ExecuteCommandBuffer`. Это сохраняет корректное состояние render
target/matrices для последующего URP terrain draw, но не даёт RenderGraph
отдельных ресурсных зависимостей lighting. Перенос в текущий RenderGraph
command buffer требует атомарного рефакторинга записи всех lighting-команд.

## Surface/boundary

`SurfaceRenderer` регистрирует red-rock/transit/perspective geometry как
lighting contributors и в `LateUpdate` перестраивает видимые meshes при смене
камеры/мира. В lighting field эти meshes рисуются отдельно, после terrain
geometry, без очистки поля.

## Post-processing

`PostProcessRenderPass` использует один unsafe RenderGraph pass и реальные
texture handles. При включённом bloom стоимость состоит из одного полноразмерного
prefilter, цепочки уменьшений, цепочки увеличений и полноразмерного composite.
Motion blur создаёт velocity target только при наличии удалённых robot
renderers; локальный игрок не должен участвовать.

## Подтверждённые точки контроля

- `Fodinae.Terrain.LateUpdate.CPU`
- `Fodinae.Lighting.UpdateLighting.CPU`
- `Fodinae.Lighting.DynamicLights.Upload.CPU`
- `Fodinae.Lighting.MaterialField`
- `Fodinae.Lighting.ContactOcclusion`
- `Fodinae.Lighting.RadianceCascades`
- `Fodinae.Lighting.ResolveAndBounce`
- `Fodinae.Lighting.Composite`
- `Fodinae.Lighting.Render.Submit.CPU` не добавлен: lighting command buffer
  исполняется напрямую до URP graph.
- `Fodinae.PostProcess`

Сначала сравниваются CPU/GPU времена этих участков и количество dispatch/draw
calls. Любая оптимизация без этого измерения считается неподтверждённой.
