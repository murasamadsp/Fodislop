#nullable enable

namespace Fodinae.World.Lighting
{
    public readonly record struct CascadeLayout(
        int Offset,
        int EntryCount,
        int ProbeWidth,
        int ProbeHeight,
        int ProbeSpacing,
        int DirectionCount,
        float IntervalStart,
        float IntervalEnd);
}
