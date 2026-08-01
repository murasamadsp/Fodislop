# INCIDENT REPORT & HANDOFF

**Project**: Fodinae (Unity 6 / URP 2D)
**Dates**: July 30–31, 2026
**Status**: INCIDENT B RESOLVED — `PostProcessRendererFeature` is recognized and attached; diagnostic `PostFxRendererFeature` removed

---

## 1. Executive Summary

This document covers TWO related incidents across multiple sessions:

**Incident A (July 30)**: Namespace migration corruption — C# compiles clean (0 errors) but `.prefab`/`.unity` YAML has broken fileID mappings.

**Incident B (July 31 — RESOLVED)**: Post-processing compute shader system created entirely by AI agents. During the incident C# compiled cleanly via `dotnet`, but Unity temporarily refused to recognize `PostProcessRendererFeature` and emitted:

```
"No script asset for PostProcessRendererFeature. Check that the definition is in a file of the same name and that it compiles properly."
```

The failure was later repaired and verified through Unity itself: `MonoScript.GetClass()` resolved `PostProcessRendererFeature`, an instance could be created, and the feature was attached to `Renderer2D.asset`. The exact initial trigger was not proven; the cache, Play Mode, namespace-poisoning and lingering-process explanations must not be presented as established causes.

---

## 2. Files Created (All AI-Generated)

### Volume Components
```
Assets/Scripts/Rendering/PostProcessing/Components/BloomComponent.cs
Assets/Scripts/Rendering/PostProcessing/Components/VignetteComponent.cs
Assets/Scripts/Rendering/PostProcessing/Components/ChromaticAberrationComponent.cs
Assets/Scripts/Rendering/PostProcessing/Components/ColorGradingComponent.cs
Assets/Scripts/Rendering/PostProcessing/Components/EigengrauComponent.cs
Assets/Scripts/Rendering/PostProcessing/Components/MotionBlurComponent.cs
```

### Renderer Features & Passes
```
Assets/Scripts/Rendering/PostProcessing/PostProcessRendererFeature.cs    # THE BROKEN ONE
Assets/Scripts/Rendering/PostProcessing/PostProcessRenderPass.cs
Assets/Scripts/Rendering/PostProcessing/FodinaeEffekseerRenderPassFeature.cs
Assets/Scripts/Rendering/PostProcessing/VelocityBufferRenderPass.cs
Assets/Scripts/Rendering/PostProcessing/VelocityBufferRendererFeature.cs
```

### Runtime Controller
```
Assets/Scripts/Rendering/PostProcessing/PostProcessController.cs
Assets/Scripts/Rendering/PostProcessing/PostProcessVolumeRegistration.cs
Assets/Scripts/Rendering/PostProcessing/MotionBlurTag.cs
```

### Compute Shaders & Effects
```
Assets/Resources/Shaders/PostProcessing/PostProcess.compute
Assets/Resources/Shaders/PostProcessing/MotionBlur.compute
Assets/Resources/Shaders/PostProcessing/Velocity.shader
```

### Editor Utilities
```
Assets/Scripts/Editor/PostProcessVolumeAssetCreator.cs    # HAD COMPILE ERROR — FIXED
Assets/Scripts/Editor/FixRenderer2DFeaturesUtility.cs     # DISABLED (#if false)
```

---

## 3. What Works

- **`dotnet build Assembly-CSharp.csproj`**: 0 errors, 377 warnings (analyzers only)
- **`PostProcessRendererFeature.cs`**: File exists, ASCII encoding, no BOM, class name matches file name
- **`.meta` file**: GUID `ee8b8d20783844e03b42386c92ab2a28` is correct and referenced in `Renderer2D.asset`
- **`.csproj`**: `PostProcessRendererFeature.cs` IS listed as a Compile item
- **Compute shaders**: `PostProcess.compute` + `MotionBlur.compute` exist in `Resources/Shaders/PostProcessing/`
- **Volume Components**: All 6 compile and derive from correct base class (`VolumeComponent, IPostProcessComponent`)
- **Namespace**: `Fodinae.Rendering.PostProcessing` — consistent across all files
- **No Assembly Definition files**: Project uses single `Assembly-CSharp.dll` (no `.asmdef` splitting)

---

## 4. What's Broken

### 4.1 Core Symptom

Unity Editor log: `No script asset for PostProcessRendererFeature`

