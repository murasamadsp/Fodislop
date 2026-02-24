# GEMINI.md - Project Instructions

## Unity Assets & Serialization (Scenes, Prefabs, Meta)
**Context:** This project uses the Unity Editor workflow. Do not attempt to generate all UI/Object hierarchies purely via C# code (e.g., `new GameObject()`, `AddComponent<Text>()`). We prefer editing assets directly.

### Handling YAML Assets (`.unity`, `.prefab`):
*   **Format:** These are text-based YAML files. You are allowed to read and modify them.
*   **Modification Strategy:**
    *   **Prefabs:** Prefer modifying `.prefab` files over `.unity` scene files. It is safer to modify a UI Prefab textually than a whole Scene.
    *   **GUIDs:** Be extremely careful not to break `guid` references. If you copy an object in YAML, you must generate a new unique `fileID`.
*   **UI Generation:**
    *   **Do not** write C# scripts that build complex UI from scratch at runtime.
    *   **Do:** Write C# scripts that reference `[SerializeField]` variables, then instruct on how to wire them up, OR directly modify the `.prefab` YAML to add the components if you are confident in the syntax.

### Handling Meta Files (`.meta`):
*   **CRITICAL:** Every asset must have a `.meta` file.
*   **Moving/Renaming:** If you suggest moving or renaming a `.cs` file or asset via shell, you **must also move/rename the corresponding `.meta` file** to maintain references.