#nullable enable

using System;
using System.IO;
using Kern.Rendering;
using Kern.Rendering.PostProcessing;
using UnityEngine;

namespace Kern.Core;

// Путь конфига на старте: чистая установка или обычный запуск.
//
// Старые схемы накладываются на текущие defaults, чтобы отсутствующие поля
// получили актуальные значения, а существующие пользовательские настройки
// сохранились. Известные изменения формата применяются последовательно.
// Сверка стандартных пресетов — текущее поведение, не миграция:
// выполняется при каждой загрузке.
//
// Отделён от ClientConfigManager, чтобы установку, миграцию и сброс можно было
// проверить на временной папке, без MonoBehaviour и persistentDataPath.
internal sealed class ClientConfigLoader
{
    private const int RimSourceSchemaVersion = 31;
    private const int RimSchemaVersion = 32;
    private const int DistortionStyleSchemaVersion = 33;
    private const int PresetPairSchemaVersion = 34;
    private const int BloomStyleSchemaVersion = 35;
    private const int KeyBindingsSchemaVersion = 36;
    private const int AggressionKeySchemaVersion = 37;
    private const int HdrSwitchPendingSchemaVersion = 38;
    private const int PeakBrightnessFromDisplaySchemaVersion = 39;
    private const int TerrainNamesSchemaVersion = 40;

    private readonly ClientConfigRepository _repository;
    private readonly ClientConfigValidator _validator;
    private readonly GraphicsQualityProfile _graphicsQualityProfile;