This is NOT a compilation error. The assembly DLL loads fine. Unity's **ScriptAsset scanner** fails to create an internal MonoBehaviour/ScriptableObject registry entry for `PostProcessRendererFeature`. Without a ScriptAsset entry, the `Renderer2D.asset` reference to GUID `ee8b8d20783844e03b42386c92ab2a28` resolves to nothing → "missing RendererFeatures".

### 4.2 PostProcessVolumeAssetCreator.cs — FIXED

Was missing `using Fodinae.Rendering.PostProcessing;` → compiler errors CS0246 for all 6 VolumeComponent types. **Fixed by adding the using directive.**

### 4.3 Renderer2D.asset — MODIFIED

Current state has TWO features registered:
- `EffekseerURPRenderPassFeature` (fileID 11400001, GUID `4a67bf8a30b9b6d49a3b2fa63d95e9b0`) — original pkg feature
- `PostProcessRendererFeature` (fileID 11400002, GUID `ee8b8d20783844e03b42386c92ab2a28`) — broken

The YAML structure is valid. `m_RendererFeatureMap` was cleared to force Unity to rebuild internal mapping.

### 4.4 FodinaeEffekseerRenderPassFeature.cs — DUPLICATE/DEAD CODE

This class duplicates all of `PostProcessRendererFeature` + `PostProcessRenderPass` inside itself as nested classes, PLUS duplicates Effekseer rendering. It's never registered in any asset. It has Effekseer.Internal dependencies that may cause issues. **Likely should be deleted.**

---

## 5. Things Tried — ALL FAILED

| Attempt | Result |
|---------|--------|
| Swapping Renderer2D.asset GUID from pkg Effekseer to FodinaeEffekseer | Still "missing" |
| Splitting into two features (Effekseer + PostProcess separate) | Still "missing" |
| Clearing m_RendererFeatureMap | No effect |
| Fixing PostProcessVolumeAssetCreator compilation error | No effect |
| Adding renderPassEvent to ComputePostProcessPass | N/A — feature not even loaded |
| Killing Unity + rebuilding from CLI | No effect |
| Checking file encoding (ASCII, no BOM) | Correct |
| Checking .csproj inclusion | Listed |
| Checking Editor.log | No compilation errors, only "No script asset" |
| Checking Library/ScriptAssemblies | DLL exists, timestamp matches |

---

## 6. Root Cause Hypothesis

### Most Likely: Namespace Migration Corruption in Unity's Internal Metadata

The namespace migration from `Fodinae.Scripts.*` → `Fodinae.*` (July 30) manually edited `.prefab` and `.unity` YAML files, including `m_EditorClassIdentifier` fields. Unity maintains internal metadata maps that link type names → file IDs → GUIDs → ScriptAssets.

If the manual YAML edits introduced broken or stale `m_EditorClassIdentifier` entries in ANY asset that gets scanned during domain reload, Unity's type resolution pipeline can enter a broken state where it refuses to register new types from the same namespace/assembly — even though the C# compiles fine.

### Alternative Hypothesis: PostProcessRenderPass Reflection Failure

`PostProcessRendererFeature` references `PostProcessRenderPass` as a field type. `PostProcessRenderPass` has:
- Conditional `#if UNITY_6000_0_OR_NEWER` block using `RenderGraphModule`
- Non-conditional block using deprecated `RenderingUtils.ReAllocIfNeeded`
- `Dispose()` method in a separate `#if` block

Unity's ScriptAsset scanner may fail to fully reflect on `PostProcessRenderPass`, causing `PostProcessRendererFeature` (which references it) to also fail registration. **The scanner requires ALL referenced types to be resolvable for ALL their conditional compilation paths**, not just the current one.

### Alternative Hypothesis: Assembly-CSharp-Editor Not Rebuilt

`PostProcessVolumeAssetCreator.cs` (Editor assembly) had compile errors. When Editor assembly fails to compile, Unity's domain reload may enter a partial state where runtime assembly types fail ScriptAsset registration. Even after fixing the error, the internal state may be stuck.

---

## 7. STRICT HANDOFF INSTRUCTIONS

### CRITICAL: Do NOT edit .asset YAML by hand. Do NOT git checkout or revert. Do NOT delete files.

### Step 1 — ELIMINATE THE DUPLICATE

Delete `FodinaeEffekseerRenderPassFeature.cs` and its `.meta`. It's a duplicate that complicates resolution. Only keep `PostProcessRendererFeature.cs` + `PostProcessRenderPass.cs`.

### Step 2 — FLATTEN PostProcessRendererFeature

