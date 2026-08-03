- [x] TODO: Refactor GIF decoding — Неоптимальный legacy-декодер `MgGifDecoder/Image.cs` (с unsafe блоками и аллокациями кастомных текстур) полностью удалён из проекта. Декодирование анимированных GIF и WebP переведено на нативный Sprite Sheet в единую текстуру-атлас с получением кадров через `AnimationContainerDecoder.DecodeGif()` без CPU spikes и лишних GC аллокаций.
- [x] Настройки аудио и обработка смены аудио-устройств — Реализован UI настроек громкости для 6 шин (Master, SFX, Music, Voice, Ambience, UI) в меню паузы, подписка на системные изменения вывода аудиодрайвера `AudioSettings.OnAudioConfigurationChanged` и авто-рессет/переинициализация FMOD/Unity бэкенда `AudioSystem.ResetBackend()` при смене устройства по умолчанию (`Default audio device was changed`).
- [x] Аудит `Assets/Editor/` — `ExportSprites.cs` удалён (мёртвая разовая утилита). Оставлены: `BuildScript.cs` (билды), `CsProjFix.cs` (csproj), `FmodBankBuilder.cs` (активен: синк банков FodinaeAudio → StreamingAssets, fallback в рантайме), `MapbConverter.cs` (единственный генератор baked-мира). ⚠ Найдено: `StreamingAssets/WorldMaps/` содержит 74МБ `pallada_cells.zip`, который НИКТО не распаковывает — рантайм ищет `pallada_cells.mapb`. Отдельная задача: либо распаковать zip→mapb репо, либо удалить zip.
- ПЕРЕВЕСТИ НА СОВРЕМЕННЫЙ СИ ШАРП
- тексты написать (без иишки)
- компонентно-солид mvp рефакторинг
- тонна блять синхронных (серийных) процессов и гонок определений
- нет экрана загрузки
- [x] добавить пимпочку справа снизу которое показывает состояние загрузки ассетов и туда вынести и версию билда и фпс и пинг и т.п.
- режим предпросмотра сделать
- [ ] Оптимизация текстур и лоадера — `Assets/Textures/loader_new.png` весит 3.57 МБ в сыром PNG. Необходима оптимизация размера (Crunch Compression в Texture Importer / PNG сжатие) и переименование в `loader.png` без временного суффикса `_new`.
- [ ] Очистка и структурирование `Assets/Textures/` — Переместить графические ассеты из корня директории `Assets/Textures/` (`perspective.png`, `programmator.png`, `skills.png`, `transit.png`) по специализированным подпапкам и настроить единые пресеты TextureImporterSettings.
- [ ] Очистка неиспользуемых 2D шаблонов сцен в `Assets/Settings/` — Удалить / перенести `Lit2DSceneTemplate.scenetemplate` (3.94 МБ) и `URP2DSceneTemplate.unity`.
- [ ] Оптимизация 2D профилей качества в `ProjectSettings/QualitySettings.asset` — Сократить 6 дефолтных 3D профилей (Very Low, High, Ultra с тенью/LOD/Reflection Probes) до 2 специализированных 2D профилей.
- [ ] Разделение 2D слоев и матрицы коллизий в `TagManager.asset` & `Physics2DSettings.asset` — Настроить отдельные слои для `Player`, `Robot`, `Pack`, `Terrain` и оптимизировать матрицу коллизий. (Многопоточная физика 2D `useMultithreading: 1` уже включена).
- [ ] Очистка неиспользуемых тем UI Toolkit в `Assets/UI Toolkit/` — Удалить `UnityDefaultRuntimeTheme.tss` и `UnityThemes/`.
- [ ] Исправление StyleCop предупреждений в C# коде (`SA1513`, `SA1407`, `SA1503` и др.), выявленных новой полной проверкой линтера.

## Lighting / HDR rendering follow-up

