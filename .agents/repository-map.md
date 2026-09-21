# Карта репозитория Kern

Этот файл — навигатор по дереву проекта. Он не заменяет `AGENTS.md` и
`.agents/project-context.md`: здесь только расположение и границы подсистем.

## Верхний уровень

| Путь | Назначение | Правило изменений |
| --- | --- | --- |
| `Assets/` | Unity-исходники, сцены, материалы, UI и runtime-ресурсы | Unity-ассеты не редактировать текстом; `.meta` перемещать вместе с объектом |
| `Assets/Scripts/` | C# runtime/editor/tests | Владелец определяется ближайшим asmdef; не переносить файл через границу без проверки зависимостей |
| `Assets/Resources/Shaders/` | production shaders/compute и include-файлы | Для lighting читать `docs/architecture/LIGHTING_ARCHITECTURE.md` перед изменением |
| `Packages/` | локальные Unity packages и third-party code | Не смешивать с кодом игры |
| `tools/` | standalone .NET/Python-инструменты и тестовые harness'ы | Generated `bin/`/`obj/` не являются исходниками |
| `visual/` | независимый UI lab и визуальные генераторы | Не считать production UI без явного production-пути |
| `KernAudio/` | FMOD Studio project и банки | Синхронизация описана в skill `fmod-sync` |
| `ProjectSettings/` | Unity project configuration | Менять только по задаче, не форматировать массово |
| `docs/` | автономные HTML-отчёты и проектные заметки | Новые HTML — self-contained, inline style |
| `.agents/` | инструкции и контекст для агентов | Не складывать сюда runtime-код |
| `scripts/` | небольшие asset/design automation scripts | Не путать с `tools/`: scripts работают с проектными артефактами |

## C#-модули

Основные границы определяются asmdef, а не глубиной папки:

- `Kern.Core` — общие runtime-сервисы, конфигурация и lifecycle;
- `Kern.Infrastructure` — внешние адаптеры, audio и Effekseer;
- `Kern.Application` — game/player orchestration;
- `Kern.Presentation` — rendering и debug tooling;
- `Kern.World` — мир, terrain, streaming и lighting;
- `Kern.Networking` — сетевой клиентский слой;
- `Kern.UI` — UI Toolkit и presentation;
- `Kern.Persistence` — сохранение и загрузка;
- `Kern.Bootstrap` — composition roots и запуск;
- `Kern.Editor` — editor tooling;
- `Kern.Tests.*` — тестовые сборки;
- `Kern.Contracts` — нижний слой контрактов через `asmdef`/`asmref`.

Папки `Core`, `Game`, `Rendering` и их поддеревья теперь являются assembly
roots. `Player` находится внутри `Game`, `Tools` — внутри `Rendering`, а
Effekseer-адаптер — внутри `Audio`, чтобы каждый слой имел один явный root.

## Навигация по документации

- визуальный каталог: [`docs/index.html`](../docs/index.html);
- архитектура и инварианты: [`AGENTS.md`](../AGENTS.md),
  [`.agents/project-context.md`](project-context.md);
- lighting dataflow: [`LIGHTING_ARCHITECTURE.md`](../docs/architecture/LIGHTING_ARCHITECTURE.md);
- текущие handoff и незавершённые работы: [`docs/operations/`](../docs/operations/),
  [`docs/planning/TODO.md`](../docs/planning/TODO.md).

## Что сознательно не является исходником

Локальные `.kilo/worktrees/`, `Library/`, `Temp/`, `Logs/`, `Build/`,
`LightingDumps/`, `ProfilerCaptures/`, `UserSettings/`, `bin/` и `obj/` не
должны попадать в карту production-файлов. Их наличие полезно для локальной
работы, но оно не должно создавать ложное ощущение дублирования исходников.

## Следующий безопасный этап

1. Разобрать корневые handoff/спецификации по жизненному циклу и владельцу.
2. Проверить, какие `Assets/Scripts/*` реально пересекают asmdef-границы.
3. Только после этого сокращать глубину каталогов или переносить C# вместе с
   `.meta` и проверкой ссылок.