The class references `PostProcessRenderPass` as a field type. This dependency may be causing the ScriptAsset scanner to fail. Create a **self-contained** version that does NOT reference `PostProcessRenderPass` — inline the pass as a private nested class, or remove the reference entirely and just log "feature loaded".

Test: if Unity recognizes a minimal ScriptableRendererFeature that does nothing, the problem is the `PostProcessRenderPass` reference chain.

### Step 3 — CHECK FOR BROKEN m_EditorClassIdentifier IN ALL YAML ASSETS

Search ALL `.prefab`, `.unity`, and `.asset` files for stale `m_EditorClassIdentifier` values from the namespace migration. Any asset with a broken class identifier poisons Unity's type registry.

```bash
grep -r "m_EditorClassIdentifier:" Assets/ --include="*.prefab" --include="*.unity" --include="*.asset"
```

Look for entries that don't match current namespaces (e.g., `Fodinae.Scripts.*` instead of `Fodinae.*`).

### Step 4 — NUCLEAR OPTION: Delete Library Folder

If steps 1-3 don't work, the only known fix is:

1. Close Unity
2. Delete `Library/` folder entirely
3. Delete `Temp/` folder entirely
4. Reopen Unity — let it do a FULL reimport from scratch

This forces Unity to rebuild ALL internal metadata from raw assets, which clears any poisoned state from the namespace migration. **This is the only reliable fix for "code compiles but Unity can't find types."**

### Step 5 — REBUILD POST-PROCESS FROM SCRATCH

If even the nuclear option fails, the post-processing system was built on a poisoned foundation. The correct approach is:

1. Commit/push current state
2. Create a NEW namespace (e.g., `Fodinae.PostFx`) NOT in `Fodinae.Rendering`
3. Create ONE minimal `ScriptableRendererFeature` — 5 lines, no dependencies
4. Verify Unity Editor recognizes it
5. Build up from there

---

## 8. NEVER DO THESE (Learned from Both Sessions)

- **NEVER** edit `.prefab` or `.unity` YAML by hand. Use Unity Editor or Editor scripts.
- **NEVER** use raw text replacement to update GUIDs in `.asset` files.
- **NEVER** create 17 interdependent files at once. Build incrementally and verify each step.
- **NEVER** mix Effekseer rendering and custom post-processing in one feature.
- **NEVER** assume `dotnet build` success = Unity Editor will recognize types.
- **NEVER** ignore `m_EditorClassIdentifier` — it's Unity's internal type map, not a cosmetic field.

---

## 9. Follow-up Result (July 31, 2026)

The proposed remediation steps did not resolve the incident.

- Clearing the Unity cache and restarting the Unity Editor did not help.
- A dependency-free `PostProcessRendererFeature` still produces the same error.
- A new dependency-free `PostFxRendererFeature` in a new namespace and with a new GUID also produces the same error.
- Both classes are present in the generated `Assembly-CSharp.dll`; this does not make them available as Unity ScriptAssets.
- Manual YAML edits were not used during this follow-up.
- Attempts to attribute the issue to lingering Unity processes were inconclusive and did not resolve it. The earlier process diagnosis must not be treated as the root cause.

Current Unity output:

```text
[Effekseer] Graphics API "Metal" is not supported. Renderer is changed into Unity.

No script asset for PostProcessRendererFeature. Check that the definition is in a file of the same name and that it compiles properly.

No script asset for PostFxRendererFeature. Check that the definition is in a file of the same name and that it compiles properly.
```

The Metal message is a separate Effekseer/platform issue. The unresolved primary issue remains Unity's failure to create ScriptAssets for user-defined `ScriptableRendererFeature` types, including minimal classes with no project dependencies.

---

## 10. Final Resolution and Corrected Conclusions (July 31, 2026)

The statement at the end of section 9 reflects an intermediate state and is superseded by this section.

Unity later reported the following successful checks for the production feature:

```text
[GUIDTRUTH] renderer script Assets/Scripts/Rendering/PostProcessing/PostProcessRendererFeature.cs:
class=Fodinae.Rendering.PostProcessing.PostProcessRendererFeature
[GUIDTRUTH] renderer instance Fodinae.Rendering.PostProcessing.PostProcessRendererFeature:
created=YES
[PostProcess] PostProcessRendererFeature is attached to Assets/Settings/Renderer2D.asset.
[PostProcess] Removed diagnostic PostFxRendererFeature from Renderer2D and deleted its script asset.
```

The repair path used Unity's Editor API to validate/repair the serialized `m_Script` reference and attach the production feature. The diagnostic feature was removed after validation. This is evidence of the repaired state, but it does not prove which earlier operation originally invalidated the reference.

