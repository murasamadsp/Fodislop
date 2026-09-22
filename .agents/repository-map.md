# Kern Repository Map

This file navigates the project tree. It does not replace `AGENTS.md` or
`.agents/project-context.md`; it only describes locations and subsystem boundaries.

## Top level

| Path | Purpose | Change rule |
| --- | --- | --- |
| `Assets/` | Unity source, scenes, materials, UI, and runtime resources | Do not edit Unity assets as text; move `.meta` files with their assets |
| `Assets/Scripts/` | C# runtime/editor/tests | The nearest asmdef owns a file; do not cross a boundary without checking dependencies |
| `Assets/Resources/Shaders/` | Production shaders/compute and include files | Read `docs/architecture/LIGHTING_ARCHITECTURE.md` before lighting changes |
| `Packages/` | Local Unity packages and third-party code | Do not mix with game code |
| `tools/` | Standalone .NET/Python tools and test harnesses | Generated `bin/`/`obj/` are not source files |
| `visual/` | Independent UI lab and visual generators | Do not treat it as production UI without an explicit production path |
| `KernAudio/` | FMOD Studio project and banks | Synchronization is documented in the `fmod-sync` skill |
| `ProjectSettings/` | Unity project configuration | Change only when requested; do not mass-format |
| `docs/` | Self-contained HTML reports and project notes | New HTML must be self-contained with inline styles |
| `.agents/` | Agent instructions and context | Do not place runtime code here |
| `scripts/` | Small asset/design automation scripts | Do not confuse with `tools/`: scripts operate on project artifacts |

## C# modules

The main boundaries are defined by asmdefs, not folder depth:

- `Kern.Core` — shared runtime services, configuration, and lifecycle;
- `Kern.Infrastructure` — external adapters, audio, and Effekseer;
- `Kern.Application` — game/player orchestration;
- `Kern.Presentation` — rendering and debug tooling;
- `Kern.World` — world, terrain, streaming, and lighting;
- `Kern.Networking` — client networking layer;
- `Kern.UI` — UI Toolkit and presentation;
- `Kern.Persistence` — saving and loading;
- `Kern.Bootstrap` — composition roots and startup;
- `Kern.Editor` — editor tooling;
- `Kern.Tests.*` — test assemblies;
- `Kern.Contracts` — the lowest contract layer through `asmdef`/`asmref`.

The `Core`, `Game`, and `Rendering` folders and their subtrees are assembly
roots. `Player` lives inside `Game`, `Tools` inside `Rendering`, and the
Effekseer adapter inside `Audio`, so every layer has one explicit root.

## Documentation navigation

- visual catalog: [`docs/index.html`](../docs/index.html);
- architecture and invariants: [`AGENTS.md`](../AGENTS.md),
  [`.agents/project-context.md`](project-context.md);
- lighting dataflow: [`LIGHTING_ARCHITECTURE.md`](../docs/architecture/LIGHTING_ARCHITECTURE.md);
- current handoffs and unfinished work: [`docs/operations/`](../docs/operations/),
  [`docs/planning/TODO.md`](../docs/planning/TODO.md).

## Deliberately not source files

Local `.kilo/worktrees/`, `Library/`, `Temp/`, `Logs/`, `Build/`,
`LightingDumps/`, `ProfilerCaptures/`, `UserSettings/`, `bin/`, and `obj/` must
not appear in the production-file map. Their presence is useful for local work,
but must not create a false impression of duplicated source.

## Next safe step

1. Break down root handoffs/specifications by lifecycle and owner.
2. Check which `Assets/Scripts/*` actually cross asmdef boundaries.
3. Only then reduce folder depth or move C# together with `.meta` files and
   reference checks.
