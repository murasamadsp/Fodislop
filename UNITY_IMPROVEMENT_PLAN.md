# План превращения Fodislop в «настоящий» Unity-проект

> Диагноз: проект — настоящий Unity-пайплайн (URP, UI Toolkit, Effekseer, FMOD,
> ScruenceTool), но сверху навешан параллельный .NET-слой, который у*крал* у Unity
> власть над сборкой, ассетами и кодом. Проблема — **не** в «код вместо префабов»,
> а в том, что держится **вторая сборочная система** (`dotnet build` + ручное ведение
> `<Compile Include>` + гитрэкинг Unity-генерированных `.csproj`), которая создаёт
> класс фейлов (CS0246 от пропущенных файлов) и заставляет бороться с генератором Unity.
>
> **Главная цель**: вернуть Unity роль единственного владельца точки сборки и графа
> ассембли (через `.asmdef`), оставив архитектуру «code-first» (scene/bootstraps,
> кастом CDN-кэш, world-streaming `.mapb`) как осознанный выбор, а не как костыль.

## НЕ-цели (осознанно исключены из этого трека)

- **Resources / Addressables** — переезд контента на Unity-ресурсы это отдельный суперогромный
  серверный трек (каталог тяжёлых текстур, банков, тайлсетов). Здесь не трогаем.
- Полная «префабизация» сущностей мира / полный перенос игровой log отк-и।
- «Очистка` кэша как способ чинить баги» — запрещено насовсем (см. AGENTS §6).

## Принципы (инварианты, на которых стоит решение)

1. **Unity — единственный источник `.csproj`, `.sln`, `.asmdef` графа.**
   - Файлы, которые Unity генерирует (`*.csproj`, `*.slnx`, `obj/`, `Temp/`), **не коммитим**.
   - Никаких постпроцессоров, переписывающих генерацию (`OnGeneratedCSProject`).
2. **Одна точка «что в ходит в сборку» = asmdef-структура**, не ручной `<Compile Include>`.
3. **Линтинг/анализ — через правильный MSBuild-механизм, пер-assembly**, а не через
   глобальный `Directory.Build.props`, который бьёт по vendored-коду.
4. Сборка и лид остаются доступны из CLI (`dotnet build` по сгенерирован. Unity asmdef csproj)
   — для pre-commit и CI, но теперь это *наш* какasmdef-домен, а не CommÜ один «Assembly-CSharp» с ручным списком файлов.

---

## Фаза 1. Git-гигиена и отказ от «борьбы с Unity»  ~ 30–60 мин

Цель: убрать из гита artifacts и постпроцессор-костыли, чтобы Unity владел генерацией.

- [ ] **Untrack все Unity-генерированные файлы** у корня:
      `git rm --cached *.csproj *.slnx` (Assembly-CSharp*.csproj, UniTask*.csproj, Effekseer*.csproj,
      MinesServer*.csproj, McpUnity*.csproj, Tests*.csproj, EffekseerEditor.csproj, Fodislop.slnx).
- [ ] `.gitignore`: **перестать исключать** `!/*.csproj` и `!/*.slnx` (строки 413-414, 484-487) —
      вернуть `*.csproj`/`*.sln`/`*.slnx` в ignore полностью. Убедиться, что `Assets/**/*.meta` остаются
      в гите (они — объекты Unity source), а генераторы — нет.
- [ ] **Удалить `Assets/Editor/CsProjFix.cs`** + `.meta`: это `AssetPostprocessor. OnGeneratedCSProject`,
      переписывает `<LangVersion>` в каждом сгенерированном csproj. После перехода на asmdef Unity даст
      C# 12 по-умолчанию (Unity 6 поддерживает), костыль не нужен.
- [ ] **Рассмотреть удаление/деактивацию `SdrOutputEnforcer`** (`InitializeOnLoad` мигрирует HDR в
      `UniversalRenderPipelineAsset` на каждом открытии) — это разовая миграция, а не постоянный жор.
      Перенести в одноразовую `RepairGraphicsQualityProfileUtility`/setup-скипт (уже есть похожие
      утилиты в `Assets/Scripts/Editor/`) и убрать `EditorApplication.delayCall`-хук из runtime-флоу.
- [ ] Проверить: **гит уже не тащит** `obj/`, `Temp/`, `Library/`, `Logs/`, `UserSettings/`
      (сейчас в `.gitignore` `/obj/`, `/Temp/` и т.д.). Оставляем.

**Гейт**: `git status` чист после `git rm --cached`, реопонт не содержат `.csproj`; Unity импорт
проходит без постпроцессор-правок.

---

## Фаза 2. Сборка-граф через `.asmdef` (основное)  ~ 1.5–3 часа

Цель: заменить один монолитный `Assembly-CSharp` (и ручной `<Compile Include>`) на asmdef-ассамбли,
которые Unity и линтер-граф генерит сам.

### 2.1 Создать runtime-asmdef по архитектурным доменам (по фактическим namespace из кода)

| asmdef | namespace(s) | зависти на |
| --- | --- | --- |
| `Fodinae.Core` | `Fodinae.Core`, `Fodinae.Core.Interfaces` | (базз: Unity + VContainer) |
| `Fodinae.Networking` | `Fodinae.Networking`, `Fodinae.Networking.*` | Core |
| `Fodinae.Audio` | `Fodinae.Audio.*` | Core, FMOD |
| `Fodinae.World` | `Fodinae.World.*`, `Fodinae.World.Terrain` | Core |
| `Fodinae.Rendering` | `Fodinae.Rendering.*` | World, Core |
| `Fodinae.Game` | `Fodinae.Game`, `Fodinae.Game.Managers` | Core, Networking, World, UI(представления) |
| `Fodinae.Player` | `Fodinae.Player.*` | Core |
| `Fodinae.UI` | `Fodinae.UI.*`, `Fodinae.UI.Builders/Programmator/HUD/*` | Core, Game (ViewModel/Model), World (данные карты) |
| `Fodinae.Editor` | `Fodinae.Editor`, `Assets/Scripts/Editor/*` + `Assets/Editor/*` | **//Only Editor**, fav many=asmdef |
| `Fodinae.Tests` | `Fodinae.Tests.*` | все + TestFrameWork, Editor |