Correct operational conclusions:

- `dotnet build` is only a C# check. A renderer feature is accepted only after Unity reports a non-null `MonoScript.GetClass()` and can create the `ScriptableObject` instance.
- Killing Unity processes, clearing caches, restarting, waiting outside Play Mode and changing namespaces did not solve this incident and must not be offered as the diagnosis.
- Never repair `Renderer2D.asset` by text/GUID replacement. Use `SerializedObject`, `AssetDatabase`, `MonoScript.GetClass()` and `ScriptableObject.CreateInstance()` from an Editor utility.
- Keep exactly one production `PostProcessRendererFeature`; do not leave a second diagnostic feature attached.
- A clean Unity domain reload and shader import are part of verification. Check the active project log, which may be `Logs/Editor.log` rather than only `~/Library/Logs/Unity/Editor.log`.

## 11. Lighting Follow-up (July 31, 2026)

The terrain-lighting regressions were separate from the ScriptAsset incident. The flat cell DDA produced discrete circular passes and effectively unbounded rectangular tunnels, while CPU texture-alpha sampling could not reproduce the terrain shader's final silhouette.

The replacement path is:

```text
Terrain OcclusionCoverage pass
    -> cached 8 texel/cell coverage using final shader shape and PNG alpha
    -> cached jump-flood SDF
    -> height-aware SDF cone tracing
    -> AO + tiled direct lights + edge-aware reconstruction
```

Coverage/SDF rebuild only when the visible terrain region, map revision, texture-atlas revision or lighting settings change. Emissive cells are clustered and each output tile receives a local light index list, avoiding `every pixel × every source`. Terrain alpha remains continuous in the coverage buffer, so translucent details produce proportional shadow coverage rather than a solid square.

The first projected-shadow defaults were rejected after runtime screenshots. Changing virtual heights only turned giant black tunnels into shorter but still broken black fragments around terrain. Temporarily disabling projected shadows was also rejected as an incomplete fix. Replacing the darkest-single-sample `min()` with integrated optical thickness removed that unstable binary decision: height now controls shadow length, the SDF cone controls penumbra, and PNG alpha/density control transmission.

### 11.1 Root cause of shadows originating in empty space (August 1, 2026)

The decisive remaining defect was not shadow tuning. `OcclusionCoverage` is rasterized into a RenderTexture with `GL.GetGPUProjectionMatrix(..., renderIntoTexture: true)`. On Metal this texture uses the opposite Y orientation from the compute shader's world-ordered lightmap. Coverage and its derived SDF were therefore sampled at vertically mirrored world positions, producing convincing-looking shadows from empty cells while the real blockers were elsewhere.

The fix keeps the lightmap in world order and applies a platform-specific transform only when sampling rasterized coverage/SDF:

```text
_OcclusionYFlip = SystemInfo.graphicsUVStartsAtTop
world/grid position -> ToOcclusionGrid -> coverage or SDF sample
```

Runtime visual verification confirmed that blocker silhouettes and projected shadows align after this change. If shadows ever originate away from geometry again, inspect `Debug View = Occlusion` before changing heights, strength, softness, AO or density. Spatial mismatch cannot be repaired by artistic coefficients.

Ultra lighting also no longer has the old 512-pixel whole-region cap. It now targets 8 texels per cell up to 2048, preventing the expanded viewport/safe-border mesh from collapsing shadow reconstruction to roughly four texels per cell.

## 12. World-space UI entered post-processing again (August 1, 2026)

The renderer feature and object layers were correct, but `PostProcessController` was only registered through `RegisterComponentOnNewGameObject<T>` and was never resolved. VContainer component creation is lazy: the compute post-process continued to run from the renderer feature, while no controller existed to remove the `UI` layer from Main Camera or create the post-process-free `WorldUICamera` overlay. Robot nicknames and clan icons were consequently rendered into the base color before bloom/eigengrau/color grading.

The fix explicitly resolves `PostProcessController` in `GameLifetimeScope.BuildCallback`. The controller also verifies the camera-separation invariant and repairs it only when the Main Camera, culling masks, overlay mode, post-processing flag or camera stack are actually invalid. Do not treat registration alone as initialization for critical runtime `MonoBehaviour` services.

### 12.1 Intermittent red capsules around moving robots