    public ClientConfigLoader(ClientConfigRepository repository, GraphicsQualityProfile graphicsQualityProfile)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _graphicsQualityProfile = graphicsQualityProfile ??
            throw new ArgumentNullException(nameof(graphicsQualityProfile));
        _validator = new ClientConfigValidator(graphicsQualityProfile);
    }

    public enum Outcome
    {
        CreatedDefaults,
        Loaded,
        Migrated,
        ResetToDefaults,
    }

    public readonly record struct Result(ClientConfig Config, Outcome Outcome, int SourceSchemaVersion);

    // Файл, который нельзя прочитать или провалидировать, не переписывается:
    // исключение уходит наверх, диск не тронут.
    public Result LoadOrCreate()
    {
        if (!_repository.Exists)
        {
            ClientConfig defaults = ClientConfigDefaults.Create(_graphicsQualityProfile);
            _validator.Validate(defaults);
            _repository.Save(defaults, _repository.BackupPath);
            return new Result(defaults, Outcome.CreatedDefaults, ClientConfig.CurrentSchemaVersion);
        }

        ClientConfigRepository.LoadedConfig loaded = _repository.Load();
        int sourceSchemaVersion = loaded.Config.SchemaVersion;
        if (sourceSchemaVersion < 1)
        {
            throw new InvalidDataException(
                $"Client config schema {sourceSchemaVersion} is invalid; refusing to overwrite it.");
        }

        if (sourceSchemaVersion > ClientConfig.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Client config schema {sourceSchemaVersion} is newer than supported " +
                $"schema {ClientConfig.CurrentSchemaVersion}; refusing to overwrite it.");
        }

        if (sourceSchemaVersion != ClientConfig.CurrentSchemaVersion)
        {
            ClientConfig migrated = ClientConfigDefaults.Create(_graphicsQualityProfile);
            JsonUtility.FromJsonOverwrite(loaded.Json, migrated);
            ApplyMigrations(migrated, loaded.Json, sourceSchemaVersion);
            ReconcileStandardPreset(migrated);
            _validator.Validate(migrated);
            _repository.Save(migrated, _repository.BackupPath);
            return new Result(migrated, Outcome.Migrated, sourceSchemaVersion);
        }

        GraphicsPreset presetBefore = loaded.Config.GraphicsPreset;
        GraphicsQualitySettings qualityBefore = loaded.Config.GraphicsQualitySettings;

        ReconcileStandardPreset(loaded.Config);
        _validator.Validate(loaded.Config);
        if (loaded.Config.GraphicsPreset != presetBefore ||
            loaded.Config.GraphicsQualitySettings != qualityBefore)
        {
            _repository.Save(loaded.Config, _repository.BackupPath);
        }

        return new Result(loaded.Config, Outcome.Loaded, sourceSchemaVersion);
    }

    private static void MigrateSchema31To32(ClientConfig config)
    {
        // Schema 31 predates TerrainSettings.EnableRim (then EnableReliefRim).
        // The field was introduced enabled, so migration must make that intent explicit
        // instead of accepting JsonUtility's CLR default for a missing bool.
        config.Terrain.EnableRim = true;
        config.SchemaVersion = RimSchemaVersion;
    }

    private static void MigrateSchema32To33(ClientConfig config, string sourceJson)
    {
        // Keep an explicitly saved style in early schema-32 configs.
        if (sourceJson.IndexOf("\"DistortionStyle\"", StringComparison.Ordinal) < 0)
        {
            config.Terrain.DistortionStyle = TerrainDistortionStyle.Organic;
        }

        config.SchemaVersion = DistortionStyleSchemaVersion;
    }

    private static void MigrateSchema33To34(ClientConfig config)
    {
        // Шесть ступеней (VeryLow…Ultra) и Custom схлопнуты в две: «Стандарт» и
        // «Overdrive». Старое число ступени не читается — маппить шесть значений
        // в две пары нечего, и все они переводятся в «Overdrive». Снимок
        // настроек следом выравнивает ReconcileStandardPreset.
        config.GraphicsPreset = GraphicsPreset.Overdrive;
        config.SchemaVersion = PresetPairSchemaVersion;
    }

    private static void MigrateSchema34To35(ClientConfig config)
    {
        // Схема 35 вводила стиль свечения. Стиль удалён, шаг только поднимает
        // номер, чтобы цепочка миграций оставалась непрерывной.
        config.SchemaVersion = BloomStyleSchemaVersion;
    }

    private static void ApplyMigrations(ClientConfig config, string sourceJson, int sourceSchemaVersion)
    {
        int schema = sourceSchemaVersion;

        // Before schema 19 visual controls were retired. Schemas 19–21 stored
        // them flat in the root and need an explicit mapping into current
        // sections before the remaining version steps run.
        if (schema < 22)
        {
            if (schema < 19)
            {
                config.Terrain = new TerrainSettings();
                config.Effects = new EffectSettings();
            }
            else
            {
                MigrateFlatVisualsToSections(config, sourceJson);
            }

            schema = 22;
        }

        if (schema < 26)
        {
            config.Display.PixelSampling = PixelSamplingMode.SmoothFiltered;
            schema = 26;
        }

        if (schema < 28)
        {
            if (Mathf.Approximately(config.Interface.UIScale, 1f) &&
                UIScaleUtility.IsRetinaOrHighDpi)
            {
                config.Interface.UIScale = UIScaleUtility.RetinaDefaultScale;
            }

            schema = 28;
        }

        // Schemas 29 and 30 removed SDR gamma/tone mapping. Those fields no
        // longer exist in the runtime model and are ignored by JsonUtility.
        if (schema < 31)
        {
            config.Display.HDRSwitchPending = false;
            schema = 31;
        }

        if (schema == RimSourceSchemaVersion)
        {
            MigrateSchema31To32(config);
            schema = RimSchemaVersion;
        }
        else if (schema < RimSourceSchemaVersion)
        {
            // Older terrain schemas receive the authored default for this new
            // boolean instead of JsonUtility's implicit false value.
            config.Terrain.EnableRim = true;
            schema = RimSchemaVersion;
        }

        if (schema == RimSchemaVersion)
        {
            MigrateSchema32To33(config, sourceJson);
            schema = DistortionStyleSchemaVersion;
        }

        if (schema == DistortionStyleSchemaVersion)
        {
            MigrateSchema33To34(config);
            schema = PresetPairSchemaVersion;
        }

        if (schema == PresetPairSchemaVersion)
        {
            MigrateSchema34To35(config);
            schema = BloomStyleSchemaVersion;
        }

        // 35 -> 36: перебиндиваемые клавиши InterfaceSettings.Key*. В JSON их
        // нет, FromJsonOverwrite оставляет дефолты из инициализаторов модели,
        // поэтому миграция только фиксирует новую версию схемы.
        if (schema == BloomStyleSchemaVersion)
        {
            schema = KeyBindingsSchemaVersion;
        }

        // 36 -> 37: InterfaceSettings.KeyAggression. Поле аддитивное:
        // FromJsonOverwrite оставляет дефолт "L" из инициализатора модели,
        // поэтому миграция только фиксирует новую версию схемы.
        if (schema == KeyBindingsSchemaVersion)
        {
            schema = AggressionKeySchemaVersion;
        }

        // 37 -> 38: DisplaySettings.HDRSwitchPending. Existing schema-37
        // files predate the safe-start marker; FromJsonOverwrite leaves the
        // default false when the field is absent and preserves it when present.
        if (schema == AggressionKeySchemaVersion)
        {
            schema = HdrSwitchPendingSchemaVersion;
        }

        // 38 -> 39: DisplaySettings.PeakBrightnessFromDisplay. Поле аддитивное:
        // FromJsonOverwrite оставляет дефолт true, и сохранённый пик заменяется
        // пиком дисплея — до этой схемы автоопределения не было вовсе.
        if (schema == HdrSwitchPendingSchemaVersion)
        {
            schema = PeakBrightnessFromDisplaySchemaVersion;
        }

        // 39 -> 40: поля террейна переименованы — EnableReliefRim → EnableRim,
        // *Emission* → *Glow*, PulseSpeedScale → BlinkingSpeedScale. Схемы
        // 22–39 хранили их под старыми именами в секции Terrain; старее 22 —
        // плоско, их уже перенёс MigrateFlatVisualsToSections.
        if (schema == PeakBrightnessFromDisplaySchemaVersion)
        {
            if (sourceSchemaVersion >= 22)
            {
                MigrateTerrainNames(config, sourceJson);
            }

            schema = TerrainNamesSchemaVersion;
        }

        config.SchemaVersion = schema;
    }

    private static void MigrateFlatVisualsToSections(ClientConfig config, string sourceJson)
    {
        LegacySchema21? legacy = JsonUtility.FromJson<LegacySchema21>(sourceJson);
        if (legacy == null)
        {
            return;
        }

        config.Terrain = new TerrainSettings
        {
            FlowScale = legacy.TerrainFlowScale,
            ShimmerSpeedScale = legacy.TerrainShimmerSpeedScale,
            BlinkingSpeedScale = legacy.TerrainPulseSpeedScale,
            ShimmerColor = legacy.TerrainShimmerColor,
            EnableDistortion = legacy.EnableTerrainDistortion,
            TransitGlowColor = legacy.TransitEmissionColor,
            TransitGlowStrength = legacy.TransitEmissionStrength,
            PerspectiveGlowColor = legacy.PerspectiveEmissionColor,
            PerspectiveGlowStrength = legacy.PerspectiveEmissionStrength,
            SurfaceOccupancy = legacy.SurfaceOccupancy,
        };
        config.Effects = new EffectSettings
        {
            BloomEnabled = legacy.BloomEnabled,
            VignetteEnabled = legacy.VignetteEnabled,
            EigengrauEnabled = legacy.FilmGrainEnabled,
        };
        SettingSchema.Clamp(config.Terrain);
        SettingSchema.Clamp(config.Effects);
    }

    private static void MigrateTerrainNames(ClientConfig config, string sourceJson)
    {
        LegacyTerrainNames39? legacy = JsonUtility.FromJson<LegacyTerrainNames39>(sourceJson);
        if (legacy?.Terrain == null)
        {
            return;
        }

        LegacyTerrain39 terrain = legacy.Terrain;
        if (Has(sourceJson, nameof(LegacyTerrain39.EnableReliefRim)))
        {
            config.Terrain.EnableRim = terrain.EnableReliefRim;
        }

        if (Has(sourceJson, nameof(LegacyTerrain39.PulseSpeedScale)))
        {
            config.Terrain.BlinkingSpeedScale = terrain.PulseSpeedScale;
        }

        if (Has(sourceJson, nameof(LegacyTerrain39.TransitEmissionColor)))
        {
            config.Terrain.TransitGlowColor = terrain.TransitEmissionColor;
        }

        if (Has(sourceJson, nameof(LegacyTerrain39.TransitEmissionStrength)))
        {
            config.Terrain.TransitGlowStrength = terrain.TransitEmissionStrength;
        }

        if (Has(sourceJson, nameof(LegacyTerrain39.PerspectiveEmissionColor)))
        {
            config.Terrain.PerspectiveGlowColor = terrain.PerspectiveEmissionColor;
        }

        if (Has(sourceJson, nameof(LegacyTerrain39.PerspectiveEmissionStrength)))
        {
            config.Terrain.PerspectiveGlowStrength = terrain.PerspectiveEmissionStrength;
        }

        SettingSchema.Clamp(config.Terrain);

        static bool Has(string json, string field) =>
            json.IndexOf($"\"{field}\"", StringComparison.Ordinal) >= 0;
    }

    [Serializable]
    private sealed class LegacyTerrainNames39
    {
        public LegacyTerrain39? Terrain = default;
    }

    [Serializable]
    private sealed class LegacyTerrain39
    {
        public bool EnableReliefRim = default;
        public float PulseSpeedScale = default;
        public Color TransitEmissionColor = default;
        public float TransitEmissionStrength = default;
        public Color PerspectiveEmissionColor = default;
        public float PerspectiveEmissionStrength = default;
    }

    [Serializable]
    private sealed class LegacySchema21
    {
        // JsonUtility fills these public fields reflectively, which the C#
        // compiler cannot see. Explicit defaults keep that contract warning-free.
        public Vector2 TerrainFlowScale = default;
        public float TerrainShimmerSpeedScale = default;
        public float TerrainPulseSpeedScale = default;
        public Color TerrainShimmerColor = default;
        public bool EnableTerrainDistortion = default;
        public Color TransitEmissionColor = default;
        public float TransitEmissionStrength = default;
        public Color PerspectiveEmissionColor = default;
        public float PerspectiveEmissionStrength = default;
        public float SurfaceOccupancy = default;
        public bool BloomEnabled = default;
        public bool VignetteEnabled = default;
        public bool FilmGrainEnabled = default;
    }

    private void ReconcileStandardPreset(ClientConfig config)
    {
        // Ступени неизменяемы, поэтому снимок настроек всегда авторский: правка
        // конфига руками не переживает загрузку. Раньше здесь была ветка
        // «настройки разошлись — перевести в Custom», но Custom больше нет.
        config.GraphicsQualitySettings = _graphicsQualityProfile.Get(config.GraphicsPreset);
    }
}
