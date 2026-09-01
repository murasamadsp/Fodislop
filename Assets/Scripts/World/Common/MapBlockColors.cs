#nullable enable

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MinesServer.Data;
using UnityEngine;

namespace Fodinae.World;

/// <summary>
/// Authentic map and minimap cell color tables ported from original client MapViewer.
/// </summary>
public static class MapBlockColors
{
    private static readonly Color[] _colorTable = new Color[256];
    private static readonly Color32[] _color32Table = new Color32[256];
    private static readonly Color[] _aliveColorTable = new Color[256];
    private static readonly Color32[] _aliveColor32Table = new Color32[256];
    private static readonly Color[] _transparentTable = new Color[256];
    private static readonly Color32[] _transparent32Table = new Color32[256];
    private static readonly Color[] _customTable = new Color[256];
    private static readonly Color32[] _custom32Table = new Color32[256];

    static MapBlockColors()
    {
        InitializeCustomTable();
        InitializeTables();
    }

    /// <summary>
    /// Gets default minimap and world map color for the specified cell type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color GetColor(CellType cellType) => _colorTable[(byte)cellType];

    /// <summary>
    /// Gets default minimap and world map Color32 for the specified cell type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 GetColor32(CellType cellType) => _color32Table[(byte)cellType];

    /// <summary>
    /// Gets alive crystal color for the specified cell type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color GetAliveColor(CellType cellType) => _aliveColorTable[(byte)cellType];

    /// <summary>
    /// Gets alive crystal Color32 for the specified cell type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 GetAliveColor32(CellType cellType) => _aliveColor32Table[(byte)cellType];

    /// <summary>
    /// Gets scanner transparency mode color for the specified cell type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color GetTransparentColor(CellType cellType) => _transparentTable[(byte)cellType];

    /// <summary>
    /// Gets scanner transparency mode Color32 for the specified cell type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color32 GetTransparentColor32(CellType cellType) => _transparent32Table[(byte)cellType];

    /// <summary>
    /// Gets custom color table color for the specified cell type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color GetCustomColor(CellType cellType) => _customTable[(byte)cellType];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color Rgbq(int r, int g, int b) =>
        new Color(r / 256f, g / 256f, b / 256f, 1f);

    private static void InitializeCustomTable()
    {
        for (int i = 0; i < 256; i++)
        {
            int num = i + 50;
            _customTable[i] = new Color(num / 312f, num / 312f, num / 312f, 1f);
            if (i <= 39)
            {
                _customTable[i] = new Color(0.04f, 0.045f, 0.045f, 1f);
            }
        }
    }

