# Visual workspace

`visual/` — независимый UI lab проекта. Он нужен для быстрых визуальных
итераций и генерации проверочных данных; это не Unity runtime и не источник
истины для production-рендера.

## Состав

- `kern-ui-lab/` — статический browser UI lab;
- `kern-ui-lab/css/` — токены, базовые компоненты и экраны;
- `kern-ui-lab/i18n/` — локализованные строки lab;
- `kern-ui-lab/tools/` — inventory, fit, cascade и asset-generation scripts;
- `kern-ui-lab/tools/derived/` — производные данные, генерируемые из lab.

## Граница production

Изменение в `visual/` само по себе не меняет UI Toolkit в `Assets/`. Для
переноса результата нужны отдельные изменения в production UXML/USS/скриптах
и проверка через production-путь.

Подробная карта всего репозитория:
[`../.agents/repository-map.md`](../.agents/repository-map.md).
