#nullable enable

using System.Runtime.InteropServices;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

// Раскладка TerrainCellData против чисел, посчитанных вручную по таблице
// битов в TerrainCellFormat.hlsl.
[TestFixture]
public sealed class TerrainCellDataTests
{
    [Test]
    public void CellIsOneByteAndTypeIsFourUints()
    {
        Assert.That(Marshal.SizeOf<TerrainCell>(), Is.EqualTo(1));
        Assert.That(Marshal.SizeOf<TerrainTypeRow>(), Is.EqualTo(16));
    }

    [Test]
    public void ZeroTypeMeansNoCell()
    {
        // Шейдер отличает пустой слот по нулевому типу без отдельного бита.
        Assert.That((int)CellType.Unloaded, Is.Zero);
    }

    [Test]
    public void CellIsItsType()
    {
        TerrainCell cell = TerrainCellData.PackCell((CellType)0x5A);

        Assert.That(cell.Bits, Is.EqualTo((byte)0x5A));
        Assert.That(TerrainCellData.TypeOf(cell), Is.EqualTo((CellType)0x5A));
    }

    // Константы искажения целые и меньше 2^24: во float шейдера они точные.
    [Test]
    public void DistortionConstantsAreExactInFloat()
    {
        Vector4[] vectors = TerrainCellData.PackDistortion();
        Assert.That(vectors, Has.Length.EqualTo(TerrainCellData.DistortionVectorCount));
        foreach (Vector4 vector in vectors)
        {
            foreach (float value in new[] { vector.x, vector.y, vector.z, vector.w })
            {
                Assert.That(value % 1f, Is.Zero);
                Assert.That(Mathf.Abs(value), Is.LessThan(1 << 24));
            }
        }
    }

    [Test]
    public void TypeFieldsLandOnTheirBits()
    {
        TerrainTypeRow row = TerrainCellData.PackType(new TerrainTypeFields(
            Type: CellType.Rock,
            Block: new BlockDefinition(
                DrawLayer: CellDrawLayer.Foreground,
                Glow: 1f,
                Outline: CellOutline.Corner,
                TextureAnchor: CellTextureAnchor.World,
                AnimationType: CellAnimationType.Shimmer,
                AnimationSpeed: 1.5f,
                SurfaceEffect: CellSurfaceEffect.Faceted,
                SurfaceEffectPalette: 0,
                DecalAtlas: CellDecalAtlas.Rock,
                RimMass: 4,
                MapColor: default),
            Slot: 2,
            AtlasRect: new Vector4(0.25f, 0.5f, 0.125f, 1f),
            TileSize: 0.03125f,
            FrameCount: 3,
            FrameHeightTiles: 2f,
            OpaqueOwn: true,
            OpaqueAny: true,
            HasTileGroup: true,
            TileGroupId: 3));

        // Тайл 1/32 — атлас 1024: x 256, y 512 << 12, кадров 3 << 24;
        // w 128, h 1024 << 12, кайма 4 << 24.
        Assert.That(row.AtlasXY, Is.EqualTo(0x03200100u));
        Assert.That(row.AtlasWH, Is.EqualTo(0x04400080u));
        // слот 2, блок (бит пола 0), непрозрачен 10 и 20, по миру 40, угол 5 << 7,
        // мерцание 2 << 10, грани 2 << 12, камень 2 << 17.
        Assert.That(row.Look, Is.EqualTo(0x00042AF2u));
        // скорость 1.5 в half — 3E00, свечение 255 << 16, тайлгруппа (3 + 1) << 24.
        Assert.That(row.SpeedGlowTile, Is.EqualTo(0x04FF3E00u));
    }

    [Test]
    public void PaletteAndNoTextureLandOnTheirBits()
    {
        var type = (CellType)0x5A;
        TerrainTypeRow row = TerrainCellData.PackType(new TerrainTypeFields(
            Type: type,
            Block: new BlockDefinition(
                DrawLayer: CellDrawLayer.Background,
                Glow: 0f,
                Outline: CellOutline.Wall,
                TextureAnchor: CellTextureAnchor.Cell,
                AnimationType: CellAnimationType.None,
                AnimationSpeed: 50f,
                SurfaceEffect: CellSurfaceEffect.Prismatic,
                SurfaceEffectPalette: 5,
                DecalAtlas: CellDecalAtlas.None,
                RimMass: 0,
                MapColor: default),
            Slot: 0,
            AtlasRect: Vector4.zero,
            TileSize: 0f,
            FrameCount: 1,
            FrameHeightTiles: 1f,
            OpaqueOwn: false,
            OpaqueAny: false,
            HasTileGroup: false,
            TileGroupId: 7));

        // кадров 1 << 24; прямоугольника нет.
        Assert.That(row.AtlasXY, Is.EqualTo(0x01000000u));
        Assert.That(row.AtlasWH, Is.Zero);
        // проходим 8, стена 4 << 7, радужный кристалл 3 << 12, палитра 5 << 14.
        Assert.That(row.Look, Is.EqualTo(0x00017208u));
        // скорость 50 в half — 5240, свечения нет; номер тайлгруппы без
        // самой группы не пишется.
        Assert.That(row.SpeedGlowTile, Is.EqualTo(0x5240u));
    }

    // Свечение — байт: края точные, середина округляется к ближнему.
    [TestCase(0f, (byte)0)]
    [TestCase(1f, (byte)255)]
    [TestCase(0.5f, (byte)128)]
    public void GlowIsAByte(float glow, byte expected)
    {
        Assert.That(TerrainCellData.GlowByte(glow), Is.EqualTo(expected));
        Assert.That(TerrainCellData.GlowOf(TerrainCellData.GlowByte(1f)), Is.EqualTo(1f));
    }

    [Test]
    public void TileDescriptorsArePackedFourPerWord()
    {
        uint[] words = TerrainCellData.PackTileDescriptors();
        Assert.That(words, Has.Length.EqualTo(64));
        for (int mask = 0; mask < 256; mask++)
        {
            Assert.That(
                (words[mask >> 2] >> ((mask & 3) * 8)) & 0xFFu,
                Is.EqualTo(TileBitmaskConverter.GetDescriptor((byte)mask)),
                $"маска {mask}");
        }
    }

    [Test]
    public void DecalRulesLandOnTheirBits()
    {
        // камень: 30 | 7 << 7 | атлас камня 1 << 15.
        Assert.That(TerrainCellData.PackDecal(TerrainDecalCatalog.RockRule), Is.EqualTo(0x839Eu));
        // земля: доля из конфига, зерно — CellType.Empty.
        Assert.That(
            TerrainCellData.PackDecal(TerrainDecalCatalog.GroundRule),
            Is.EqualTo(TerrainConfigHolder.GroundDecalPlacementPercent | ((uint)CellType.Empty << 7)));
    }
}
