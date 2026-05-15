# Fodinae

2D-клиент для [Fodinae](https://github.com/MinesReborn) — реворк клиента MMORPG Сергея Мячина. Проект сфокусирован на производительном тайловом рендеринге и современной сетевой архитектуре.

## Требования

- **Unity 6** (`6000.2.10f1`) — [скачать через Unity Hub](https://unity.com/releases/editor/archive)
- **Git** — для клонирования и подтягивания пакетных зависимостей

## Быстрый старт

```bash
git clone https://github.com/MinesReborn/Fodislop.git
```

1. Откройте проект через **Unity Hub** → `Open` → выберите папку проекта.
2. Unity автоматически подтянет внешние зависимости.
3. Откройте сцену `Assets/Scenes/SampleScene.unity`.
4. Нажмите **Play**. При отсутствии сервера включится автономный тестовый режим.

## Архитектура

Подробные инструкции для разработчиков и описание внутренних систем — в файле [**`AGENTS.md`**](AGENTS.md).

### Ключевые фичи

- **Custom Terrain Rendering**: Эффективный рендеринг террейна в один меш с использованием 7 UV-каналов.
- **Chunk Streaming**: Дисковое кэширование и стриминг мира с RLE-сжатием.
- **Packet-driven UI**: Динамическая сборка интерфейса на базе серверных пакетов через UI Toolkit.
- **Modern Networking**: Асинхронная обработка пакетов на базе `UniTask`.

## Roadmap / TODO

- [ ] Формирование детального технического бэклога.
- [ ] Определение ключевых этапов разработки (Milestones).
- [ ] Анализ и приоритизация будущих функциональных модулей.

## Зависимости

- **Core**: `darkar25.fodinae.*` (data, networking) — сетевой стек.
- **Compression**: SharpCompress, ZstdSharp, LZ4.
- **Async**: UniTask.
- **Other**: NetCoreServer, WebP decoder.

## Contributing

Правила оформления PR и стандарты кода — в [`CONTRIBUTING.md`](CONTRIBUTING.md).

## Лицензия

[MIT](LICENSE)
