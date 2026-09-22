---
name: critical-invariants
description: >-
  Kern engine critical invariants: coordinate system, UI Toolkit positioning, RenderTexture blit,
  LifetimeScope/scene structure, VolumeProfile editor API, camera pixel-grid alignment, performance
  analysis rules, profiler interpretation, and the no-fallback policy. Use when touching coordinates,
  WorldToScreen, UI element anchoring, RenderTexture, LifetimeScope, VolumeProfile, camera, FPS
  analysis, Profiler markers, or any code near an "invariant" comment. Triggers on: CoordinateUtils,
  MapManager.WorldHeight, RuntimePanelUtils, CameraTransformWorldToPanel, WorldToLocal, PositionWriteEpsilon,
  CameraPixelGridAligner, LifetimeScope, RegisterComponent, VolumeProfile, AssetDatabase.AddObjectToAsset,
  EditorLoop, VSync, fallback, paper white, FPS, frame time.
---

# Critical engine invariants

## Coordinates

- Server coordinates have their origin at the top-left with Y pointing down.
- Perform all transformations exclusively via `CoordinateUtils` with `MapManager.WorldHeight`.

## UI Toolkit

- Single style tree via `KernTheme.tss`; static structure lives in UXML.
- Toggle visibility with the `is-hidden` class via `UIState`.
- Screen coordinates via `RuntimePanelUtils.ScreenToPanel`.
- An element anchored to a world point must be positioned exactly like `WorldLabels.LateTick`: `RuntimePanelUtils.CameraTransformWorldToPanel` for the point, then `WorldToLocal` of the container.
- Rolling your own `WorldToScreenPoint` + `ScreenToPanel` is **FORBIDDEN**: `left`/`top` are relative to the parent, not the panel.
- Position and size styles are written only when the change exceeds half a pixel (`PositionWriteEpsilon`). Writing unconditionally every frame shakes the element and keeps the panel style-dirty.

## RenderTexture / blit

- Blitting into a `RenderTexture` for a UI element must use `Blend Off`. With blending enabled, each frame accumulates on top of the previous contents — nobody clears the texture.

## Scene / LifetimeScope

- Every scene has exactly one root — its own `LifetimeScope`; all authored objects live beneath it.
- Objects placed next to the scope are invisible to the container.
- Guarded by `ProductionSceneContractValidator.ValidateSingleRoot`, fixable via `Kern/Architecture/Move Scene Roots Under Composition Root`.

## VolumeProfile

- `VolumeProfile.Add<T>()` creates the component in memory only; editor code must add it via `AssetDatabase.AddObjectToAsset()` before saving.

## Camera

- The camera moves with smoothing and snaps to the pixel grid (`CameraPixelGridAligner.SnapPosition`).

## Performance and diagnostics

- Do not mask defects by clearing Unity cache, recompiling, applying FPS caps, frame skipping, or throttling.
- Only change hot paths after reproducing the issue or strictly confirming the root cause.
- If performance worsened or did not improve after the agent's own change, the agent **MUST** explain the reason from the change itself: exactly what it added per frame (dispatch count and threads, marching steps, texture reads/writes, sizes of new textures and buffers) and why there was no gain. Shifting root-cause analysis onto the user is **FORBIDDEN**.
- Before changing a GPU hot path — calculate its per-frame cost and compare it with what it replaces.

## Lighting (update rate)

- Lighting is **FORBIDDEN** from being rate-limited (timer, Hz cap, solve-frequency setting) — for neither light sources nor geometry.
- Dynamic lights (robot light sources) must not be tied to entering a new cell.
- Light is recalculated every frame together with the robot's smooth movement. Expensive passes must be made cheaper, not skipped.

## Profiler / measurements

- VSync **NEVER** explains performance. Forbidden to mention as a cause of low FPS, a "ceiling", "waiting for display", a measurement caveat, a recommendation, a code comment, or a line in a report.
- "Editor overhead" (`EditorLoop`, Scene view GPU, `PlayerConnection` socket) is **NEVER** an explanation for low in-game FPS. These are measurement artifacts of Play Mode inside the Editor — look for the cause in `Scripts`, `Rendering`, `Physics`, `GarbageCollector`.

## Fallbacks

- Fallbacks are **FORBIDDEN** everywhere, always: no fallback values, simplified paths, or silent substitutions when primary data is absent. No texel — black. No data — error or empty. Masking missing data with a fallback is a bug, not resilience.
