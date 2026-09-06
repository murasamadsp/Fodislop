# Plan: Client Config Cleanup

## Context
User provided a numbered critique of `client_config.json` settings. This plan translates that critique into concrete code changes.

## Issues and Actions

### P0 — Blockers (must fix before any release)

| # | Issue | Root Cause | Fix |
|---|-------|------------|-----|
| 1 | `MasterVolume = 0.0` mutes all audio | Saved config has master at zero while children are 1.0 | Add clamp in `ClientConfigManager.Save()` or `AudioSettings` validator: if `MasterVolume == 0`, either reject or reset to `DefaultMasterVolume`. Also ensure UI slider cannot visually reach 0 without explicit mute. |
| 2 | `ResolutionWidth = 3420`, `ResolutionHeight = 1890` | Non-standard resolution saved; likely Retina scaling bug or manual edit | In `DisplaySettings.Validate()`: accept only native resolutions or `0x0`. Reject or clamp arbitrary values. Reset to `0,0` on invalid. |
| 3 | `Gamma = 1.7999999523162842` | `JsonUtility` writes raw float; no sanitization | Add `SettingRange` clamp in `DisplaySettings.Gamma` setter or validator: round to `DefaultGamma` if within epsilon. Replace float serialization with sanitized write in `ClientConfigRepository.Save()`. |

### P1 — High Priority

| # | Issue | Root Cause | Fix |
|---|-------|------------|-----|
| 4 | `PaperWhiteNits = 200` at HDR | HDR UI uses SDR paper-white; waste of dynamic range | Change `DefaultPaperWhite` to `400` for HDR mode. In `DisplayManager.SetHDREnabled`, auto-adjust paper white based on display capability. |
| 5 | VSync/Refresh/TargetFrameRate contradiction | `VSync=false`, `RefreshRate=60`, `TargetFrameRate=-1` means uncapped FPS on 60Hz | Make `VSync` and `TargetFrameRate` mutually consistent in UI: if VSync on, set TargetFrameRate = RefreshRate; if off, allow uncapped or user-defined cap. |
| 6 | `SolidExtinctionRgb = (1.2, 1.1, 1.0)` | Physics-correct but RGB sliders in UI clamp to `[0,1]`; user sees "1.0" but config stores >1 | Move extinction color to HDR-only inspector or store as linear float array. In `PauseMenuAdvancedTabBuilder`, allow color picker >1.0 for HDR values. Remove RGB clamping in `ApplyLightingColor`. |
| 13 | Float garbage in colors | `JsonUtility.ToJson` writes full float precision | In `ClientConfigRepository.Save()`, round floats to 4 decimal places before serialization. Or use custom `JsonConverter` for `Color`. |

### P2 — Medium Priority

| # | Issue | Root Cause | Fix |
|---|-------|------------|-----|
| 7 | `SchemaVersion = 28` | 28 migrations accumulated | After migration #28, reset schema: default config has no migration path. Old configs get replaced, not migrated. Remove `ClientConfigMigration` history or archive it. |
| 8 | `ControlScheme = 0` | Magic number without names | Change `ControlScheme` from `int` to `string` enum: `"wasd"`, `"arrows"`, `"gamepad"`. Add `[SettingLabel]` with localized names. |
| 9 | `PixelSampling = 0` | Enum value without human-readable name | Add `[SettingLabel]` with enum description. Expose names in settings UI, not just index. |
| 10 | `DynamicLightUpdatesPerSecond = 20` | 50ms delay causes visible lag | Change default to `60` (16ms). Clamp min to `30` in `SettingRange`. Remove hardcoded 20 from `WorldLightingSettings`. |
| 11 | `FilmGrainEnabled = true` + `LocalContrastEnabled = true` | Combined noise + contrast = soap effect on planet | Leave as user preference but add note in tooltip: "May reduce planetary surface clarity". No code change needed unless AGENTS.md mandates removal. |
| 12 | `TemporalEnabled = true` on 2D UI | Temporal accumulation on UI Toolkit causes ghosting | Disable temporal effects when UI is open, or exclude UI layer from temporal pass in `PostProcessRendererFeature`. |
| 14 | `TransmittanceDebugDistanceCells` in user config | Debug-only field exposed to players | Move to `#if UNITY_EDITOR || UNITY_ENABLE_CHECKS` in `PauseMenuAdvancedTabBuilder` (already done) and `WorldLightingSettings`. Remove from `ClientConfig` persistence via `[NonSerialized]` or editor-only settings section. |

### P3 — Low / Out of Scope

| # | Issue | Decision |
|---|-------|----------|
| 15 | `ReducePhotosensitivity` single flag | Expand to `ReducedMotion`, `ReducedParticles`, `ReducedFlash` toggles. Out of scope for config cleanup. |
| 16 | `GammaMin = 1.8`, `GammaMax = 2.6` | Keep as-is; user can adjust. No action. |
| 17 | HDR vs SDR paper white | Handled in P1 #4. |

## Implementation Order

1. **P0 #1**: Fix `MasterVolume` validation and clamp in `AudioSettings` / `ClientConfigManager`
2. **P0 #2**: Add resolution validation in `DisplaySettings` / `ClientConfigValidator`
3. **P0 #3**: Sanitize float serialization in `ClientConfigRepository.Save()`
4. **P1 #4**: HDR paper-white auto-adjust in `DisplayManager`
5. **P1 #5**: VSync/TargetFrameRate consistency in `DisplaySettings`
6. **P1 #6**: Remove RGB clamp in `PauseMenuAdvancedTabBuilder` color controls
7. **P1 #13**: Round floats in `ClientConfigRepository.Save()`
8. **P2 #7**: Schema reset after migration #28
9. **P2 #8**: `ControlScheme` enum string
10. **P2 #9**: `PixelSamplingMode` labels
11. **P2 #10**: `DynamicLightUpdatesPerSecond` default to 60
12. **P2 #12**: Temporal UI ghosting fix in `PostProcessRendererFeature`
13. **P2 #14**: Move debug fields to editor-only

## Validation

- `dotnet run --project tools/Fodinae.SettingsProbe` must pass after changes
- `ClientConfigMigrationTests` must pass after schema reset
- Manual test: load corrupted `client_config.json` with `MasterVolume=0`, invalid resolution, gamma garbage → game starts with defaults
- Manual test: HDR display shows correct paper-white brightness
- Manual test: VSync toggle updates TargetFrameRate consistently

## Open Questions

- Should `MasterVolume=0` be rejected entirely or auto-corrected to default? **Recommended: auto-correct with log warning.**
- Should schema reset delete old backups? **Recommended: keep `.backup` files for one version, then clean.**
- Should `ControlScheme` be string or keep int with `[SettingLabel]` map? **Recommended: string enum for clarity.**