> Точный граф следует подтвердить по реальным зависимостям (см. §2.4). asmdef — минимальный
> зоов, избегаем циклов: `UI` не зависит от `Game.Managers`, а от thin-interfaces в `Core.Interfaces`.

- [ ] Создать `.asmdef` файлы рядом с папками (Unity auto-scan). Каждый с:
      (Dll? which): имена авто, `Auto Referenced`: true для runtime, false для Editor/Tests,
      explicit `references` вместо `AutoReferenced` на желаемые соседи.
- [ ] Для vendored кода — **отдельные ассембли**: `VContainer.Runtime.asmdef` (в
      `Assets/Scripts/VContainer/Runtime`, уже есть папка) и `MgGifDecoder` (пока в отдельном файле аsmdef чтобы не линтится основным).
      Позволяет исключить их из анализа сов своих правил.

### 2.2 Решить GlobalUsings per-assembly

- Сейчас `Assets/Scripts/Core/GlobalUsings.cs` содержит один `global using Audio=SFX`,
  но AGENTS §1 обещает «Fodinae.Core.GlobalUsings» массово учитывает многие namespace (Core,
  Networking, World, Game, Player, UI, Effekseer). Проверить де-факто: код явно `using`'и по файлам
  (см. `GameBootstrap.cs` — 15+ явных using). Это нормально, т.к. **global using сра European only в
  своей сборке**. Решение:
  - Оставить явные `using` (не «мигрируют» on global-usings-мозг) — главное не добавлять их
    в пустых случаях.
  - `GlobalUsings.cs` расположить в каждом runtime-asmdef как место для *пересекающих* alias
    (типа `Audio`), либо вынести в отдельный астирауent.
- [ ] Перепроверить: **ни один файл game-кода не зависит от типов из другого asmdef
      в обход заявленного графа** (иначе Unity ловите «needs reference to ...» на импорт).

### 2.3 Удалить ручной сводный Compile Include

- [ ] `dotnet build Assembly-CSharp.csproj` больше не является основной точкой заказа:
      вместо него используем события из Unity-графа (см. Фаза 3). Сам `Assembly-CSharp.csproj`
      остаётся как *генерируемый* Unity итог экспорта (не коммитим).
- [ ] ВП Hunter: убрать правки, что резолви CS0246 «вручную add to csproj» (как было с
      `IOfflineConnection.cs`, `WorldMapController.cs`). После asmdef этот класс проблем исчезает.

### 2.4 Составить карту реальных зависимостей

- [ ] Прогнать стат-анализ (`scripts/inject_analysis.py` + `grep` по `namespace`/`using`) и
      зафиксировать фактический граф фа сумме в `docs/asmdf-graph.md` (автономный html не обязательно,
      — như текст в `/docs` это не запрещено, если не был дубликатом).
- [ ] Выбирать непротиворечивый порядок: `Core → Networking/Audio → World → Rendering →
      Game/Player → UI`, `Editor`/`Tests` отдельно, без циклов.

---

## Фаза 3. Перенос анализа-политики и кли-сборки на новый фундамент  ~ 0.5–1 день

Цель: чтобы «dotnet-lint-lint работал через Unity csproj(ых) asmdef, а не через
глобальный `Directory.Build.props`, не переизвесь vendored-код.

- [ ] `.editorconfig`, `.stylecop.json` остаются для IDE-навигации — это ок.
- [ ] **Смягчить `Directory.Build.props`**: его условия `Condition="'$(MSBuildProjectName)'=='Assembly-CSharp'"`
      перестают быть у dissпл; вместо него **одна корневой** доп. пропайтом на ассmod `Fodinae.*`
      (например `Condition="$(MSBuildProjectName)–matches 'Fodinae\.'`), а vendored-ассбmovie
      (VContainer, Effek-еек, MinesServer, UniTask, MgGifDecoder) — condition их исключает.
