---
name: in-memory-audit
description: Autonomous code audit and bug hunting performed mentally on files already loaded into context, without tools, search, or external commands.
---

# In-Memory Bug Hunter

This skill performs a deep analysis of code defects, architectural problems,
leaks, and logical errors using only the files already loaded into context.
No intermediate shell, search, or file-tool calls are allowed during the audit.

## 1. Zero-tool principle

When this mode is active:

1. **No additional tool calls:** do not invoke commands, searches, file viewers,
   or background utilities during the audit.
2. **The code under review is already in context:** reason only from files
   supplied by the user or loaded when the task started.
3. **Mental compilation and tracing:** simulate runtime behavior, garbage
   collection, concurrency, and GPU execution mentally.

## 2. Mental analysis pipeline

Run the loaded code through five filters:

### Phase 1. Lifecycle and execution order

- **Entry points:** what runs first (`Awake`, `OnEnable`, `Construct`, `Start`)?
  Is anything using an uninitialized injection or field?
- **Reset and re-entry:** are static collections, caches, and event subscriptions
  cleared when a session restarts or a scene changes?
- **Deinitialization order (`OnDisable`, `OnDestroy`, `Dispose`):** can a resource
  be released while a worker thread or background task still uses it?

### Phase 2. Asynchrony and concurrency

- **State races:** can two async methods mutate the same field or collection
  concurrently without a lock or semaphore?
- **Forgotten operations and cancellation tokens:** are tokens passed through
  every nested call? What happens on cancellation?
- **Context switching:** does background-pool code (`UniTask.RunOnThreadPool`
  or `Task.Run`) access Unity objects such as `Transform`, `Texture`,
  `GameObject`, or `Time`?
- **Duplicate calls and swallowed exceptions:** is `async void` used outside
  event handlers? Can critical I/O failures disappear?

### Phase 3. Resources, memory, and GC pressure

- **Native-resource leaks:** for every `ComputeBuffer`, `RenderTexture`,
  `Texture2D`, `NativeArray`, and `FileStream`, is `Dispose`/`Release`/`Destroy`
  guaranteed even when an exception occurs (`try/finally`)?
- **Hidden hot-path allocations:** closures, boxing, new collections, LINQ,
  string interpolation, and concatenation in `Update`, `Tick`, `OnGUI`, or `Draw`.

### Phase 4. Edge cases and numerical stability

- **Division by zero and singularities:** are vector normalization, scale
  calculations, and delta-time division guarded?
- **NaN/Infinity propagation:** are trigonometry, roots, logarithms, and user
  input results validated?
- **Array bounds:** check `Length`, circular buffers, spans, slices, and empty arrays.
- **Nullability traps:** inspect every null-forgiving `!` that can be null at runtime.

### Phase 5. Architectural purity and hidden side effects

- **Side-effecting getters:** properties must not mutate state, allocate, or do
  I/O merely when read.
- **Contract violations:** does the class validate inputs at startup, or pass
  invalid state deeper into the system?

## 3. Audit report format

```markdown
### Mental code audit

#### [Severity: Critical / High / Medium / Micro-optimization] Defect name
- **Location:** `File.cs:Lnumber` or method signature.
- **Mechanism:** exact scenario that causes the failure, leak, or degradation.
- **Why it is a bug:** concise reasoning without requiring runtime execution.
- **Ready fix:** the exact corrected code block.
```

## 4. Usage example

1. The user supplies files or asks for an in-memory audit without tool calls.
2. The agent enables mental simulation through all five phases.
3. The agent immediately returns a structured report with concrete defects and fixes.
