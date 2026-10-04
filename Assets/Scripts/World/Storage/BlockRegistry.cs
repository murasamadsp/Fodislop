#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MinesServer.Data;
using MinesServer.Networking.Server.Packets.Connection;
using UnityEngine;

namespace Kern.World;

public sealed class BlockRegistry : IBlockRegistry
{
    private const string ResourcePath = "Config/cells";
    private const string RelativeFilePath = "Assets/Resources/Config/cells.json";

    private static readonly BlockDefinition[] s_blockArray = new BlockDefinition[256];
    private static readonly Dictionary<CellType, BlockDefinition> s_blocks = LoadRegistry();
    private static readonly byte[][] s_tileGroups = ComputeTileGroups();
    private static readonly BlockRegistry s_defaultInstance = new();

    public static IBlockRegistry Default => s_defaultInstance;

    public static IReadOnlyDictionary<CellType, BlockDefinition> Blocks => s_blocks;

    IReadOnlyDictionary<CellType, BlockDefinition> IBlockRegistry.All => s_blocks;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Warmup()
    {
        _ = s_blocks;
    }

    public static BlockDefinition Get(CellType type) =>
        s_blockArray[(byte)type];

    BlockDefinition IBlockRegistry.Get(CellType type) =>
        s_blockArray[(byte)type];

    public static bool TryGet(CellType type, out BlockDefinition definition) =>
        s_blocks.TryGetValue(type, out definition);

    bool IBlockRegistry.TryGet(CellType type, out BlockDefinition definition) =>
        s_blocks.TryGetValue(type, out definition);

    public static byte[][] GetTileGroups() =>
        s_tileGroups;

    byte[][] IBlockRegistry.GetTileGroups() =>
        s_tileGroups;

    private static Dictionary<CellType, BlockDefinition> LoadRegistry()
    {
        string json = LoadJsonContent();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        var rawMap = JsonSerializer.Deserialize<Dictionary<string, RawCellDefinition>>(json, options);
        if (rawMap == null || rawMap.Count == 0)
        {
            throw new InvalidDataException($"Failed to deserialize block config from '{RelativeFilePath}'.");
        }

        var result = new Dictionary<CellType, BlockDefinition>(rawMap.Count);
        Array.Clear(s_blockArray, 0, s_blockArray.Length);

        foreach ((string cellName, RawCellDefinition raw) in rawMap)
        {
            if (!Enum.TryParse(cellName, out CellType cellType))
            {
                continue;
            }

            CellDistortionType distortion = CellDistortionType.Neutral;
            if (!string.IsNullOrEmpty(raw.MeshDistortion))
            {
                Enum.TryParse(raw.MeshDistortion, true, out distortion);
            }

            CellAnimationType animation = CellAnimationType.None;
            if (!string.IsNullOrEmpty(raw.ShaderEffect))
            {
                Enum.TryParse(raw.ShaderEffect, true, out animation);
            }

            var def = new BlockDefinition
            {
                Passable = raw.Passable,
                Breakable = raw.Breakable,
                Diggable = raw.Diggable,
                MoveCooldownMs = raw.MoveCooldownMs,
                CastsShadow = raw.CastsShadow,
                ReceivesShadow = raw.ReceivesShadow,
                BlendWithNeighbors = raw.BlendWithNeighbors,
                EmitsLight = raw.EmitsLight,
                ConnectedTileGroupId = raw.ConnectedTileGroupId,
                MeshDistortion = distortion,
                ShaderEffect = animation,
                ShaderEffectSpeed = raw.ShaderEffectSpeed,
                ShaderEffectPhaseOffset = raw.ShaderEffectPhaseOffset,
                SurfaceShaderProfile = raw.SurfaceShaderProfile ?? "Default",
                DecalFamily = raw.DecalFamily ?? "None",
                PrismaticPaletteIndex = raw.PrismaticPaletteIndex,
                TerrainSeamGroupId = raw.TerrainSeamGroupId,
                CanRoundCorners = raw.CanRoundCorners,
                IsRoad = raw.IsRoad,
                IsCrystalVein = raw.IsCrystalVein,
                IsSolidRockBed = raw.IsSolidRockBed,
                IsFluid = raw.IsFluid,
                ReliefRimFamily = raw.ReliefRimFamily ?? "None",
                StructurePartType = raw.StructurePartType ?? "None",
                IsPackBlock = raw.IsPackBlock,
                IsBuildingBlock = raw.IsBuildingBlock,
                CrystalBasketIndex = raw.CrystalBasketIndex,
                MapColorHex = raw.MapColorHex,
            };

            result[cellType] = def;
            s_blockArray[(byte)cellType] = def;
        }

        return result;
    }

