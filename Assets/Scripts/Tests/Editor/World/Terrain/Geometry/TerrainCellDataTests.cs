#nullable enable

using System.Runtime.InteropServices;
using Kern.World;
using Kern.World.Terrain;
using MinesServer.Data;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.World;

// Раскладка TerrainCellData против чисел, посчитанных вручную по таблице
// битов в начале TerrainCellData.cs.
[TestFixture]
public sealed class TerrainCellDataTests
{
    [Test]
    public void CellIsOneUshortAndTypeIsTwoUint4()
    {
        Assert.That(Marshal.SizeOf<TerrainCell>(), Is.EqualTo(2));
        Assert.That(Marshal.SizeOf<TerrainTypeRow>(), Is.EqualTo(32));
    }

    [Test]
    public void ZeroTypeMeansNoCell()
    {
        // Шейдер отличает пустой слот по нулевому типу без отдельного бита.
        Assert.That((int)CellType.Unloaded, Is.Zero);
    }

    [Test]
    public void CellFieldsLandOnTheirBits()
    {
        TerrainCell cell = TerrainCellData.PackCell((CellType)0x5A, (CellType)0x21);

        // 5A | 21 << 8.
        Assert.That(cell.Bits, Is.EqualTo((ushort)0x215A));
        Assert.That(TerrainCellData.ForegroundTypeOf(cell), Is.EqualTo((CellType)0x5A));
        Assert.That(TerrainCellData.BackgroundTypeOf(cell), Is.EqualTo((CellType)0x21));
    }

    [Test]
    public void MarginCellCarriesOnlyForegroundType()
    {
        Assert.That(TerrainCellData.PackMargin((CellType)0x5A), Is.EqualTo(new TerrainCell(0x5A)));
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
        TerrainTypeRow row = TerrainCellData.PackType(new TerrainTypeSurface(
            AtlasSlot: 2,
            AtlasRect: new Vector4(0.25f, 0.5f, 0.125f, 1f),
            TileSize: 0.03125f,
            FrameCount: 3,
            FrameHeightTiles: 2f,
            Animation: CellAnimationType.Blinking,
            AnimationSettings: new TerrainAnimationSettings(TerrainAnimationProfile.FacetedCrystal, 1.5f),
            HasTileGroup: true,
            TileGroupId: 3,
            ContinuousSheet: true,
            ReliefGroup: 4,
            LightColor: new Color32(10, 20, 30, 128),
            IsGlowing: true,
            EmissionPower: 0.5f,
            Solid: true,
            ForegroundRoundable: false,
            ForegroundDecal: TerrainDecalFamily.Stone,
            IsBuildingWall: false,
            IsBuildingCorner: true,
            OpaqueInOwnAtlas: true,
            OpaqueInAnyAtlas: true,
            Distortion: MinesServer.Networking.Server.Packets.Connection.CellDistortionType.Cause));

        // half: 0.25 = 3400, 0.5 = 3800, 0.125 = 3000, 1 = 3C00,
        // 1/32 = 2800, 3 = 4200, 2 = 4000, 1.5 = 3E00.
        Assert.That(row.AX, Is.EqualTo(0x38003400u));
        Assert.That(row.AY, Is.EqualTo(0x3C003000u));
        Assert.That(row.AZ, Is.EqualTo(0x42002800u));
        Assert.That(row.AW, Is.EqualTo(0x3E004000u));
        Assert.That(row.BX, Is.EqualTo(0x021E140Au));
        Assert.That(row.BY, Is.EqualTo(0x3E000000u), "0.5 * 0.25 = 0.125f");
        // анимация 1, профиль 3, светится, твёрдый, масса, Cause, есть атлас.
        Assert.That(row.BZ, Is.EqualTo(0x00930301u));
        // камень 2, тайлгруппа 4, угол 10, непрозрачен 20 и 40; кайма Rock
        // 2 << 8; рельеф 4 << 16; тайлгруппа 3 << 24.
        Assert.That(row.BW, Is.EqualTo(0x03040176u));
    }

    [Test]
    public void PaletteBlockAndEmptyLandOnTheirBits()
    {
        TerrainTypeRow row = TerrainCellData.PackType(new TerrainTypeSurface(
            AtlasSlot: 0,
            AtlasRect: Vector4.zero,
            TileSize: 0f,
            FrameCount: 1,
            FrameHeightTiles: 1f,
            Animation: CellAnimationType.None,
            AnimationSettings: new TerrainAnimationSettings(TerrainAnimationProfile.PrismaticCrystal, 0f, 5f),
            HasTileGroup: false,
            TileGroupId: 7,
            ContinuousSheet: false,
            ReliefGroup: 0,
            LightColor: default,
            IsGlowing: false,
            EmissionPower: 0f,
            Solid: false,
            ForegroundRoundable: false,
            ForegroundDecal: TerrainDecalFamily.None,
            IsBuildingWall: true,
            IsBuildingCorner: false,
            OpaqueInOwnAtlas: false,
            OpaqueInAnyAtlas: false,
            Distortion: MinesServer.Networking.Server.Packets.Connection.CellDistortionType.Block,
            IsEmpty: true));

        // профиль 1 << 8, Block 21, Empty 22, атласа нет, палитра 5 << 24.
        Assert.That(row.BZ, Is.EqualTo(0x05600100u));
        // стена 8; номер тайлгруппы без самой группы не пишется.
        Assert.That(row.BW, Is.EqualTo(0x00000008u));
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
        Assert.That(TerrainCellData.PackDecal(TerrainDecalCatalog.StoneRule), Is.EqualTo(0x839Eu));
        // земля: доля из конфига, зерно — CellType.Empty.
        Assert.That(
            TerrainCellData.PackDecal(TerrainDecalCatalog.GroundRule),
            Is.EqualTo(TerrainConfigHolder.GroundDecalPlacementPercent | ((uint)CellType.Empty << 7)));
    }
}