- [ ] Разделить diffuse lighting и emission в финальной композиции: `albedo * (ambient + direct + bounce) + emission`; emission не должен усиливать базовую terrain-текстуру.
- [ ] Откалибровать физические единицы radiance, extinction и emission для блоков и динамических источников; убрать случайные усилители и скрытые коэффициенты.
- [ ] Довести display-referred HDR pipeline до production-уровня: проверить HDR camera target, linear workflow, output transform и отсутствие обхода post-process.
- [ ] Проверить ACES tone mapping на реальных HDR-сценах: dark preservation, highlight shoulder, насыщенные красные/синие источники и отсутствие цветового клиппинга.
- [ ] Добавить в Unity Volume Profile рабочие настройки tone mapping: enable, exposure, white point и при необходимости shoulder/contrast; проверить фактические значения в runtime diagnostics.
- [ ] Разделить художественную яркость texture/albedo, diffuse irradiance, emission и bloom threshold; художническая текстура должна сохранять вид при нейтральном свете.
- [ ] Сделать отдельные визуальные тест-сцены/кейсы: голый блок, цветной источник, HDR emission, соседний albedo, 1–3 клетки extinction, AO и bounce.
- [ ] Проверить производительность HDR/post-process и lighting в Unity Profiler и GPU timing: стабильный frame time, без периодических полных mip/cascade rebuild и CPU↔GPU sync.
- [ ] Удалить диагностический `Enable Final Lighting Clamp` после завершения калибровки или оставить только как явно помеченный debug-инструмент.

## Cheap modern graphics track

Порядок приоритета: сначала снизить стоимость lighting без изменения физического результата, затем добавить визуальные эффекты поверх уже рассчитанного результата.

- [ ] Edge-aware upsampling для lighting: считать radiance/AO в пониженном разрешении и восстанавливать границы по occupancy/material edges. Цель — снизить стоимость cascade и AO без швов на блоках.
- [ ] Half-resolution diffuse bounce: уменьшить bounce до 8 направлений × 4 шагов и поднимать результат edge-aware фильтром. Проверить визуальное отличие и GPU timing.
- [ ] Разделить static и dynamic lighting: кэшировать static terrain radiance, а перемещаемые Robot sources считать в отдельном малом dynamic field. Движение источника не должно пересобирать весь static cascade.
- [ ] Selective emission bloom: использовать отдельную emission mask для bloom, сохранив terrain/albedo без дополнительного пересвета. Работать на half-resolution.
- [ ] Surface-gradient lighting: получить дешёвый локальный surface gradient из occupancy/albedo для ощущения объёма без normal map и дополнительных текстур. Не менять физическое поглощение.
- [x] Автоматические surface normals из occupancy: normal response вычисляется в `WorldLighting.compute` без отдельного MRT/NormalField; влияет только на direct и diffuse bounce.
- [ ] Стабильный blue-noise dithering для низкоразрешённых AO/soft-shadow границ. Не использовать temporal noise и не менять детерминированный Contact AO.
- [ ] Профилировать Ultra отдельно: проверить стоимость `LightingPixelsPerCell=8`, field до `2048`, cascade steps `64` и diffuse bounce перед изменением значений профиля.

## Modern GPU technology candidates (audit before implementation)

- [ ] Проверить существующий SDF/JFA: в активных shader-pass отдельного JFA/SDF нет; комментарий в `TerrainRenderer` упоминает старый SDF и может быть устаревшим. Не добавлять новый distance-field проход, пока GPU timing не покажет, что он дешевле текущих cascade/AO.
- [ ] GPU distance field через Jump Flooding Algorithm — кандидат для soft contact shadows, penumbra и source falloff, но только как geometry-cache pass при изменении occupancy; не пересчитывать каждый кадр и не заменять физический extinction без тестов.
- [ ] Dynamic-light field: проверить, можно ли считать движущиеся источники в малом отдельном поле и композить со static radiance без повторного полного cascade solve.
- [ ] Tiled/clustered light culling — добавить только при подтверждённом большом числе dynamic sources; для нескольких Robot sources отдельный culling может быть дороже обычного buffer.
- [ ] Async compute — проверить через Unity Render Graph Viewer и GPU timestamps; не считать автоматически оптимизацией, потому что неподдерживаемые async queues выполняются на graphics queue.
- [ ] Render Graph resource aliasing — проверить lifetime lighting/bloom textures и убрать лишние device-memory copies, не меняя математический результат lighting.
- [ ] Не внедрять пока temporal/neural upscaling, mesh shaders, ray tracing и Metal tile shaders: для 2D voxel-границ есть риск ghosting/платформенной зависимости при сомнительной выгоде.