After restoring the UI camera, movement exposed a separate motion-blur defect. The velocity material depended on SpriteRenderer's implicit `_MainTex`; dynamic/atlas sprites could resolve to the white fallback on Metal, so the transparent rectangle around a robot became a fully moving mask. The full-screen composite then sampled scene color along that velocity and produced large red capsules that appeared only while robots moved.

The pass now binds each renderer's real `sprite.texture`, sets the camera GPU matrices explicitly, rejects teleport/non-finite velocity, limits displacement to physical screen pixels and accumulates only samples carrying compatible robot velocity. This keeps motion blur inside the actual opaque robot silhouette and prevents it from dragging the illuminated background into the robot mask.

### 12.2 Intermittent acid colors and HDR/SDR switching

The motion-blur repair did not explain the global red/green/magenta distortion. Two different settings had been conflated: URP internal HDR rendering and HDR display output. The broken state had internal `UniversalRenderPipelineAsset.supportsHDR` disabled while `PlayerSettings.useHDRDisplay` remained enabled. Unity repeatedly logged that HDR output was being disabled and switched the display path at runtime, producing alternating presentation transforms.

The stable configuration is intentionally asymmetric:

- internal URP HDR rendering is enabled so lighting and bloom retain headroom;
- HDR display/10-bit output is disabled (`allowHDRDisplaySupport = false`, `useHDRDisplay = false`);
- the custom compute composite performs hue-preserving HDR highlight compression because built-in URP post-processing is disabled for camera-stack/UI separation.

The first ACES attempt removed clipping but lifted dark/mid values and made working AO/shadows appear weaker. The final mapping leaves all RGB values at or below `1.0` exactly unchanged and normalizes only HDR highlights with one shared RGB multiplier. This preserves occlusion darkness and emissive hue simultaneously. `SdrOutputEnforcer` applies and persists the correct HDR-render/SDR-display split through Unity Editor APIs; do not repair these assets through YAML text replacement.

### 12.3 Server-authoritative cell emission

`RedRock` had been incorrectly included in a client-side legacy glowing-cell fallback. The attempted hard deny for several rock types was also the wrong ownership model: cell emission is server data. `TerrariaLightingEngine` now creates a light only when `CellConfigurationPacket.Properties` contains `CellConfigProperties.Glowing`; it has no client allow-list, deny-list, or name-based fallback. `DummyConnection.CreateTestCellConfigurations()` mirrors server responsibility in offline mode and marks living crystals, crystals, building/artificial blocks, boxes, lava/magma and every current acid/slime variant as glowing, while ordinary rocks, sands and boulders remain non-emissive. All solid types still participate independently in terrain coverage, SDF, shadows and AO.

### 12.4 Movement-triggered cache stalls

Moving by one cell rebuilt the complete terrain mesh/cache. Static lighting also watched the global `MapStorage.Revision`, so streamed `MapRegionPacket` cells outside the viewport repeatedly rebuilt coverage, the jump-flood SDF and the static lightmap. This caused large main-thread/GPU stalls during ordinary movement.

The terrain region is now world-anchored in eight-cell increments with dedicated movement padding. Existing cached cells are shifted through `TerrainCellCache.ScrollAndFill`, and only newly exposed strips are loaded before mesh reconstruction. Cell changes invalidate terrain and static lighting only when they intersect the current cached regions; entering a new region, changing worlds or loading a visible cell texture still performs a full invalidation. Do not restore per-cell camera rebuilds or global-revision viewport invalidation.

A second movement artifact was caused by `BuildWorldAnchoredLightClusters` dynamically increasing cluster size until the current region fit the light limit. Crossing the limit changed every cluster position/color at once, so lighting visibly jumped even though the world had not changed. Clusters are now permanently aligned to a 2×2 world grid. If the configured light limit is exceeded, only the farthest cached-region clusters are omitted using deterministic distance/world-key ordering; existing clusters are never globally regrouped during camera movement.

Continuous camera zoom had a separate allocation storm: every small `orthographicSize` change produced a new exact terrain dimension, reallocating all CPU terrain buffers and lighting render textures before rebuilding mesh, coverage and SDF. Cached dimensions now grow in 32-cell quanta only when required and shrink once after zoom has remained stable for 0.4 seconds. Smooth zoom therefore reuses existing capacity instead of rebuilding resources every frame.

The receiver self-shadow exception also incorrectly skipped coverage for an entire connected opaque mass. A ray starting on one block could therefore traverse every touching block without accumulating optical depth, making walls appear not to absorb light. Self-skip is now limited to samples whose integer grid coordinate matches the original receiver cell; adjacent blocks immediately resume continuous alpha/optical-depth absorption.
