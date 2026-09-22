# Инвентарь игрового арта

Файл машинный: правки затираются. Пересобрать:

```
dotnet run --project tools/Kern.DesignSystem -- inventory-art
```

Всего PNG: **324** в 20 семействах. Текстовых ассетов прочитано: 1056.

## Сводка по семействам

| семейство | шт | размеры | метрика | занятость | мягкость | оттенок, секторов | насыщ., разброс | без ссылок |
| --- | ---: | --- | ---: | --- | --- | ---: | ---: | ---: |
| `Assets/Resources/Programmator` | 166 | 15x15, 30x30, 13x13 … (+1) | ok | 0.00–1.00 | 0.00–0.06 | 12/12 | 1.00 | 166/166 |
| `Assets/Resources/Skills` | 4 | 73x73 | ok | 1.00–1.00 | 0.81–1.09 | 3/12 | 0.10 | 0/4 |
| `Assets/Resources/UI/Sprites` | 1 | 32x32 | ok | 1.00–1.00 | 0.96–0.96 | 1/12 | 0.00 | 1/1 |
| `Assets/Textures` | 3 | 480x329, 512x32, 480x47 | ok | 0.33–1.00 | 0.00–0.45 | 2/12 | 0.41 | 0/3 |
| `Assets/Textures/Cells` | 65 | 320x320, 32x32, 128x128 … (+1) | ok | 0.82–1.00 | 0.00–0.00 | 10/12 | 0.96 | 65/65 |
| `Assets/Textures/Clan` | 1 | 13x13 | ok | 0.53–0.53 | 0.00–0.00 | 1/12 | 0.00 | 1/1 |
| `Assets/Textures/Crystals` | 6 | 24x24, 25x24 | **1** | 0.69–1.00 | 0.00–0.00 | 6/12 | 0.57 | 1/6 |
| `Assets/Textures/Items` | 51 | 42x42 | ok | 0.22–0.88 | 0.00–1.66 | 12/12 | 0.87 | 6/51 |
| `Assets/Textures/Pack/Clans` | 1 | 114x82 | **1** | 1.00–1.00 | 0.00–0.00 | 1/12 | 0.00 | 1/1 |
| `Assets/Textures/Pack/Craft` | 1 | 50x47 | **1** | 1.00–1.00 | 0.00–0.00 | 1/12 | 0.00 | 1/1 |
| `Assets/Textures/Pack/Gun` | 1 | 94x94 | **1** | 1.00–1.00 | 0.00–0.00 | 1/12 | 0.00 | 1/1 |
| `Assets/Textures/Pack/Market` | 1 | 114x114 | **1** | 1.00–1.00 | 0.00–0.00 | 1/12 | 0.00 | 1/1 |
| `Assets/Textures/Pack/Resp` | 1 | 50x87 | **1** | 1.00–1.00 | 0.00–0.00 | 1/12 | 0.00 | 1/1 |
| `Assets/Textures/Pack/Science` | 1 | 242x114 | **1** | 1.00–1.00 | 0.00–0.00 | 1/12 | 0.00 | 1/1 |
| `Assets/Textures/Pack/Storage` | 1 | 50x18 | **1** | 1.00–1.00 | 0.00–0.00 | 1/12 | 0.00 | 1/1 |
| `Assets/Textures/Pack/Teleport` | 1 | 50x50 | **1** | 1.00–1.00 | 0.00–0.00 | 1/12 | 0.00 | 1/1 |
| `Assets/Textures/Pack/Up` | 1 | 50x79 | **1** | 1.00–1.00 | 0.00–0.00 | 1/12 | 0.00 | 1/1 |
| `Assets/Textures/Skin` | 1 | 32x32 | ok | 0.85–0.85 | 0.23–0.23 | 1/12 | 0.00 | 1/1 |
| `Assets/Textures/Tail` | 1 | 32x32 | ok | 1.00–1.00 | 0.00–0.00 | 1/12 | 0.00 | 0/1 |
| `Assets/Textures/UI` | 16 | 128x128, 259x253, 259x260 … (+1) | ok | 0.47–1.00 | 0.00–1.16 | 4/12 | 0.75 | 1/16 |

Колонки «метрика» и «без ссылок» отвечают числом: жирное число — столько ассетов просит объяснения, `ok` — объяснять нечего.

«Без ссылок» — не приговор. Programmator и Cells грузятся вычисленным путём, поэтому литеральной ссылки у них нет по устройству. Выход за метрику — тоже не всегда дефект: числа нужны арт-библии, а не приговору.

## Разбивка по оттенку

Сектор — 30°. Считаются только непрозрачные пиксели.

| семейство | 0–30° | 30–60° | 60–90° | 90–120° | 120–150° | 150–180° | 180–210° | 210–240° | 240–270° | 270–300° | 300–330° | 330–360° |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `Assets/Resources/Programmator` | 29 | 31 | 6 | 10 | 13 | 29 | 4 | 5 | 12 | 1 | 21 | 1 |
| `Assets/Resources/Skills` | 2 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 1 | 0 |
| `Assets/Resources/UI/Sprites` | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures` | 1 | 2 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Cells` | 15 | 11 | 5 | 0 | 1 | 3 | 9 | 10 | 3 | 4 | 0 | 4 |
| `Assets/Textures/Clan` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Crystals` | 0 | 0 | 0 | 1 | 1 | 1 | 0 | 1 | 0 | 0 | 1 | 1 |
| `Assets/Textures/Items` | 4 | 8 | 13 | 4 | 2 | 4 | 1 | 3 | 3 | 4 | 1 | 4 |
| `Assets/Textures/Pack/Clans` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Pack/Craft` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| `Assets/Textures/Pack/Gun` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Pack/Market` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Pack/Resp` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 |
| `Assets/Textures/Pack/Science` | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Pack/Storage` | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Pack/Teleport` | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 | 0 | 0 |
| `Assets/Textures/Pack/Up` | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Skin` | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/Tail` | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| `Assets/Textures/UI` | 8 | 0 | 0 | 0 | 0 | 1 | 5 | 2 | 0 | 0 | 0 | 0 |

