---
name: fmod-sync
description: Синхронизация и компиляция звуковых банков FMOD Studio в StreamingAssets
---

# FMOD Bank Sync Skill (Синхронизация звука)

Используйте этот навык при изменении аудио-событий или банков в проекте FMOD Studio (`FodinaeAudio`).

## Порядок действий
1. Запустите автоматическую утилиту `FmodBankBuilder.cs` через меню Unity или из билдера.
2. Проверьте, что собраны аудио-банки:
   - `Master.bank`
   - `Master.strings.bank`
   - `SFX.bank`
3. Убедитесь, что скомпилированные `.bank` файлы скопированы в `Assets/StreamingAssets/Audio/`.
4. Проверьте работоспособность `AudioSystem.cs` при воспроизведении 3D-звука и событий SFX.