    private static void InitializeTables()
    {
        var sandIndices = new HashSet<int>
        {
            82, 91, 97, 98, 99, 100, 86, 66, 67, 95, 96, 60, 61, 62, 63, 64, 65, 68, 69,
        };

        for (int i = 0; i < 256; i++)
        {
            _colorTable[i] = new Color(i / 512f, i / 256f, 0.01f, 1f);
            _aliveColorTable[i] = new Color(i / 512f, i / 256f, 0.01f, 1f);
            _transparentTable[i] = new Color(0f, 0f, 0f, 1f);

            if (i > 39)
            {
                _transparentTable[i] = new Color(0.4f, 0.45f, 0.45f, 1f);
            }

            if (i < 120 && sandIndices.Contains(i))
            {
                _transparentTable[i] = new Color(0.3f, 0.35f, 0.35f, 1f);
            }
        }

        // Transparent table overrides
        _transparentTable[30] = new Color(1f, 1f, 0f, 1f);
        _transparentTable[35] = new Color(0.4f, 0.01f, 0.1f, 1f);
        _transparentTable[36] = new Color(0.4f, 0.1f, 0.01f, 1f);
        _transparentTable[39] = new Color(0.4f, 0.01f, 0.01f, 1f);
        _transparentTable[80] = new Color(0f, 1f, 1f, 1f);
        _transparentTable[81] = new Color(0f, 0.7f, 0.7f, 1f);
        _transparentTable[106] = new Color(1f, 1f, 1f, 1f);
        _transparentTable[114] = new Color(0.6f, 0.6f, 0.6f, 1f);
        _transparentTable[115] = new Color(0.6f, 0.6f, 0.6f, 1f);
        _transparentTable[117] = new Color(1f, 1f, 1f, 1f);
        _transparentTable[119] = new Color(0.8f, 1f, 1f, 1f);

        // Color table overrides
        _colorTable[0] = new Color(0f, 0f, 0f, 0.5f);
        _colorTable[1] = new Color(0f, 0f, 0f, 0.5f);
        _colorTable[32] = Rgbq(0, 0, 0);
        _colorTable[33] = Rgbq(15, 11, 3);
        _colorTable[34] = Rgbq(29, 25, 18);
        _colorTable[35] = Rgbq(68, 68, 68);
        _colorTable[36] = Rgbq(85, 68, 34);
        _colorTable[37] = Rgbq(68, 0, 0);
        _colorTable[38] = Rgbq(51, 68, 0);
        _colorTable[40] = Rgbq(255, 97, 107);
        _colorTable[41] = Rgbq(255, 107, 97);
        _colorTable[42] = Rgbq(255, 107, 107);
        _colorTable[43] = Rgbq(255, 187, 251);
        _colorTable[44] = Rgbq(191, 241, 251);
        _colorTable[45] = Rgbq(207, 203, 241);
        _colorTable[48] = Rgbq(255, 255, 255);
        _colorTable[49] = Rgbq(101, 150, 126);
        _colorTable[50] = Rgbq(101, 255, 255);
        _colorTable[51] = Rgbq(255, 51, 51);
        _colorTable[52] = Rgbq(255, 101, 255);
        _colorTable[53] = Rgbq(34, 101, 255);
        _colorTable[54] = Rgbq(238, 254, 255);
        _colorTable[55] = Rgbq(238, 254, 255);
        _colorTable[56] = Rgbq(225, 254, 255);
        _colorTable[57] = Rgbq(226, 254, 255);
        _colorTable[58] = Rgbq(227, 254, 255);
        _colorTable[59] = Rgbq(228, 254, 255);
        _colorTable[60] = Rgbq(204, 204, 204);
        _colorTable[61] = Rgbq(221, 221, 221);
        _colorTable[62] = Rgbq(255, 204, 204);
        _colorTable[63] = Rgbq(255, 221, 221);
        _colorTable[64] = Rgbq(170, 170, 170);
        _colorTable[65] = Rgbq(187, 187, 187);
        _colorTable[66] = Rgbq(184, 153, 51);
        _colorTable[67] = Rgbq(184, 136, 187);
        _colorTable[68] = Rgbq(119, 68, 68);
        _colorTable[69] = Rgbq(34, 68, 153);
        _colorTable[70] = Rgbq(243, 241, 152);
        _colorTable[71] = Rgbq(71, 215, 100);
        _colorTable[72] = Rgbq(101, 134, 247);
        _colorTable[73] = Rgbq(247, 82, 67);
        _colorTable[74] = Rgbq(132, 238, 247);
        _colorTable[75] = Rgbq(255, 135, 231);
        _colorTable[82] = Rgbq(17, 102, 102);
        _colorTable[83] = Rgbq(50, 135, 152);
        _colorTable[86] = Rgbq(184, 255, 17);
        _colorTable[90] = Rgbq(238, 238, 238);
        _colorTable[91] = Rgbq(255, 90, 0);
        _colorTable[92] = Rgbq(193, 187, 187);
        _colorTable[93] = Rgbq(187, 193, 187);
        _colorTable[94] = Rgbq(187, 187, 193);
        _colorTable[95] = Rgbq(184, 255, 34);
        _colorTable[96] = Rgbq(184, 255, 68);
        _colorTable[97] = Rgbq(112, 160, 183);
        _colorTable[98] = Rgbq(112, 187, 207);
        _colorTable[99] = Rgbq(219, 209, 125);
        _colorTable[100] = Rgbq(181, 168, 57);
        _colorTable[101] = Rgbq(76, 191, 0);
        _colorTable[102] = Rgbq(208, 206, 0);
        _colorTable[103] = Rgbq(133, 81, 166);
        _colorTable[104] = Rgbq(153, 153, 136);
        _colorTable[105] = Rgbq(198, 0, 0);
        _colorTable[106] = Rgbq(136, 136, 136);
        _colorTable[107] = Rgbq(8, 215, 100);
        _colorTable[108] = Rgbq(255, 0, 0);
        _colorTable[109] = Rgbq(0, 0, 255);
        _colorTable[110] = Rgbq(255, 0, 255);
        _colorTable[111] = Rgbq(238, 238, 255);
        _colorTable[112] = Rgbq(0, 255, 255);
        _colorTable[113] = Rgbq(211, 159, 166);
        _colorTable[114] = Rgbq(119, 119, 119);
        _colorTable[115] = Rgbq(56, 118, 65);
        _colorTable[116] = Rgbq(17, 17, 255);
        _colorTable[117] = Rgbq(170, 119, 119);
        _colorTable[118] = Rgbq(100, 98, 21);
        _colorTable[119] = Rgbq(170, 255, 255);
        _colorTable[120] = Rgbq(227, 191, 120);
        _colorTable[121] = Rgbq(163, 136, 72);
        _colorTable[122] = Rgbq(51, 153, 120);

        // Alive color table overrides
        _aliveColorTable[50] = Rgbq(101, 255, 255);
        _aliveColorTable[51] = Rgbq(255, 51, 51);
        _aliveColorTable[52] = Rgbq(255, 101, 255);
        _aliveColorTable[53] = Rgbq(255, 138, 255);
        _aliveColorTable[54] = Rgbq(238, 254, 255);
        _aliveColorTable[55] = Rgbq(238, 254, 255);
        _aliveColorTable[116] = Rgbq(161, 162, 255);
        _aliveColorTable[119] = Rgbq(170, 255, 255);

        // Fill Color32 lookup tables
        for (int i = 0; i < 256; i++)
        {
            _color32Table[i] = (Color32)_colorTable[i];
            _aliveColor32Table[i] = (Color32)_aliveColorTable[i];
            _transparent32Table[i] = (Color32)_transparentTable[i];
            _custom32Table[i] = (Color32)_customTable[i];
        }
    }
}