- [ ] `scripts/pre-commit-lint.sh`: переехать с ручного списка зависимых правитель (`DEPENDENCIES=`)
      на loop по найденным `Fodinae.*.csproj` + их шаблер восстановления (`dotnet ef8 list`-helper
      см. ниже). «Assembly-CSharp*» больше не жёсткий ярёкокуто; собираем `Fodinae.*`.
- [ ] Добавить создаю scripт-хелпер **regen-asam**: если в корне нет `*.csproj` (Unity не открывался),
  запускать `Unity -quit -batchmode -projectPath . -executeMethod Script.FlampProjectGenerate`
  (через `BuildScript`-подобный метод) — чтобы pre-commit не «пропадал» без GitHub.

---

## Фаза 4. Средства Editor/утиц и рабочий процесс: убрать конфликты с импорт-процесс

~ 0.5–1 дня

- [ ] Inventory`editors-utils: собранные в`Assets/Scripts/Editor/`:
      `RepairGraphicsQualityProfileUtility`,`FixRenderer2DFeaturesUtility`,`FixPlayerPrefabUtility`,
      `EnsureLightingEngineInMainSceneUtility`,`FixPlayerPrefab` — оставляем (one-shot stop-gap).
      Они НЕ должны вредят, но — **продумать/перевести на menu>># Tool/Debug-and-auto**, а не в
      bdeafly-run. Проверить, нет ли `[InitializeOnLoad]` с сайд-эффектами (кроме SdrOutputEnforcer — см ф1).
- [ ] Убрать из `GlobalPostProcessVolume`/terain жизн динамического расстановки того, что должно жить в
      лицензированной сцене/профиладовых хлорисе творкар — перенестие через EditorScriptableRESettingfulness
      уместо ретя=ку глубины стритма. (это узкое повторно использует.

---

## Фа-за 5. Проверка и документацию  ~ 3–6 ч

- [ ] Прогнать полный loop:
      1. Unity import (UnityEfx Hub → сцена открывается),
      2. `dotnet build Fodinae.Core.csproj ...` + Fodinae.ViewModel*.csproj (по-факт;) — zero CS/SAn/CA/RCS/S/UNT.
      3. pre-commit hooks (setup-hooks + `run-linter` skill) — проходят.
      4. `Fodinae.Diagnostics`/Validate Injections`(`scripts/inject_analysis.py`) — все`[Inject] разрешаются.
      5. F11/F12 basic hit-build & runtime smoke (MainGame.unity ш юдит в play mode smoke).
- [ ] Обновить `AGENTS.md`:
      §2 карта проекта (asmdef-доменов и csproj-тита) — убрать «Assembly-CSharp.csproj» как точку сборки
      (§7 «Линтинг C#»); §1 — «global usings per-asmdef»; §4 пункт project-структуры — csproj-некоммит.
- [ ] `INCIDENT_REPORT.md / TODO.md`: разобрать вписки про «ручные sandbox` «добавить в csproj» —
      это были симпться bugs блокировки, где после asmdef больше невозможно.

---

## Фаза 6. Долг-невая гигиена кода (по-xодимо, отдельного но критично)

Цель: проект уже переходит с «packed» к графовому — по пути улучшение «дно-грейха» вещей,
которые преимущественно в абсолютном одиночаА. НЕ радиусно: только если репозитории в тот
же флоу-исходный код с оt — чтобы не раскалывать дыру ни в фаза 2.

- [ ] «Test tests правильное сговорку» тек, где высокое OHeap в `Update` (§9 AGENTS) — одно-то
      поместити в заявку пунктах.
- Конфликт доверие: **noimplicit defaults / fail-fast** уже зафикрер — тай-костов не добавляем.

---

## Критерии «настоящего Unity» (definition of done)

1. `git ls-files '*.csproj' '*.slnx'` → **пусто** (Unity генерит и они не в гите).
2. `git status` не виден `obj\`-бать эти lm.
3. Adding new .cs anywhere in `Assets/Scripts/<domain>` → **не требует правок сбр/гита**.
4. `dotnet build Fod*.csproj` (генерит) — zero over C# warnings for our code; vendoredможрафых не лінится.
5. В `.gitignore:`*.csproj`,`*.sln`,`*.slnx` — ignore-я-руh (норм); `*.meta` остаются committed.
6. `CsProjFix.cs` и `AssetPostprocessor.OnGeneratedCSProject` — **удалено**.
7. Runtime smoke на `MainGame.unity` — play запускается без инжект-Null и с нуля ошибок некритичных.
8. Majority None of `Assembly-CSharp`-specific sciptна-«hacks` не требуется.

---

## Что дальше (§2 перемен)

После `Unity_improvement_plan` выполняемся **§1 (git hygiene)** первым — самый дешёвый и даёт
немедленный эффект на стабильность. Затем §2 asmdef (основная) — рекомендованный следующий
актом создания графа зависимостей и регресси-тейстов. §3–5 — сопрстность корректильно по мере дохода.

**Контроль масштаба:** §1 (git чистая применяем и сам по себе полезен даже если asmdef отложат).
§2 раскроется только когда мы, а вегда можно остановиться после §1 и решить до.