## Все файлы

Занятость — площадь непрозрачного bbox к площади холста. Мягкость — насколько рамка bbox полупрозрачна. Поля — отступы bbox от краёв. Ссылка: `guid`, `имя` или `нет`.

| файл | размер | метрика | занятость | мягкость | поля (л,в,п,н) | оттенок | насыщ. | светлота | ссылка |
| --- | --- | --- | ---: | ---: | --- | ---: | ---: | ---: | --- |
| `Assets/Resources/Programmator/0.png` | 27x7 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 0° | 0.00 | 1.00 | нет |
| `Assets/Resources/Programmator/1.png` | 28x13 | — | 0.82 | 0.00 | 0, 1, 1, 1 | 0° | 0.00 | 1.00 | нет |
| `Assets/Resources/Programmator/10.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/100.png` | 29x28 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 259° | 0.72 | 0.99 | нет |
| `Assets/Resources/Programmator/101.png` | 29x28 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 264° | 0.72 | 1.00 | нет |
| `Assets/Resources/Programmator/102.png` | 29x28 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 259° | 0.72 | 1.00 | нет |
| `Assets/Resources/Programmator/103.png` | 29x28 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 265° | 0.71 | 1.00 | нет |
| `Assets/Resources/Programmator/104.png` | 29x26 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 316° | 0.72 | 0.98 | нет |
| `Assets/Resources/Programmator/105.png` | 29x26 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 322° | 0.73 | 1.00 | нет |
| `Assets/Resources/Programmator/106.png` | 29x26 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 305° | 0.72 | 1.00 | нет |
| `Assets/Resources/Programmator/107.png` | 29x26 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 316° | 0.71 | 1.00 | нет |
| `Assets/Resources/Programmator/108.png` | 26x28 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 314° | 1.00 | 0.99 | нет |
| `Assets/Resources/Programmator/109.png` | 26x28 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 316° | 1.00 | 1.00 | нет |
| `Assets/Resources/Programmator/11.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/110.png` | 26x28 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 309° | 1.00 | 1.00 | нет |
| `Assets/Resources/Programmator/111.png` | 26x28 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 314° | 1.00 | 1.00 | нет |
| `Assets/Resources/Programmator/112.png` | 27x22 | — | 0.91 | 0.00 | 0, 0, 0, 2 | 312° | 0.73 | 1.00 | нет |
| `Assets/Resources/Programmator/113.png` | 27x22 | — | 0.91 | 0.00 | 0, 0, 0, 2 | 312° | 0.73 | 1.00 | нет |
| `Assets/Resources/Programmator/114.png` | 27x22 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 312° | 0.68 | 1.00 | нет |
| `Assets/Resources/Programmator/115.png` | 27x22 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 312° | 0.68 | 1.00 | нет |
| `Assets/Resources/Programmator/116.png` | 28x22 | — | 0.91 | 0.00 | 0, 0, 0, 2 | 312° | 0.70 | 1.00 | нет |
| `Assets/Resources/Programmator/117.png` | 28x23 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 312° | 0.65 | 1.00 | нет |
| `Assets/Resources/Programmator/118.png` | 23x28 | — | 0.00 | 0.00 | 0, 0, 0, 0 | — | 0.00 | 0.00 | нет |
| `Assets/Resources/Programmator/119.png` | 29x30 | — | 1.00 | 0.06 | 0, 0, 0, 0 | 265° | 0.64 | 1.00 | нет |
| `Assets/Resources/Programmator/12.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/120.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 265° | 0.64 | 1.00 | нет |
| `Assets/Resources/Programmator/121.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 265° | 0.62 | 1.00 | нет |
| `Assets/Resources/Programmator/122.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 265° | 0.62 | 1.00 | нет |
| `Assets/Resources/Programmator/123.png` | 30x30 | — | 1.00 | 0.06 | 0, 0, 0, 0 | 265° | 0.62 | 1.00 | нет |
| `Assets/Resources/Programmator/124.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 265° | 0.60 | 1.00 | нет |
| `Assets/Resources/Programmator/125.png` | 25x19 | — | 1.00 | 0.06 | 0, 0, 0, 0 | 312° | 0.43 | 1.00 | нет |
| `Assets/Resources/Programmator/126.png` | 25x19 | — | 1.00 | 0.06 | 0, 0, 0, 0 | 312° | 0.49 | 1.00 | нет |
| `Assets/Resources/Programmator/127.png` | 25x19 | — | 1.00 | 0.06 | 0, 0, 0, 0 | 312° | 0.49 | 1.00 | нет |
| `Assets/Resources/Programmator/128.png` | 29x28 | — | 1.00 | 0.05 | 0, 0, 0, 0 | 265° | 0.66 | 1.00 | нет |
| `Assets/Resources/Programmator/129.png` | 29x28 | — | 1.00 | 0.06 | 0, 0, 0, 0 | 307° | 0.87 | 1.00 | нет |
| `Assets/Resources/Programmator/13.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/130.png` | 29x28 | — | 0.96 | 0.06 | 0, 1, 0, 0 | 312° | 0.69 | 1.00 | нет |
| `Assets/Resources/Programmator/131.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/132.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/133.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/134.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/135.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/136.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/137.png` | 31x10 | — | 1.00 | 0.06 | 0, 0, 0, 0 | 0° | 0.19 | 1.00 | нет |
| `Assets/Resources/Programmator/138.png` | 16x10 | — | 1.00 | 0.02 | 0, 0, 0, 0 | 0° | 0.29 | 1.00 | нет |
| `Assets/Resources/Programmator/139.png` | 27x28 | — | 0.96 | 0.00 | 0, 1, 0, 0 | 0° | 0.00 | 1.00 | нет |
| `Assets/Resources/Programmator/14.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/140.png` | 27x28 | — | 0.96 | 0.00 | 0, 1, 0, 0 | 0° | 0.00 | 1.00 | нет |
| `Assets/Resources/Programmator/141.png` | 19x19 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/142.png` | 19x19 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/143.png` | 19x19 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/144.png` | 16x13 | — | 0.74 | 0.00 | 1, 1, 1, 1 | 232° | 0.50 | 1.00 | нет |
| `Assets/Resources/Programmator/145.png` | 19x19 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 65° | 0.63 | 0.99 | нет |
| `Assets/Resources/Programmator/146.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 181° | 0.44 | 0.83 | нет |
| `Assets/Resources/Programmator/147.png` | 19x19 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 56° | 0.57 | 1.00 | нет |
| `Assets/Resources/Programmator/148.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 0° | 0.55 | 1.00 | нет |
| `Assets/Resources/Programmator/149.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 0° | 0.55 | 1.00 | нет |
| `Assets/Resources/Programmator/15.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/150.png` | 27x28 | — | 0.89 | 0.00 | 0, 3, 0, 0 | 0° | 0.00 | 1.00 | нет |
| `Assets/Resources/Programmator/151.png` | 27x28 | — | 0.89 | 0.00 | 0, 3, 0, 0 | 0° | 0.00 | 1.00 | нет |
| `Assets/Resources/Programmator/152.png` | 27x28 | — | 0.96 | 0.00 | 0, 1, 0, 0 | 0° | 0.21 | 1.00 | нет |
| `Assets/Resources/Programmator/153.png` | 27x28 | — | 0.96 | 0.00 | 0, 1, 0, 0 | 0° | 0.21 | 1.00 | нет |
| `Assets/Resources/Programmator/154.png` | 28x28 | — | 0.96 | 0.00 | 0, 1, 0, 0 | 0° | 0.23 | 1.00 | нет |
| `Assets/Resources/Programmator/155.png` | 27x28 | — | 0.96 | 0.00 | 0, 1, 0, 0 | 0° | 0.23 | 1.00 | нет |
| `Assets/Resources/Programmator/156.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/157.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/158.png` | 15x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 0° | 0.52 | 0.86 | нет |
| `Assets/Resources/Programmator/159.png` | 15x13 | — | 0.92 | 0.02 | 0, 0, 0, 1 | 0° | 0.00 | 0.56 | нет |
| `Assets/Resources/Programmator/16.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/160.png` | 11x9 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 0° | 0.52 | 0.86 | нет |
| `Assets/Resources/Programmator/161.png` | 11x9 | — | 0.89 | 0.02 | 0, 0, 0, 1 | 0° | 0.00 | 0.56 | нет |
| `Assets/Resources/Programmator/162.png` | 20x15 | — | 1.00 | 0.02 | 0, 0, 0, 0 | 68° | 0.78 | 0.88 | нет |
| `Assets/Resources/Programmator/163.png` | 20x15 | — | 1.00 | 0.02 | 0, 0, 0, 0 | 96° | 0.77 | 0.88 | нет |
| `Assets/Resources/Programmator/164.png` | 20x15 | — | 1.00 | 0.02 | 0, 0, 0, 0 | 58° | 0.78 | 0.88 | нет |
| `Assets/Resources/Programmator/165.png` | 13x13 | — | 1.00 | 0.06 | 0, 0, 0, 0 | 102° | 0.74 | 0.91 | нет |
| `Assets/Resources/Programmator/17.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/18.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/19.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/2.png` | 11x11 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 0° | 0.54 | 1.00 | нет |
| `Assets/Resources/Programmator/20.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/21.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/22.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/23.png` | 13x13 | — | 0.72 | 0.00 | 1, 1, 1, 1 | 24° | 1.00 | 1.00 | нет |
| `Assets/Resources/Programmator/24.png` | 30x10 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 0° | 0.00 | 1.00 | нет |
| `Assets/Resources/Programmator/25.png` | 31x10 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 0° | 0.00 | 1.00 | нет |
| `Assets/Resources/Programmator/26.png` | 31x10 | — | 1.00 | 0.06 | 0, 0, 0, 0 | 239° | 0.16 | 1.00 | нет |
| `Assets/Resources/Programmator/27.png` | 17x10 | — | 0.82 | 0.00 | 1, 0, 2, 0 | 0° | 0.00 | 1.00 | нет |
| `Assets/Resources/Programmator/28.png` | 16x10 | — | 1.00 | 0.04 | 0, 0, 0, 0 | 239° | 0.25 | 1.00 | нет |
| `Assets/Resources/Programmator/29.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/3.png` | 11x11 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 0° | 0.54 | 1.00 | нет |
| `Assets/Resources/Programmator/30.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/31.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/32.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/33.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/34.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/35.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/36.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/37.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/38.png` | 19x19 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 137° | 1.00 | 0.87 | нет |
| `Assets/Resources/Programmator/39.png` | 19x19 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 137° | 1.00 | 0.87 | нет |
| `Assets/Resources/Programmator/4.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/40.png` | 30x10 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 0° | 0.00 | 1.00 | нет |
| `Assets/Resources/Programmator/41.png` | 27x28 | — | 0.75 | 0.00 | 0, 3, 0, 4 | 0° | 0.00 | 1.00 | нет |
| `Assets/Resources/Programmator/42.png` | 27x28 | — | 0.75 | 0.00 | 0, 3, 0, 4 | 0° | 0.00 | 1.00 | нет |
| `Assets/Resources/Programmator/43.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 157° | 0.93 | 0.92 | нет |
| `Assets/Resources/Programmator/44.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 148° | 0.96 | 0.90 | нет |
| `Assets/Resources/Programmator/45.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 151° | 0.95 | 0.91 | нет |
| `Assets/Resources/Programmator/46.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 151° | 0.95 | 0.91 | нет |
| `Assets/Resources/Programmator/47.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 152° | 0.95 | 0.91 | нет |
| `Assets/Resources/Programmator/48.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 150° | 0.96 | 0.90 | нет |
| `Assets/Resources/Programmator/49.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 151° | 0.95 | 0.91 | нет |
| `Assets/Resources/Programmator/5.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/50.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 155° | 0.94 | 0.92 | нет |
| `Assets/Resources/Programmator/51.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 154° | 0.94 | 0.92 | нет |
| `Assets/Resources/Programmator/52.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 98° | 0.53 | 0.54 | нет |
| `Assets/Resources/Programmator/53.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 118° | 0.40 | 0.44 | нет |
| `Assets/Resources/Programmator/54.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 153° | 0.95 | 0.91 | нет |
| `Assets/Resources/Programmator/55.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 137° | 1.00 | 0.87 | нет |
| `Assets/Resources/Programmator/56.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 141° | 0.50 | 0.66 | нет |
| `Assets/Resources/Programmator/57.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 137° | 0.43 | 0.84 | нет |
| `Assets/Resources/Programmator/58.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 137° | 0.68 | 0.83 | нет |
| `Assets/Resources/Programmator/59.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 79° | 1.00 | 0.93 | нет |
| `Assets/Resources/Programmator/6.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/60.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 104° | 1.00 | 0.88 | нет |
| `Assets/Resources/Programmator/61.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 0° | 0.55 | 1.00 | нет |
| `Assets/Resources/Programmator/62.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 0° | 0.55 | 1.00 | нет |
| `Assets/Resources/Programmator/63.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 97° | 0.78 | 0.69 | нет |
| `Assets/Resources/Programmator/64.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 102° | 0.39 | 0.54 | нет |
| `Assets/Resources/Programmator/65.png` | 15x15 | — | 1.00 | 0.06 | 0, 0, 0, 0 | 73° | 0.77 | 0.66 | нет |
| `Assets/Resources/Programmator/66.png` | 15x15 | — | 1.00 | 0.06 | 0, 0, 0, 0 | 169° | 0.75 | 0.69 | нет |
| `Assets/Resources/Programmator/67.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 115° | 0.45 | 0.71 | нет |
| `Assets/Resources/Programmator/68.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 49° | 0.87 | 0.74 | нет |
| `Assets/Resources/Programmator/69.png` | 15x15 | — | 1.00 | 0.06 | 0, 0, 0, 0 | 346° | 0.78 | 0.70 | нет |
| `Assets/Resources/Programmator/7.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/70.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 117° | 0.88 | 0.66 | нет |
| `Assets/Resources/Programmator/71.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 208° | 0.82 | 0.76 | нет |
| `Assets/Resources/Programmator/72.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 135° | 0.90 | 0.83 | нет |
| `Assets/Resources/Programmator/73.png` | 14x15 | — | 0.04 | 0.06 | 0, 5, 13, 2 | 135° | 0.98 | 0.87 | нет |
| `Assets/Resources/Programmator/74.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 90° | 0.63 | 0.85 | нет |
| `Assets/Resources/Programmator/75.png` | 15x15 | — | 0.00 | 0.00 | 0, 0, 0, 0 | — | 0.00 | 0.00 | нет |
| `Assets/Resources/Programmator/76.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 132° | 0.51 | 0.72 | нет |
| `Assets/Resources/Programmator/77.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 121° | 1.00 | 0.93 | нет |
| `Assets/Resources/Programmator/78.png` | 15x15 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 282° | 0.17 | 0.99 | нет |
| `Assets/Resources/Programmator/79.png` | 15x15 | — | 1.00 | 0.06 | 0, 0, 0, 0 | 200° | 0.03 | 0.97 | нет |
| `Assets/Resources/Programmator/8.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/80.png` | 15x15 | — | 0.00 | 0.00 | 0, 0, 0, 0 | — | 0.00 | 0.00 | нет |
| `Assets/Resources/Programmator/81.png` | 11x11 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 210° | 0.70 | 1.00 | нет |
| `Assets/Resources/Programmator/82.png` | 11x11 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 220° | 0.67 | 1.00 | нет |
| `Assets/Resources/Programmator/83.png` | 11x11 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 183° | 0.82 | 0.95 | нет |
| `Assets/Resources/Programmator/84.png` | 30x30 | — | 0.00 | 0.00 | 0, 0, 0, 0 | — | 0.00 | 0.00 | нет |
| `Assets/Resources/Programmator/85.png` | 31x30 | — | 0.00 | 0.06 | 0, 17, 30, 9 | 135° | 0.98 | 0.87 | нет |
| `Assets/Resources/Programmator/86.png` | 30x30 | — | 1.00 | 0.02 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/87.png` | 30x30 | — | 1.00 | 0.02 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/88.png` | 30x30 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 172° | 0.88 | 0.96 | нет |
| `Assets/Resources/Programmator/89.png` | 16x22 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 57° | 0.46 | 0.91 | нет |
| `Assets/Resources/Programmator/9.png` | 13x13 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.58 | 1.00 | нет |
| `Assets/Resources/Programmator/90.png` | 16x22 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 58° | 0.46 | 0.91 | нет |
| `Assets/Resources/Programmator/91.png` | 16x22 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 52° | 0.40 | 0.92 | нет |
| `Assets/Resources/Programmator/92.png` | 16x22 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 61° | 0.53 | 0.92 | нет |
| `Assets/Resources/Programmator/93.png` | 16x22 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 43° | 0.55 | 0.92 | нет |
| `Assets/Resources/Programmator/94.png` | 16x22 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 54° | 0.57 | 0.92 | нет |
| `Assets/Resources/Programmator/95.png` | 16x22 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 65° | 0.54 | 0.91 | нет |
| `Assets/Resources/Programmator/96.png` | 16x22 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 42° | 0.54 | 0.91 | нет |
| `Assets/Resources/Programmator/97.png` | 26x23 | — | 0.88 | 0.00 | 0, 1, 1, 1 | 312° | 0.60 | 1.00 | нет |
| `Assets/Resources/Programmator/98.png` | 25x23 | — | 0.92 | 0.00 | 1, 0, 0, 1 | 312° | 0.61 | 1.00 | нет |
| `Assets/Resources/Programmator/99.png` | 29x28 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 262° | 0.70 | 1.00 | нет |
| `Assets/Resources/Skills/Extraction.png` | 73x73 | ok | 1.00 | 0.81 | 0, 0, 0, 0 | 23° | 0.75 | 0.73 | имя |
| `Assets/Resources/Skills/Health.png` | 73x73 | ok | 1.00 | 1.09 | 0, 0, 0, 0 | 317° | 0.81 | 0.85 | имя |
| `Assets/Resources/Skills/MineGeneral.png` | 73x73 | ok | 1.00 | 0.95 | 0, 0, 0, 0 | 23° | 0.75 | 0.73 | имя |
| `Assets/Resources/Skills/Movement.png` | 73x73 | ok | 1.00 | 1.03 | 0, 0, 0, 0 | 158° | 0.85 | 0.88 | имя |
| `Assets/Resources/UI/Sprites/LocalChatBubble.png` | 32x32 | — | 1.00 | 0.96 | 0, 0, 0, 0 | 120° | 0.00 | 0.99 | нет |
| `Assets/Textures/Cells/0.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 237° | 0.55 | 0.64 | нет |
| `Assets/Textures/Cells/000.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 5° | 0.74 | 0.26 | нет |
| `Assets/Textures/Cells/100.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 35° | 0.63 | 0.65 | нет |
| `Assets/Textures/Cells/101.png` | 128x128 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 83° | 0.67 | 0.29 | нет |
| `Assets/Textures/Cells/102.png` | 128x128 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 34° | 0.94 | 0.39 | нет |
| `Assets/Textures/Cells/103.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 240° | 0.79 | 0.39 | нет |
| `Assets/Textures/Cells/104.png` | 32x32 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 37° | 0.28 | 0.55 | нет |
| `Assets/Textures/Cells/105.png` | 128x128 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 7° | 0.91 | 0.30 | нет |
| `Assets/Textures/Cells/106.png` | 512x32 | ok | 0.94 | 0.00 | 32, 0, 0, 0 | 149° | 0.48 | 0.68 | нет |
| `Assets/Textures/Cells/107.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 233° | 0.81 | 0.40 | нет |
| `Assets/Textures/Cells/108.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 360° | 0.75 | 0.46 | нет |
| `Assets/Textures/Cells/109.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 235° | 0.80 | 0.47 | нет |
| `Assets/Textures/Cells/110.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 278° | 0.60 | 0.47 | нет |
| `Assets/Textures/Cells/111.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 237° | 0.57 | 0.65 | нет |
| `Assets/Textures/Cells/112.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 211° | 0.87 | 0.50 | нет |
| `Assets/Textures/Cells/113.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 346° | 0.62 | 0.39 | нет |
| `Assets/Textures/Cells/114.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 192° | 0.19 | 0.13 | нет |
| `Assets/Textures/Cells/115.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 192° | 0.19 | 0.13 | нет |
| `Assets/Textures/Cells/117.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 5° | 0.74 | 0.26 | нет |
| `Assets/Textures/Cells/118.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 56° | 0.83 | 0.32 | нет |
| `Assets/Textures/Cells/120.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 28° | 0.79 | 0.49 | нет |
| `Assets/Textures/Cells/121.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 83° | 0.68 | 0.35 | нет |
| `Assets/Textures/Cells/122.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 13° | 0.58 | 0.18 | нет |
| `Assets/Textures/Cells/29.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 294° | 0.06 | 0.11 | нет |
| `Assets/Textures/Cells/31.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 24° | 0.66 | 0.19 | нет |
| `Assets/Textures/Cells/32.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 24° | 0.66 | 0.19 | нет |
| `Assets/Textures/Cells/35.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 294° | 0.06 | 0.11 | нет |
| `Assets/Textures/Cells/36.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 36° | 0.76 | 0.15 | нет |
| `Assets/Textures/Cells/37.png` | 448x32 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 173° | 0.78 | 0.82 | нет |
| `Assets/Textures/Cells/38.png` | 448x32 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 222° | 0.80 | 0.83 | нет |
| `Assets/Textures/Cells/39.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 159° | 0.52 | 0.14 | нет |
| `Assets/Textures/Cells/40.png` | 32x32 | ok | 0.82 | 0.00 | 1, 2, 1, 2 | 12° | 0.37 | 0.19 | нет |
| `Assets/Textures/Cells/41.png` | 32x32 | ok | 0.91 | 0.00 | 1, 1, 1, 0 | 28° | 0.35 | 0.19 | нет |
| `Assets/Textures/Cells/42.png` | 32x32 | ok | 0.94 | 0.00 | 0, 1, 1, 0 | 47° | 0.35 | 0.18 | нет |
| `Assets/Textures/Cells/43.png` | 32x32 | ok | 0.82 | 0.00 | 2, 1, 2, 1 | 253° | 0.39 | 0.23 | нет |
| `Assets/Textures/Cells/44.png` | 32x32 | ok | 0.85 | 0.00 | 1, 2, 1, 1 | 193° | 0.39 | 0.23 | нет |
| `Assets/Textures/Cells/45.png` | 32x32 | ok | 0.88 | 0.00 | 0, 2, 1, 1 | 341° | 0.40 | 0.21 | нет |
| `Assets/Textures/Cells/48.png` | 128x128 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 0° | 0.00 | 0.46 | нет |
| `Assets/Textures/Cells/49.png` | 128x128 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 46° | 0.10 | 0.44 | нет |
| `Assets/Textures/Cells/60.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 40° | 0.20 | 0.81 | нет |
| `Assets/Textures/Cells/61.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 36° | 0.34 | 0.66 | нет |
| `Assets/Textures/Cells/62.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 25° | 0.63 | 0.71 | нет |
| `Assets/Textures/Cells/63.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 22° | 0.78 | 0.51 | нет |
| `Assets/Textures/Cells/64.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 32° | 0.13 | 0.47 | нет |
| `Assets/Textures/Cells/65.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 30° | 0.22 | 0.26 | нет |
| `Assets/Textures/Cells/70.png` | 32x32 | ok | 0.88 | 0.00 | 1, 1, 1, 1 | 358° | 0.54 | 0.35 | нет |
| `Assets/Textures/Cells/71.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 176° | 0.85 | 0.41 | нет |
| `Assets/Textures/Cells/72.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 224° | 0.84 | 0.56 | нет |
| `Assets/Textures/Cells/73.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 1° | 0.72 | 0.54 | нет |
| `Assets/Textures/Cells/74.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 207° | 0.81 | 0.56 | нет |
| `Assets/Textures/Cells/75.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 276° | 0.53 | 0.60 | нет |
| `Assets/Textures/Cells/76.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 255° | 0.44 | 0.11 | нет |
| `Assets/Textures/Cells/77.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 213° | 0.46 | 0.51 | нет |
| `Assets/Textures/Cells/78.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 218° | 0.54 | 0.63 | нет |
| `Assets/Textures/Cells/79.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 190° | 0.43 | 0.41 | нет |
| `Assets/Textures/Cells/80.png` | 128x128 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 213° | 0.61 | 0.26 | нет |
| `Assets/Textures/Cells/81.png` | 128x128 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 186° | 0.48 | 0.40 | нет |
| `Assets/Textures/Cells/82.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 206° | 0.68 | 0.79 | нет |
| `Assets/Textures/Cells/91.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 7° | 0.96 | 0.71 | нет |
| `Assets/Textures/Cells/92.png` | 32x32 | ok | 0.91 | 0.00 | 1, 1, 1, 0 | 72° | 0.03 | 0.23 | нет |
| `Assets/Textures/Cells/93.png` | 32x32 | ok | 0.88 | 0.00 | 1, 1, 1, 1 | 66° | 0.03 | 0.13 | нет |
| `Assets/Textures/Cells/94.png` | 32x32 | ok | 0.94 | 0.00 | 0, 1, 1, 0 | 75° | 0.02 | 0.30 | нет |
| `Assets/Textures/Cells/97.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 209° | 0.44 | 0.81 | нет |
| `Assets/Textures/Cells/98.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 206° | 0.52 | 0.63 | нет |
| `Assets/Textures/Cells/99.png` | 320x320 | ok | 1.00 | 0.00 | 0, 0, 0, 0 | 38° | 0.56 | 0.93 | нет |
| `Assets/Textures/Clan/1.png` | 13x13 | — | 0.53 | 0.00 | 2, 1, 2, 2 | 0° | 0.00 | 0.70 | нет |
| `Assets/Textures/Crystals/blue.png` | 24x24 | — | 0.92 | 0.00 | 0, 0, 0, 2 | 233° | 0.58 | 0.42 | имя |
| `Assets/Textures/Crystals/cyan.png` | 24x24 | — | 0.88 | 0.00 | 0, 0, 1, 2 | 174° | 0.37 | 0.31 | имя |
| `Assets/Textures/Crystals/green.png` | 25x24 | violation (не 24x24) | 1.00 | 0.00 | 0, 0, 0, 0 | 110° | 0.61 | 0.37 | имя |
| `Assets/Textures/Crystals/red.png` | 24x24 | — | 0.92 | 0.00 | 0, 2, 0, 0 | 360° | 0.44 | 0.45 | нет |
| `Assets/Textures/Crystals/violet.png` | 24x24 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 327° | 0.41 | 0.40 | имя |
| `Assets/Textures/Crystals/white.png` | 24x24 | — | 0.69 | 0.00 | 2, 2, 2, 2 | 139° | 0.04 | 0.31 | имя |
| `Assets/Textures/Items/aliveRadar.png` | 42x42 | ok | 0.66 | 1.06 | 4, 4, 4, 4 | 163° | 0.58 | 0.32 | имя |
| `Assets/Textures/Items/auto.png` | 42x42 | ok | 0.52 | 1.66 | 4, 8, 3, 8 | 354° | 0.17 | 0.48 | имя |
| `Assets/Textures/Items/battery.png` | 42x42 | ok | 0.43 | 0.66 | 9, 5, 8, 7 | 229° | 0.75 | 0.88 | имя |
| `Assets/Textures/Items/bombShop.png` | 42x42 | ok | 0.51 | 1.35 | 6, 5, 6, 7 | 60° | 0.44 | 0.74 | имя |
| `Assets/Textures/Items/botSpot.png` | 42x42 | ok | 0.23 | 0.00 | 10, 9, 12, 13 | 169° | 0.17 | 0.53 | имя |
| `Assets/Textures/Items/c190.png` | 42x42 | ok | 0.88 | 0.07 | 3, 2, 0, 0 | 179° | 0.54 | 0.79 | имя |
| `Assets/Textures/Items/charge.png` | 42x42 | ok | 0.56 | 0.45 | 4, 6, 3, 8 | 233° | 0.69 | 0.61 | имя |
| `Assets/Textures/Items/clans.png` | 42x42 | ok | 0.65 | 1.04 | 3, 6, 1, 6 | 352° | 0.15 | 0.49 | имя |
| `Assets/Textures/Items/compressor.png` | 42x42 | ok | 0.41 | 0.54 | 9, 6, 7, 8 | 87° | 0.27 | 0.72 | имя |
| `Assets/Textures/Items/constructionBot.png` | 42x42 | ok | 0.62 | 0.35 | 3, 8, 4, 3 | 181° | 0.56 | 0.62 | имя |
| `Assets/Textures/Items/craft.png` | 42x42 | ok | 0.51 | 1.38 | 7, 6, 5, 6 | 349° | 0.50 | 0.73 | имя |
| `Assets/Textures/Items/cred.png` | 42x42 | ok | 0.48 | 0.95 | 9, 3, 8, 5 | 54° | 1.00 | 0.76 | имя |
| `Assets/Textures/Items/currency.png` | 42x42 | ok | 0.50 | 1.08 | 9, 3, 9, 2 | 79° | 0.77 | 0.79 | имя |
| `Assets/Textures/Items/disassembler.png` | 42x42 | ok | 0.38 | 0.00 | 10, 6, 9, 7 | 81° | 0.90 | 0.51 | имя |
| `Assets/Textures/Items/eMI.png` | 42x42 | ok | 0.44 | 0.19 | 9, 6, 8, 5 | 260° | 0.17 | 0.44 | нет |
| `Assets/Textures/Items/fED.png` | 42x42 | ok | 0.63 | 1.23 | 0, 6, 5, 6 | 40° | 0.40 | 0.82 | нет |
| `Assets/Textures/Items/freeUp.png` | 42x42 | ok | 0.37 | 0.00 | 8, 6, 12, 6 | 287° | 0.53 | 1.00 | имя |
| `Assets/Textures/Items/gate.png` | 42x42 | ok | 0.37 | 0.86 | 12, 4, 11, 4 | 123° | 0.19 | 0.26 | имя |
| `Assets/Textures/Items/generator.png` | 42x42 | ok | 0.78 | 0.60 | 3, 4, 1, 2 | 355° | 0.47 | 0.63 | имя |
| `Assets/Textures/Items/geoBlack.png` | 42x42 | ok | 0.77 | 0.99 | 1, 0, 1, 8 | 71° | 0.19 | 0.50 | имя |
| `Assets/Textures/Items/geoBlackRock.png` | 42x42 | ok | 0.79 | 1.51 | 0, 4, 1, 4 | 31° | 0.54 | 0.57 | имя |
| `Assets/Textures/Items/geoBlue.png` | 42x42 | ok | 0.77 | 0.71 | 1, 0, 1, 8 | 73° | 0.18 | 0.50 | имя |
| `Assets/Textures/Items/geoCyan.png` | 42x42 | ok | 0.79 | 1.30 | 0, 1, 1, 7 | 74° | 0.18 | 0.50 | имя |
| `Assets/Textures/Items/geoHypno.png` | 42x42 | ok | 0.77 | 0.71 | 1, 4, 1, 4 | 70° | 0.18 | 0.49 | имя |
| `Assets/Textures/Items/geoRainbow.png` | 42x42 | ok | 0.79 | 0.79 | 0, 3, 1, 5 | 31° | 0.56 | 0.60 | имя |
| `Assets/Textures/Items/geoRed.png` | 42x42 | ok | 0.77 | 1.05 | 1, 1, 1, 7 | 68° | 0.19 | 0.50 | имя |
| `Assets/Textures/Items/geoRedRock.png` | 42x42 | ok | 0.79 | 1.09 | 0, 3, 1, 5 | 30° | 0.56 | 0.58 | имя |
| `Assets/Textures/Items/geoViolet.png` | 42x42 | ok | 0.77 | 1.10 | 1, 0, 1, 8 | 69° | 0.18 | 0.50 | имя |
| `Assets/Textures/Items/geoWhite.png` | 42x42 | ok | 0.77 | 1.10 | 1, 0, 1, 8 | 71° | 0.13 | 0.50 | имя |
| `Assets/Textures/Items/geopack.png` | 42x42 | ok | 0.79 | 1.51 | 0, 2, 1, 6 | 71° | 0.16 | 0.46 | имя |
| `Assets/Textures/Items/gun.png` | 42x42 | ok | 0.51 | 1.12 | 6, 6, 6, 6 | 38° | 0.55 | 0.36 | нет |
| `Assets/Textures/Items/market.png` | 42x42 | ok | 0.65 | 1.34 | 2, 5, 2, 7 | 45° | 0.61 | 0.77 | имя |
| `Assets/Textures/Items/mineBooster.png` | 42x42 | ok | 0.34 | 0.00 | 7, 8, 10, 10 | 27° | 1.00 | 1.00 | имя |
| `Assets/Textures/Items/nano.png` | 42x42 | ok | 0.28 | 1.06 | 11, 7, 12, 9 | 1° | 0.58 | 0.43 | имя |
| `Assets/Textures/Items/oPP.png` | 42x42 | ok | 0.72 | 0.07 | 3, 0, 8, 1 | 274° | 0.42 | 0.82 | нет |
| `Assets/Textures/Items/plasmBomb.png` | 42x42 | ok | 0.86 | 0.71 | 2, 1, 1, 2 | 33° | 0.75 | 0.73 | имя |
| `Assets/Textures/Items/poly.png` | 42x42 | ok | 0.37 | 0.99 | 8, 8, 8, 9 | 119° | 1.00 | 0.60 | имя |
| `Assets/Textures/Items/portableTeleporter.png` | 42x42 | ok | 0.57 | 0.42 | 2, 8, 1, 8 | 129° | 0.48 | 0.52 | имя |
| `Assets/Textures/Items/protonBomb.png` | 42x42 | ok | 0.86 | 0.72 | 2, 1, 1, 2 | 266° | 0.75 | 0.73 | имя |
| `Assets/Textures/Items/razBomb.png` | 42x42 | ok | 0.86 | 0.73 | 1, 1, 2, 2 | 152° | 0.75 | 0.73 | имя |
| `Assets/Textures/Items/rem.png` | 42x42 | ok | 0.62 | 0.45 | 3, 6, 4, 5 | 3° | 0.58 | 0.62 | нет |
| `Assets/Textures/Items/resp.png` | 42x42 | ok | 0.51 | 0.85 | 6, 6, 6, 6 | 224° | 0.47 | 0.75 | имя |
| `Assets/Textures/Items/robotRadar.png` | 42x42 | ok | 0.66 | 1.00 | 5, 3, 3, 5 | 295° | 0.52 | 0.31 | имя |
| `Assets/Textures/Items/scanner.png` | 42x42 | ok | 0.22 | 1.42 | 13, 5, 16, 7 | 115° | 0.25 | 0.28 | имя |
| `Assets/Textures/Items/scienceCentre.png` | 42x42 | ok | 0.79 | 1.00 | 1, 3, 1, 4 | 257° | 0.13 | 0.41 | имя |
| `Assets/Textures/Items/storage.png` | 42x42 | ok | 0.41 | 0.93 | 5, 9, 4, 11 | 70° | 0.24 | 0.34 | имя |
| `Assets/Textures/Items/teleport.png` | 42x42 | ok | 0.51 | 1.35 | 6, 6, 6, 6 | 292° | 0.47 | 0.78 | имя |
| `Assets/Textures/Items/trans.png` | 42x42 | ok | 0.34 | 1.12 | 10, 7, 10, 8 | 303° | 0.42 | 0.46 | имя |
| `Assets/Textures/Items/up.png` | 42x42 | ok | 0.51 | 1.35 | 6, 5, 6, 7 | 111° | 0.48 | 0.76 | нет |
| `Assets/Textures/Items/upgradeBooster.png` | 42x42 | ok | 0.46 | 0.00 | 6, 4, 5, 12 | 108° | 0.61 | 0.55 | имя |
| `Assets/Textures/Items/vulkanRadar.png` | 42x42 | ok | 0.66 | 1.06 | 4, 4, 4, 4 | 22° | 0.55 | 0.31 | имя |
| `Assets/Textures/Pack/Clans/0.png` | 114x82 | violation (не кратно 32) | 1.00 | 0.00 | 0, 0, 0, 0 | 7° | 0.06 | 0.42 | нет |
| `Assets/Textures/Pack/Craft/0.png` | 50x47 | violation (не кратно 32) | 1.00 | 0.00 | 0, 0, 0, 0 | 352° | 0.26 | 0.43 | нет |
| `Assets/Textures/Pack/Gun/0.png` | 94x94 | violation (не кратно 32) | 1.00 | 0.00 | 0, 0, 0, 0 | 24° | 0.39 | 0.26 | нет |
| `Assets/Textures/Pack/Market/0.png` | 114x114 | violation (не кратно 32) | 1.00 | 0.00 | 0, 0, 0, 0 | 16° | 0.19 | 0.54 | нет |
| `Assets/Textures/Pack/Resp/0.png` | 50x87 | violation (не кратно 32) | 1.00 | 0.00 | 0, 0, 0, 0 | 247° | 0.29 | 0.61 | нет |
| `Assets/Textures/Pack/Science/0.png` | 242x114 | violation (не кратно 32) | 1.00 | 0.00 | 0, 0, 0, 0 | 169° | 0.10 | 0.51 | нет |
| `Assets/Textures/Pack/Storage/0.png` | 50x18 | violation (не кратно 32) | 1.00 | 0.00 | 0, 0, 0, 0 | 3° | 0.49 | 0.74 | нет |
| `Assets/Textures/Pack/Teleport/0.png` | 50x50 | violation (не кратно 32) | 1.00 | 0.00 | 0, 0, 0, 0 | 300° | 0.33 | 0.83 | нет |
| `Assets/Textures/Pack/Up/0.png` | 50x79 | violation (не кратно 32) | 1.00 | 0.00 | 0, 0, 0, 0 | 116° | 0.23 | 0.41 | нет |
| `Assets/Textures/Skin/bee.png` | 32x32 | — | 0.85 | 0.23 | 0, 3, 2, 0 | 37° | 0.27 | 0.23 | нет |
| `Assets/Textures/Tail/default.png` | 32x32 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 66° | 0.23 | 0.14 | имя |
| `Assets/Textures/UI/checked.png` | 259x253 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 206° | 0.39 | 0.80 | имя |
| `Assets/Textures/UI/deselected.png` | 259x260 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 207° | 0.46 | 0.38 | нет |
| `Assets/Textures/UI/knob.png` | 24x24 | — | 0.88 | 0.00 | 0, 0, 1, 2 | 174° | 0.37 | 0.31 | имя |
| `Assets/Textures/UI/mm_icon_chronicle.png` | 128x128 | — | 0.47 | 1.16 | 21, 16, 27, 16 | 0° | 0.00 | 1.00 | имя |
| `Assets/Textures/UI/mm_icon_discord.png` | 128x128 | — | 0.77 | 0.85 | 0, 15, 0, 15 | 0° | 0.00 | 1.00 | имя |
| `Assets/Textures/UI/mm_icon_exit.png` | 128x128 | — | 0.70 | 0.84 | 11, 11, 9, 11 | 0° | 0.00 | 1.00 | имя |
| `Assets/Textures/UI/mm_icon_repair.png` | 128x128 | — | 0.77 | 1.14 | 8, 8, 8, 8 | 0° | 0.00 | 1.00 | имя |
| `Assets/Textures/UI/mm_icon_settings.png` | 128x128 | — | 1.00 | 1.14 | 0, 0, 0, 0 | 0° | 0.00 | 1.00 | имя |
| `Assets/Textures/UI/mm_icon_telegram.png` | 128x128 | — | 0.71 | 0.89 | 10, 10, 10, 10 | 0° | 0.00 | 1.00 | имя |
| `Assets/Textures/UI/mm_icon_update.png` | 128x128 | — | 0.56 | 0.68 | 19, 13, 19, 13 | 0° | 0.00 | 1.00 | имя |
| `Assets/Textures/UI/mm_icon_vk.png` | 128x128 | — | 0.71 | 1.16 | 10, 10, 10, 10 | 0° | 0.00 | 1.00 | имя |
| `Assets/Textures/UI/mm_logo.png` | 128x128 | — | 0.68 | 0.00 | 15, 7, 15, 7 | 196° | 0.67 | 0.53 | имя |
| `Assets/Textures/UI/mm_shade.png` | 1024x1024 | — | 1.00 | 0.88 | 0, 0, 0, 0 | 220° | 0.75 | 0.05 | имя |
| `Assets/Textures/UI/mm_space_bg.png` | 1920x1080 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 223° | 0.75 | 0.02 | имя |
| `Assets/Textures/UI/selected.png` | 260x260 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 206° | 0.39 | 0.81 | имя |
| `Assets/Textures/UI/unchecked.png` | 256x255 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 206° | 0.44 | 0.38 | имя |
| `Assets/Textures/perspective.png` | 480x329 | — | 1.00 | 0.03 | 0, 0, 0, 0 | 55° | 0.62 | 0.41 | имя |
| `Assets/Textures/terrain-decals.png` | 512x32 | — | 0.33 | 0.45 | 4, 11, 14, 10 | 27° | 0.21 | 0.33 | имя |
| `Assets/Textures/transit.png` | 480x47 | — | 1.00 | 0.00 | 0, 0, 0, 0 | 46° | 0.59 | 0.32 | имя |