    private static string LoadJsonContent()
    {
        string directPath = Path.Combine(Application.dataPath, "Resources", "Config", "cells.json");
        if (File.Exists(directPath))
        {
            return File.ReadAllText(directPath);
        }

        string workingDirPath = Path.Combine(Directory.GetCurrentDirectory(), RelativeFilePath);
        if (File.Exists(workingDirPath))
        {
            return File.ReadAllText(workingDirPath);
        }

        // Walk up from cwd (handles dotnet test running from bin/Debug/net10.0/)
        DirectoryInfo? dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, RelativeFilePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            dir = dir.Parent;
        }

        TextAsset? asset = Resources.Load<TextAsset>(ResourcePath);
        if (asset != null && !string.IsNullOrEmpty(asset.text))
        {
            return asset.text;
        }

        throw new FileNotFoundException(
            $"Master block config file not found at '{RelativeFilePath}' or Resources '{ResourcePath}'.");
    }

    private static byte[][] ComputeTileGroups()
    {
        var map = new SortedDictionary<int, List<byte>>();
        foreach ((CellType type, BlockDefinition def) in s_blocks)
        {
            if (def.ConnectedTileGroupId >= 0)
            {
                if (!map.TryGetValue(def.ConnectedTileGroupId, out List<byte>? list))
                {
                    list = new List<byte>();
                    map[def.ConnectedTileGroupId] = list;
                }

                list.Add((byte)type);
            }
        }

        var result = new byte[map.Count][];
        int i = 0;
        foreach (List<byte> list in map.Values)
        {
            result[i++] = list.ToArray();
        }

        return result;
    }

    private sealed class RawCellDefinition
    {
        // 1. Физика и базовые свойства
        public bool Passable { get; set; }

        public bool Breakable { get; set; }

        public bool Diggable { get; set; }

        public ushort MoveCooldownMs { get; set; }

        // 2. Светотень и освещение
        public bool CastsShadow { get; set; }

        public bool ReceivesShadow { get; set; }

        public bool BlendWithNeighbors { get; set; }

        public bool EmitsLight { get; set; }

        // 3. Текстура и шейдерные эффекты
        public int ConnectedTileGroupId { get; set; } = -1;

        public string? MeshDistortion { get; set; }

        public string? ShaderEffect { get; set; }

        public byte ShaderEffectSpeed { get; set; }

        public byte ShaderEffectPhaseOffset { get; set; }

        public string? SurfaceShaderProfile { get; set; }

        public string? DecalFamily { get; set; }

        public int PrismaticPaletteIndex { get; set; }

        // 4. Геометрия террейна и швы
        public byte TerrainSeamGroupId { get; set; }

        public bool CanRoundCorners { get; set; }

        public bool IsRoad { get; set; }

        public bool IsCrystalVein { get; set; }

        public bool IsSolidRockBed { get; set; }

        public bool IsFluid { get; set; }

        public string? ReliefRimFamily { get; set; }

        // 5. Постройки и интерактивные зоны
        public string? StructurePartType { get; set; }

        public bool IsPackBlock { get; set; }

        public bool IsBuildingBlock { get; set; }

        // 6. Экономика и сбор
        public int CrystalBasketIndex { get; set; } = -1;

        // 7. Карта
        public string? MapColorHex { get; set; }
    }
}
