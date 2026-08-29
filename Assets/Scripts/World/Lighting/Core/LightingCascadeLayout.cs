#nullable enable

namespace Fodinae.World.Lighting
{
    internal readonly record struct CascadeLayout(
        int Offset,
        int EntryCount,
        int ProbeWidth,
        int ProbeHeight,
        int ProbeSpacing,
        int DirectionCount,
        float IntervalStart,
        float IntervalEnd);
}
