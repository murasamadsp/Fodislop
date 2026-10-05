#nullable enable

using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>
/// Четыре угловые UV квада и восемь их автотайловых вариантов.
/// </summary>
///
/// Автотайлинг не хранит отражённые и повёрнутые копии тайла в атласе — он
/// хранит один тайл и три бита дескриптора: отражение по x, отражение по y и
/// поворот на четверть. Порядок применения важен: отражения по осям, затем
/// поворот. Поменять его местами значит получить зеркальный тайл там, где
/// нужен повёрнутый.
public readonly struct TerrainQuadUvs
{
    private const int MirrorXBit = 0x40;
    private const int MirrorYBit = 0x20;
    private const int RotateBit = 0x80;

    public TerrainQuadUvs(Vector2 c0, Vector2 c1, Vector2 c2, Vector2 c3)
    {
        C0 = c0;
        C1 = c1;
        C2 = c2;
        C3 = c3;
    }

    public Vector2 C0 { get; }

    public Vector2 C1 { get; }

    public Vector2 C2 { get; }

    public Vector2 C3 { get; }

    public static TerrainQuadUvs Canonical => new(
        new Vector2(0f, 0f),
        new Vector2(1f, 0f),
        new Vector2(1f, 1f),
        new Vector2(0f, 1f));

    public TerrainQuadUvs Transform(int descriptor)
    {
        Vector2 c0 = C0;
        Vector2 c1 = C1;
        Vector2 c2 = C2;
        Vector2 c3 = C3;

        if ((descriptor & MirrorXBit) != 0)
        {
            (c0.x, c1.x) = (c1.x, c0.x);
            (c3.x, c2.x) = (c2.x, c3.x);
        }

        if ((descriptor & MirrorYBit) != 0)
        {
            (c0.y, c3.y) = (c3.y, c0.y);
            (c1.y, c2.y) = (c2.y, c1.y);
        }

        if ((descriptor & RotateBit) != 0)
        {
            (c0, c1, c2, c3) = (c1, c2, c3, c0);
        }

        return new TerrainQuadUvs(c0, c1, c2, c3);
    }
}
