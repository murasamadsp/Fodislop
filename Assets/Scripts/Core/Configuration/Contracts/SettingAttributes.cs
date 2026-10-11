#nullable enable

using System;

namespace Kern.Core;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SettingRangeAttribute : Attribute
{
    public SettingRangeAttribute(float minimum, float maximum)
    {
        if (minimum > maximum)
        {
            throw new ArgumentException(
                $"Setting range is inverted: [{minimum}, {maximum}].",
                nameof(minimum));
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    public float Minimum { get; }

    public float Maximum { get; }
}

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SettingLabelAttribute : Attribute
{
    public SettingLabelAttribute(string localizationKey)
    {
        LocalizationKey = string.IsNullOrWhiteSpace(localizationKey)
            ? throw new ArgumentException("Localization key must not be empty.", nameof(localizationKey))
            : localizationKey;
    }

    public string LocalizationKey { get; }
}

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SettingUnboundedAttribute : Attribute
{
    public SettingUnboundedAttribute(string reason)
    {
        Reason = string.IsNullOrWhiteSpace(reason)
            ? throw new ArgumentException("Reason must not be empty.", nameof(reason))
            : reason;
    }

    public string Reason { get; }
}

public enum SettingConsumerTarget
{
    LightingEngine,
    PostProcessController,
    TerrainRenderer,
    SurfaceRenderer,
    DisplayManager,
    AudioSystem,
    LocalizationService,
    NetworkService,
    UserInterface,
    Gameplay,
}

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class SettingConsumerAttribute : Attribute
{
    public SettingConsumerAttribute(SettingConsumerTarget target, string mechanism)
    {
        Target = target;
        Mechanism = string.IsNullOrWhiteSpace(mechanism)
            ? throw new ArgumentException("Mechanism must not be empty.", nameof(mechanism))
            : mechanism;
    }

    public SettingConsumerTarget Target { get; }

    public string Mechanism { get; }
}
