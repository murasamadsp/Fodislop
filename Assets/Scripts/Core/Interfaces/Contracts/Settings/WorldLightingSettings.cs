#nullable enable

using System;

namespace Kern.Core;

[Serializable]
public sealed class WorldLightingSettings
{
    // Параметры света, включая динамический, живут в LightingConfigHolder
    // (VisualTuning.cs). Здесь лежала вторая копия: движок и робот читали
    // разные константы и совпадали только случайно.
}
