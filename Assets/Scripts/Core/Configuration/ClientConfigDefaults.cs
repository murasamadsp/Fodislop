#nullable enable

using System;
using System.IO;
using Fodinae.Rendering;

namespace Fodinae.Core;

internal static class ClientConfigDefaults
{
    public static ClientConfig Create(GraphicsQualityProfile graphicsQualityProfile)
    {
        if (graphicsQualityProfile == null)
        {
            throw new ArgumentNullException(nameof(graphicsQualityProfile));
        }

        var config = new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
        };
        config.GraphicsQualitySettings = graphicsQualityProfile.Get(config.GraphicsPreset);
        config.Interface.UIScale = UIScaleUtility.RecommendedDefaultScale;
        return config;
    }

    public static GraphicsPreset ConvertLegacyGraphicsQuality(int legacyQuality)
    {
        return legacyQuality switch
        {
            0 => GraphicsPreset.Low,
            1 => GraphicsPreset.Medium,
            2 => GraphicsPreset.High,
            3 => GraphicsPreset.Ultra,
            _ => throw new InvalidDataException(
                $"Legacy graphics quality '{legacyQuality}' is outside the supported range 0..3."),
        };
    }
}
