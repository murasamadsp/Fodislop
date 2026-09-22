---
name: fmod-sync
description: Synchronize and compile FMOD Studio banks into StreamingAssets
---

# FMOD Studio Bank Build and Audio Backend Pipeline

This skill covers the FMOD Studio audio system (`KernAudio`), binary banks, and
CLI compilation.

## 1. Automatic bank compilation through FmodBankBuilder

The editor script `FmodBankBuilder.cs` compiles `KernAudio/KernAudio.fspro`
into `.bank` binaries:

* **macOS CLI**: `/Applications/FMOD Studio.app/Contents/MacOS/fmodstudiocl`
* **Windows CLI**: `C:\Program Files (x86)\FMOD SoundSystem\FMOD Studio\fmodstudiocl.exe`

Compiler invocation:

```bash
"/Applications/FMOD Studio.app/Contents/MacOS/fmodstudiocl" build "/path/to/KernAudio/KernAudio.fspro"
```

## 2. Build output and target paths

Compiled banks must be copied to `Assets/StreamingAssets/Audio/`:

- `Master.bank` — top-level output buses and limiters
- `Master.strings.bank` — FMOD event GUID and name table
- `SFX.bank` — spatial 3D sounds, ambience, and UI effects

## 3. Backend error handling

When calling `AudioSystem.cs`:

* For output-device changes, listen for FMOD system events and call `AudioSystem.Instance.ResetBackend()`.
* Output buses: `SFXDefault`, `UIDefault`, `MusicDefault`.
