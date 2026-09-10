# PIPELINE

`F` — размер поля освещения = клетки региона × scale (scale: 1 в PerBlock, 2 в High, 4 в Ultra).
Регион ≈ 128×96 клеток. `S` — разрешение экрана.

## Освещение

```
меш террейна ──[раст. MRT, орто]──┬──► MaterialField  (F, RGBA32, +mips)
                                  └──► StaticEmission (F, ARGBHalf)

источники ──[DynamicEmissionComposition]──► DynamicEmission (F, ARGBHalf)

                     ┌── MaterialField
StaticEmission ──────┴──[SolveCascade]──► RadianceAtlas (uint3 × N)
                                              │
                                              ▼
                                       [ResolveDirect]
                                              │
                                              ▼
                                    StaticDirect (F, ARGBHalf)

                     ┌── MaterialField
DynamicEmission ─────┴──[SolveCascade]──► RadianceAtlas   ← тот же буфер,
                                              │             перезаписан
                                              ▼
                                       [ResolveDirect]
                                              │
                                              ▼
                                       Direct (F, ARGBHalf)

Direct ─────────┐
StaticDirect ───┼──[SolveDiffuseBounce]──► Bounce (F/2, ARGBHalf)
MaterialField ──┘

Direct ─────────┐
StaticDirect ───┼──[CompositeLighting]──► Lightmap (F, ARGBHalf)
Bounce ─────────┘                              │
                                               ▼
                                    global _WorldLightTexture
```

## Террейн (фрагмент, пасс Universal2D)

```
_WorldLightTexture ──[если DebugView≠0]──► выход, шаги ниже не выполняются

BaseMap (атлас) ──┐
subAtlasRect ─────┼──[выборка тайла]──► texColor
tileSizeUV ───────┤
animData ─────────┘
                       │
                       ▼
                  [анимация цвета]──► finalRGB ─────────────┐
                                                            │
маска соседства ──[силуэт: круг + углы]──► finalAlpha       │
                                                            │
MaterialField.mips ──[AO вокруг блоков, только фон]──► AO    │
                                          │                 │
_WorldLightTexture ──► lightColor ────────┤                 │
                                          ▼                 ▼
                    finalRGB × lightColor × (1-AO) ÷ finalAlpha
                                          │
                                          ▼
                                     цвет пикселя
```

## Экран

```
кадр ──[сигмоида Fodinae]──[блум]──[виньетка]──[аберрация]──[зерно]──► экран
```

## Таблица стадий

| #  | Стадия              | Читает                          | Пишет           | Размер | Когда           |
|----|---------------------|---------------------------------|-----------------|--------|-----------------|
| 1  | Поле материалов     | меш террейна + анимация цвета   | Material + Emis | F      | геометрия/регион|
| 2  | Мипы поля           | Material                        | Material.mips   | F      | геометрия/регион|
| 3  | Динам. эмиссия      | источники + анимир. клетки      | DynEmission     | F      | каждый кадр     |
| 4  | Каскады (стат.)     | Material, StaticEmission        | RadianceAtlas   | N зап. | мир изменился   |
| 5  | Resolve (стат.)     | RadianceAtlas                   | StaticDirect    | F      | мир изменился   |
| 6  | Каскады (динам.)    | Material, DynEmission           | RadianceAtlas   | N зап. | каждый кадр*    |
| 7  | Resolve (динам.)    | RadianceAtlas                   | Direct          | F      | каждый кадр*    |
| 8  | Диффузный отскок    | Direct, StaticDirect, Material  | Bounce          | F/2    | каждый кадр     |
| 9  | Сведение            | Direct, StaticDirect, Bounce    | Lightmap        | F      | каждый кадр     |
| 10 | Выборка тайла       | BaseMap, атрибуты вершины       | texColor        | S      | каждый пиксель  |
| 11 | Анимация цвета      | texColor, animData              | finalRGB        | S      | каждый пиксель  |
| 12 | Силуэт              | маска соседства                 | finalAlpha      | S      | каждый пиксель  |
| 13 | AO вокруг блоков    | MaterialField.mips (только фон) | occlusion       | S      | каждый пиксель  |
| 14 | Освещение           | Lightmap, finalRGB, occlusion   | цвет пикселя    | S      | каждый пиксель  |
| 15 | Постобработка       | кадр                            | экран           | S      | каждый кадр     |

\* Нет динамических источников → 6-7 заменяются обнулением `Direct`.

## Ветвления

| Условие                       | Эффект                                                  |
|-------------------------------|---------------------------------------------------------|
| `_WorldLightDebugView != 0`   | Стадии 10-14 не выполняются, выводится тексель Lightmap |
| `_BlockAveraged != 0`         | В 5 и 7 выборка атласа привязана к центру клетки        |
| `!rebuildFields`              | Стадии 1-2 пропущены, используются прошлые текстуры     |
| Лимит текстуры превышен       | `scale` понижается, вплоть до 1                         |

## Отсутствует

| Что                                | Статус                                     |
|------------------------------------|--------------------------------------------|
| Нормали поверхности                | Снесены полностью: объём даёт AO вокруг блоков |
| Эмиссия в стадии 9                 | Читается только отладочными видами         |
| Детали мельче клетки в освещении   | Невозможны: F ≤ 4 текселя на клетку, тайл 32 px |
