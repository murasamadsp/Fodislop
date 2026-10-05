#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World;

public sealed class BlockRegistry : IBlockRegistry
{
    private const string ResourcePath = "Config/cells";
    private const string RelativeFilePath = "Assets/Resources/Config/cells.json";

    private static readonly BlockDefinition[] s_blockArray = new BlockDefinition[256];
    private static readonly Dictionary<CellType, BlockDefinition> s_blocks = LoadRegistry();
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

            var def = new BlockDefinition
            {
                Passable = raw.Passable,
                EmitsLight = raw.EmitsLight,
                Surface = ParseEnum(raw.Surface, CellSurface.Plain, cellName),
                SurfaceSpeed = raw.SurfaceSpeed,
                SurfacePalette = raw.SurfacePalette,
                DecalFamily = ParseEnum(raw.DecalFamily, TerrainDecalFamily.None, cellName),
                RimGroup = raw.RimGroup,
                Shape = ParseEnum(raw.Shape, CellShape.Flat, cellName),
                MapColorHEX = raw.MapColorHEX,
            };

            result[cellType] = def;
            s_blockArray[(byte)cellType] = def;
        }

        return result;
    }

    // Неизвестное имя — ошибка конфига, а не повод тихо нарисовать тип
    // значением по умолчанию.
    private static T ParseEnum<T>(string? value, T fallback, string cellName)
        where T : struct, Enum
    {
        if (string.IsNullOrEmpty(value))
        {
            return fallback;
        }

        return Enum.TryParse(value, ignoreCase: true, out T parsed)
            ? parsed
            : throw new InvalidDataException($"Cell '{cellName}': unknown {typeof(T).Name} '{value}'.");
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

    private sealed class RawCellDefinition
    {
        // 1. Физика и базовые свойства
        public bool Passable { get; set; }

        // 2. Светотень и освещение
        public bool EmitsLight { get; set; }

        // 3. Поверхность и декаль
        public string? Surface { get; set; }

        public byte SurfaceSpeed { get; set; }

        public byte SurfacePalette { get; set; }

        public string? DecalFamily { get; set; }

        // 4. Геометрия и кайма
        public byte RimGroup { get; set; }

        public string? Shape { get; set; }

        // 5. Карта
        public string? MapColorHEX { get; set; }
    }
}
