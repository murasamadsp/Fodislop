#nullable enable

using System;
using System.IO;
using Kern.Rendering;
using UnityEngine;

namespace Kern.Core;

internal sealed class ClientConfigMigration(GraphicsQualityProfile graphicsQualityProfile)
{
    private readonly GraphicsQualityProfile _graphicsQualityProfile = graphicsQualityProfile ??
        throw new ArgumentNullException(nameof(graphicsQualityProfile));

    /// <param name="rawJson">
    /// Исходный текст файла. Нужен потому, что до схемы 22 поля вида лежали
    /// плоско в корне и в типизированный <see cref="ClientConfig"/> не
    /// попадают: их читает <see cref="ClientConfigLegacySchema21"/>.
    /// </param>
    public bool Migrate(ClientConfig config, string rawJson)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        bool migrated = false;
        if (config.SchemaVersion < 9)
        {
            // Всё, что старее девятой схемы, не имело осмысленного пресета
            // графики: там лежал legacy-индекс качества 0..3.
            GraphicsPreset previousPreset = ClientConfigDefaults.ConvertLegacyGraphicsQuality(
                Mathf.Clamp((int)config.GraphicsPreset, 0, 3));
            config.GraphicsQualitySettings = _graphicsQualityProfile.Get(previousPreset);
            config.GraphicsPreset = GraphicsPreset.Custom;
            config.SchemaVersion = 9;
            migrated = true;
        }

        if (config.SchemaVersion < 12)
        {
            config.GraphicsQualitySettings.LightingMaximumTextureDimension =
                Mathf.Max(
                    config.GraphicsQualitySettings.LightingMaximumTextureDimension,
                    GraphicsQualitySettings.MinimumLightingTextureDimension);
            config.SchemaVersion = 12;
            migrated = true;
        }

        if (config.SchemaVersion < 22)
        {
            MigrateFlatVisualsToSections(config, rawJson);
            config.SchemaVersion = 22;
            migrated = true;
        }

        if (config.SchemaVersion < 23)
        {
            // Схема 23 сохраняла исторический сброс SDR-гаммы при деградации
            // до минимума. Поле удалено из текущей схемы, поэтому миграция
            // ничего не меняет, но ступень остаётся для старых конфигов.
            config.SchemaVersion = 23;
            migrated = true;
        }

        if (config.SchemaVersion < 24)
        {
            // Схема 24: все значения освещения теперь константные.
            // Миграция не требуется — ClientConfig.Lighting больше не используется.
            // Нельзя сразу ставить 28: ниже находятся реальные шаги схем 25-28.
            config.SchemaVersion = 24;
            migrated = true;
        }

        if (config.SchemaVersion < 25)
        {
            // Схема 25 добавляла тумблер сжатия динамического диапазона.
            // В схеме 27 он удалён вместе с самим сжатием, поэтому делать
            // здесь нечего — но ступень обязана остаться: версии идут
            // подряд, и пропуск двадцать пятой оставил бы старые конфиги
            // навсегда позади.
            config.SchemaVersion = 25;
            migrated = true;
        }

        if (config.SchemaVersion < 26)
        {
            // Схема 26: добавлен режим выборки пиксельной сетки. Старые
            // конфиги получают сглаживание границ: оно сохраняет привычный
            // плавный зум, а ступенчатый вариант навязывать нельзя — это
            // заметная смена ощущений.
            if (config.Display != null)
            {
                config.Display.PixelSampling = PixelSamplingMode.SmoothFiltered;
            }

            config.SchemaVersion = 26;
            migrated = true;
        }

        if (config.SchemaVersion < 27)
        {
            // Схема 27: сжатие динамического диапазона удалено целиком.
            // Поля ToneMappingEnabled и ToneMappingWhitePoint исчезли из
            // секций; JsonUtility просто не читает то, чего в типе нет,
            // поэтому старым конфигам достаточно поднять версию — иначе
            // они бесконечно считались бы устаревшими и переписывались с
            // резервной копией при каждом запуске.
            config.SchemaVersion = 27;
            migrated = true;
        }

        if (config.SchemaVersion < 28)
        {
            // Схема 28: адаптация масштаба интерфейса к экранам Retina / High-DPI.
            // Если масштаб оставался в неадаптированном значении по умолчанию (1.0)
            // на Retina-дисплее (MacBook и др.), поднимаем его до рекомендуемого (1.35x).
            if (config.Interface != null &&
                Mathf.Approximately(config.Interface.UIScale, 1f) &&
                UIScaleUtility.IsRetinaOrHighDpi)
            {
                config.Interface.UIScale = UIScaleUtility.RetinaDefaultScale;
            }

            config.SchemaVersion = 28;
            migrated = true;
        }

        if (config.SchemaVersion < 29)
        {
            // Схема 29 удаляет пользовательскую SDR-гамму. JsonUtility
            // отбрасывает legacy-поле Gamma при чтении, поэтому достаточно
            // продвинуть версию и перезаписать конфиг без этого поля.
            config.SchemaVersion = 29;
            migrated = true;
        }
        if (config.SchemaVersion > ClientConfig.CurrentSchemaVersion)
        {
            // JsonUtility уже отбросил неизвестные поля будущей схемы. Тихое
            // понижение и сохранение такого объекта поэтому необратимо теряет
            // данные при переключении ветки или откате билда.
            throw new InvalidDataException(
                $"Client config schema {config.SchemaVersion} is newer than supported " +
                $"schema {ClientConfig.CurrentSchemaVersion}; refusing to overwrite it.");
        }

        if (GraphicsQualityProfile.IsStandard(config.GraphicsPreset))
        {
            GraphicsQualitySettings standardSettings =
                _graphicsQualityProfile.Get(config.GraphicsPreset);
            if (config.GraphicsQualitySettings != standardSettings)
            {
                config.GraphicsQualitySettings = standardSettings;
                migrated = true;
            }

            if (!SettingSchema.MatchesDefaults(config.Lighting) ||
                !SettingSchema.MatchesDefaults(config.Terrain) ||
                !SettingSchema.MatchesDefaults(config.Effects) ||
                !SettingSchema.MatchesDefaults(config.PostProcess))
            {
                config.GraphicsPreset = GraphicsPreset.Custom;
                migrated = true;
            }
        }

        return migrated;
    }

    private static void MigrateFlatVisualsToSections(ClientConfig config, string rawJson)
    {
        if (config.SchemaVersion < 19)
        {
            config.Lighting = new WorldLightingSettings();
            config.Terrain = new TerrainSettings();
            config.Effects = new EffectSettings();
            config.PostProcess = new PostProcessSettings();
            return;
        }

        ClientConfigLegacySchema21? legacy =
            JsonUtility.FromJson<ClientConfigLegacySchema21>(rawJson);
        if (legacy == null)
        {
            return;
        }

        config.Lighting = legacy.ToLighting();
        config.Terrain = legacy.ToTerrain();
        config.Effects = legacy.ToEffects();

        // Плоский файл мог хранить значения вне нынешних границ: раньше
        // диапазон проверялся не везде, где записывался. Валидатор после
        // миграции падает на таком значении, поэтому границы применяются здесь,
        // при переносе, — это перенос, а не тихая правка живого конфига.
        SettingSchema.Clamp(config.Lighting);
        SettingSchema.Clamp(config.Terrain);
        SettingSchema.Clamp(config.PostProcess);
    }
}
