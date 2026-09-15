#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Fodinae.World.Lighting;

// Lamp light, kept per lamp until the lamp or what it lights changes.
//
// A lamp's light depends only on its position, colour and the material field.
// A lamp that has not changed since its tile was traced would trace to the
// very same numbers, so its tile is reused as is. Lamps that moved, changed,
// appeared, or lost their tiles to a geometry change are traced in the same
// frame. There is no update rate: every change is traced when it happens.
internal sealed class DynamicLightTileCache
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct TileInfo
    {
        public readonly int FieldOriginX;
        public readonly int FieldOriginY;
        public readonly int SizeX;
        public readonly int SizeY;
        public readonly int TileOffsetX;
        public readonly int TileOffsetY;

        public TileInfo(RectInt fieldRect, Vector2Int tileOffset)
        {
            FieldOriginX = fieldRect.x;
            FieldOriginY = fieldRect.y;
            SizeX = fieldRect.width;
            SizeY = fieldRect.height;
            TileOffsetX = tileOffset.x;
            TileOffsetY = tileOffset.y;
        }
    }

    public const int TileInfoStride = sizeof(int) * 6;

    // Tile sizes round up to this many texels so a slightly brighter lamp
    // does not reallocate the atlas.
    private const int TileQuantum = 64;

    private readonly Dictionary<int, int> _slotByLightId = new();
    private int[] _lightIdBySlot = Array.Empty<int>();
    private bool[] _slotInUse = Array.Empty<bool>();
    private int[] _slotSeenFrame = Array.Empty<int>();
    private bool[] _slotValid = Array.Empty<bool>();
    private Vector4[] _slotPosition = Array.Empty<Vector4>();
    private Vector4[] _slotColor = Array.Empty<Vector4>();
    private RectInt[] _slotRect = Array.Empty<RectInt>();
    private int _frame;
    private int _columns = 1;
    private int _tileWidth;
    private int _tileHeight;

    public RenderTexture? Tiles { get; private set; }

    public ComputeBuffer? TileInfos { get; private set; }

    // Optical depth along lamp-centred rays of the lamp being traced:
    // column = ray angle, row = distance in texels. Scratch for one lamp at a
    // time; float, because depths through rock grow past half precision.
    public RenderTexture? Polar { get; private set; }

    public void EnsurePolar(int angles, int radii)
    {
        if (Polar != null && Polar.width >= angles && Polar.height >= radii)
        {
            return;
        }

        int width = Math.Max(angles, Polar?.width ?? 0);
        int height = Math.Max(radii, Polar?.height ?? 0);
        if (width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
        {
            throw new InvalidOperationException(
                $"Lamp ray texture {width}x{height} exceeds the maximum texture size {SystemInfo.maxTextureSize}.");
        }

        ReleasePolar();
        var polar = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        {
            enableRandomWrite = true,
            useMipMap = false,
            autoGenerateMips = false,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "_LampRayDepth",
        };
        if (!polar.Create())
        {
            DestroyObject(polar);
            throw new InvalidOperationException("Failed to create the lamp ray texture.");
        }

        Polar = polar;
    }

    private void ReleasePolar()
    {
        if (Polar != null)
        {
            Polar.Release();
            DestroyObject(Polar);
            Polar = null;
        }
    }

    public int Capacity { get; private set; }

    // Grows the atlas when a lamp rectangle or the lamp count no longer fits.
    // A new atlas holds no traced light, so every tile is invalidated.
    public void EnsureLayout(int requiredTileWidth, int requiredTileHeight, int lightCount)
    {
        int tileWidth = Math.Max(_tileWidth, RoundUpToQuantum(requiredTileWidth));
        int tileHeight = Math.Max(_tileHeight, RoundUpToQuantum(requiredTileHeight));
        int capacity = Math.Max(Capacity, Mathf.NextPowerOfTwo(Math.Max(1, lightCount)));
        if (Tiles != null &&
            tileWidth == _tileWidth &&
            tileHeight == _tileHeight &&
            capacity == Capacity)
        {
            return;
        }

        int columns = Mathf.CeilToInt(Mathf.Sqrt(capacity));
        int rows = Mathf.CeilToInt(capacity / (float)columns);
        int atlasWidth = columns * tileWidth;
        int atlasHeight = rows * tileHeight;
        if (atlasWidth > SystemInfo.maxTextureSize || atlasHeight > SystemInfo.maxTextureSize)
        {
            throw new InvalidOperationException(
                $"Lamp light atlas {atlasWidth}x{atlasHeight} for {capacity} lamps exceeds the " +
                $"maximum texture size {SystemInfo.maxTextureSize}.");
        }

        ReleaseGpuResources();
        var tiles = new RenderTexture(atlasWidth, atlasHeight, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
        {
            enableRandomWrite = true,
            useMipMap = false,
            autoGenerateMips = false,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "_LampLightTiles",
        };
        if (!tiles.Create())
        {
            DestroyObject(tiles);
            throw new InvalidOperationException("Failed to create the lamp light atlas.");
        }

        Tiles = tiles;
        TileInfos = new ComputeBuffer(capacity, TileInfoStride, ComputeBufferType.Structured);

        Array.Resize(ref _lightIdBySlot, capacity);
        Array.Resize(ref _slotInUse, capacity);
        Array.Resize(ref _slotSeenFrame, capacity);
        Array.Resize(ref _slotValid, capacity);
        Array.Resize(ref _slotPosition, capacity);
        Array.Resize(ref _slotColor, capacity);
        Array.Resize(ref _slotRect, capacity);
        Capacity = capacity;
        _columns = columns;
        _tileWidth = tileWidth;
        _tileHeight = tileHeight;
        InvalidateAll();
    }

    // Every tile must be traced again: the material field, the field layout,
    // or a debug view changed what a lamp's light is.
    public void InvalidateAll()
    {
        Array.Clear(_slotValid, 0, _slotValid.Length);
    }

    // Keeps the slots of lamps still present, frees those of lamps gone, and
    // gives new lamps free slots. Call once per solve, before SlotOf.
    public void AssignSlots(ReadOnlySpan<int> lightIds)
    {
        _frame++;
        for (int i = 0; i < lightIds.Length; i++)
        {
            if (_slotByLightId.TryGetValue(lightIds[i], out int slot))
            {
                _slotSeenFrame[slot] = _frame;
            }
        }

        for (int slot = 0; slot < Capacity; slot++)
        {
            if (_slotInUse[slot] && _slotSeenFrame[slot] != _frame)
            {
                _slotByLightId.Remove(_lightIdBySlot[slot]);
                _slotInUse[slot] = false;
                _slotValid[slot] = false;
            }
        }

        int freeSearch = 0;
        for (int i = 0; i < lightIds.Length; i++)
        {
            if (_slotByLightId.ContainsKey(lightIds[i]))
            {
                continue;
            }

            while (_slotInUse[freeSearch])
            {
                freeSearch++;
            }

            _slotInUse[freeSearch] = true;
            _slotValid[freeSearch] = false;
            _slotSeenFrame[freeSearch] = _frame;
            _lightIdBySlot[freeSearch] = lightIds[i];
            _slotByLightId.Add(lightIds[i], freeSearch);
        }
    }

    public int SlotOf(int lightId) => _slotByLightId[lightId];

    public Vector2Int TileOffset(int slot) =>
        new((slot % _columns) * _tileWidth, (slot / _columns) * _tileHeight);

    public bool NeedsTrace(int slot, Vector4 position, Vector4 color, RectInt fieldRect) =>
        !_slotValid[slot] ||
        _slotPosition[slot] != position ||
        _slotColor[slot] != color ||
        !_slotRect[slot].Equals(fieldRect);

    public void MarkTraced(int slot, Vector4 position, Vector4 color, RectInt fieldRect)
    {
        _slotValid[slot] = true;
        _slotPosition[slot] = position;
        _slotColor[slot] = color;
        _slotRect[slot] = fieldRect;
    }

    public void Release()
    {
        ReleaseGpuResources();
        ReleasePolar();
        _slotByLightId.Clear();
        _lightIdBySlot = Array.Empty<int>();
        _slotInUse = Array.Empty<bool>();
        _slotSeenFrame = Array.Empty<int>();
        _slotValid = Array.Empty<bool>();
        _slotPosition = Array.Empty<Vector4>();
        _slotColor = Array.Empty<Vector4>();
        _slotRect = Array.Empty<RectInt>();
        Capacity = 0;
        _columns = 1;
        _tileWidth = 0;
        _tileHeight = 0;
    }

    private void ReleaseGpuResources()
    {
        if (Tiles != null)
        {
            Tiles.Release();
            DestroyObject(Tiles);
            Tiles = null;
        }

        TileInfos?.Release();
        TileInfos = null;
    }

    private static int RoundUpToQuantum(int value) =>
        Math.Max(TileQuantum, ((value + TileQuantum - 1) / TileQuantum) * TileQuantum);

    private static void DestroyObject(UnityEngine.Object target)
    {
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(target);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
