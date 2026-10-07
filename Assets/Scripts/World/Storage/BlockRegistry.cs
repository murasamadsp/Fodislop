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
    private static CellType s_underlayType;
    private static readonly Dictionary<CellType, BlockDefinition> s_blocks = LoadRegistry();
    private static readonly BlockRegistry s_defaultInstance = new();

    public static IBlockRegistry Default => s_defaultInstance;

    public static IReadOnlyDictionary<CellType, BlockDefinition> Blocks => s_blocks;

    /// <summary>Тип с drawLayer: Underlay — подложка под каждым передним планом.</summary>
    public static CellType UnderlayType
    {
        get
        {
            _ = s_blocks;
            return s_underlayType;
        }
    }

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

        RejectUnknownKeys(json);
        var result = new Dictionary<CellType, BlockDefinition>(rawMap.Count);
        Array.Clear(s_blockArray, 0, s_blockArray.Length);

        foreach ((string cellName, RawCellDefinition raw) in rawMap)
        {
            if (!Enum.TryParse(cellName, out CellType cellType))
            {
                throw new InvalidDataException($"Cell '{cellName}': no such CellType.");
            }

            var def = new BlockDefinition(
                DrawLayer: ParseEnum<CellDrawLayer>(raw.DrawLayer, cellName, "drawLayer"),
                Outline: ParseEnum<CellOutline>(raw.Outline, cellName, "outline"),
                TextureAnchor: ParseEnum<CellTextureAnchor>(raw.TextureAnchor, cellName, "textureAnchor"),
                AnimationType: ParseEnum<CellAnimationType>(raw.AnimationType, cellName, "animationType"),
                AnimationSpeed: Required(raw.AnimationSpeed, cellName, "animationSpeed"),
                SurfaceEffect: ParseEnum<CellSurfaceEffect>(raw.SurfaceEffect, cellName, "surfaceEffect"),
                SurfaceEffectPalette: Required(raw.SurfaceEffectPalette, cellName, "surfaceEffectPalette"),
                DecalAtlas: ParseEnum<CellDecalAtlas>(raw.DecalAtlas, cellName, "decalAtlas"),
                RimMass: Required(raw.RimMass, cellName, "rimMass"),
                Glow: Required(raw.Glow, cellName, "glow"),
                MapColor: ParseColor(raw.MapColor, cellName, "mapColor"));
            if (def.Glow is < 0f or > 1f)
            {
                throw new InvalidDataException($"Cell '{cellName}': glow {def.Glow} is outside 0..1.");
            }

            result[cellType] = def;
            s_blockArray[(byte)cellType] = def;
        }

        s_underlayType = ResolveUnderlay(result);
        return result;
    }

    // Подложка — ровно один тип с drawLayer: Underlay.
    private static CellType ResolveUnderlay(Dictionary<CellType, BlockDefinition> blocks)
    {
        var underlays = new List<CellType>();
        foreach ((CellType type, BlockDefinition def) in blocks)
        {
            if (def.DrawLayer == CellDrawLayer.Underlay)
            {
                underlays.Add(type);
            }
        }

        if (underlays.Count != 1)
        {
            throw new InvalidDataException(
                $"cells.json: exactly one cell must have \"drawLayer\": \"Underlay\", found {underlays.Count}.");
        }

        return underlays[0];
    }

    // Ключ с опечаткой иначе молча пропал бы, а поле упало бы на «missing».
    private static void RejectUnknownKeys(string json)
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Reflection.PropertyInfo property in typeof(RawCellDefinition).GetProperties())
        {
            known.Add(property.Name);
        }

        using JsonDocument document = JsonDocument.Parse(json);
        foreach (JsonProperty cell in document.RootElement.EnumerateObject())
        {
            foreach (JsonProperty field in cell.Value.EnumerateObject())
            {
                if (!known.Contains(field.Name))
                {
                    throw new InvalidDataException($"Cell '{cell.Name}': unknown key '{field.Name}'.");
                }
            }
        }
    }

    // Каждый ключ обязателен: пропуск — ошибка конфига, а не повод тихо
    // подставить значение, выведенное из других полей.
    private static T Required<T>(T? value, string cellName, string key)
        where T : struct =>
        value ?? throw new InvalidDataException($"Cell '{cellName}': missing '{key}'.");

    private static T ParseEnum<T>(string? value, string cellName, string key)
        where T : struct, Enum
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidDataException($"Cell '{cellName}': missing '{key}'.");
        }

        return Enum.TryParse(value, ignoreCase: true, out T parsed) && Enum.IsDefined(typeof(T), parsed)
            ? parsed
            : throw new InvalidDataException($"Cell '{cellName}': unknown {key} '{value}'.");
    }

    // #RRGGBB или #RRGGBBAA.
    private static Color32 ParseColor(string? value, string cellName, string key)
    {
        ReadOnlySpan<char> hex = value.AsSpan();
        if (hex.StartsWith("#"))
        {
            hex = hex[1..];
        }

        if ((hex.Length == 6 || hex.Length == 8) &&
            uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint bits))
        {
            uint rgba = hex.Length == 6 ? (bits << 8) | 0xFFu : bits;
            return new Color32((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
        }

        throw new InvalidDataException($"Cell '{cellName}': '{key}' must be #RRGGBB or #RRGGBBAA, got '{value}'.");
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

    // Ключи cells.json — те же имена, что у BlockDefinition.
    private sealed class RawCellDefinition
    {
        public string? DrawLayer { get; set; }

        public string? Outline { get; set; }

        public string? TextureAnchor { get; set; }

        public string? AnimationType { get; set; }

        public float? AnimationSpeed { get; set; }

        public string? SurfaceEffect { get; set; }

        public byte? SurfaceEffectPalette { get; set; }

        public string? DecalAtlas { get; set; }

        public byte? RimMass { get; set; }

        public float? Glow { get; set; }

        public string? MapColor { get; set; }
    }
}
