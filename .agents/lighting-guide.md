# Lighting guide

Читать перед редактированием любого lighting кода (shaders или C#) — шейдеров освещения, compute-пассов, DDA, каскадов, bounce, блоков.

## Перед изменением

1. Прочитай [LIGHTING_ARCHITECTURE.md](../docs/architecture/LIGHTING_ARCHITECTURE.md) — там dataflow и контракты стадий.
2. Определи, к какой стадии относится изменение:

| Файл | Стадия |
|------|--------|
| `LightingTypes.hlsl`, `Extinction.hlsl`, `GeometryField.hlsl`, `DDA.hlsl` | Общие примитивы |
| `GeometryCache/GeometryCache.hlsl` | `BuildCellSolidMask` |
| `Cascades/CascadeTrace.hlsl` | `SolveCascade` (DDA traversal) |
| `Cascades/CascadeResolve.hlsl` | `ResolveDirect` (atlas lookup) |
| `Dynamic/DynamicPolar.hlsl` | `TraceDynamicPolar`, `DynamicRadianceFromPolar` (DDA) |
| `Dynamic/DynamicLightTrace.hlsl` | `SolveDynamicLighting`, `ComposeDynamicLighting` |
| `Bounce/BounceCache.hlsl` | `BuildBounceTaps`, `BuildBounceFilter` (DDA) |
| `Bounce/BounceSolve.hlsl` | `SolveDiffuseBounce` |
| `Composite/CompositeLighting.hlsl` | `CompositeLighting` |
| `Block/BlockLighting.hlsl` | PerBlock tier |

## Запреты

**ЗАПРЕЩЕНО** добавлять вызовы DDA (`TraceLightSegment`, `TraceRadianceSegment`) в:
- `CascadeResolve`
- `DynamicLightTrace`
- `BounceSolve`
- `CompositeLighting`
- `BlockLighting`

## Метрики и проверка

- Любое изменение transport-стадий (CascadeTrace, DynamicPolar, BounceCache) должно увеличивать `LightingDdaSegments` или `LightingDdaTexelVisits` в `IFrameTelemetry`.
- После изменения transport math проверь:
  - Все debug views (0–9) показывают ожидаемую картину.
  - `LightingDdaSegments` / `LightingDdaTexelVisits` не выросли неожиданно.
  - FPS не упал относительно baseline.

## Архитектурные ограничения

- Не добавляй «quality step budget» или frame skipping в DDA — стены должны быть точными.
- Static/dynamic split сохраняется: каскады кэшируются до изменения terrain/emission, динамический свет решается per-frame.

## Научные материалы

Академические статьи по GI и Radiance Cascades — в [`docs/lighting-research/`](../docs/lighting-research/README.md).
