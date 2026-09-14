#nullable enable

using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae.World.Terrain;

// Текстуры данных клетки террейна: по текселю на квад, по две строки на
// клетку (фон, передний план), плюс сетка смещений искажения на узлах.
//
// Адрес кольцевой: тексель клетки — её мировая координата по модулю размера
// сетки, узел — по модулю размера сетки узлов. Окно камеры покрывает ровно
// один полный круг, поэтому записанная клетка остаётся на месте, пока видна.
//
// Сборка идёт в управляемые массивы (их можно заполнять из Parallel.For).
// На GPU уходит только изменённый прямоугольник: он копируется в маленькую
// текстуру-заплатку и переносится Graphics.CopyTexture. Заплатка при копании
// почти каждый кадр, и выгрузка всей текстуры на каждой была дороже, чем
// прежняя частичная заливка вершин.
public sealed class TerrainCellDataTextures : IDisposable
{
    public static readonly int ColorId = Shader.PropertyToID("_TerrainCellColor");
    public static readonly int MetaId = Shader.PropertyToID("_TerrainCellMeta");
    public static readonly int AtlasRectId = Shader.PropertyToID("_TerrainCellAtlasRect");
    public static readonly int TileSizeId = Shader.PropertyToID("_TerrainCellTileSize");
    public static readonly int AnimationId = Shader.PropertyToID("_TerrainCellAnimation");
    public static readonly int WorldId = Shader.PropertyToID("_TerrainCellWorld");
    public static readonly int GlowId = Shader.PropertyToID("_TerrainCellGlow");
    public static readonly int GridOffsetsId = Shader.PropertyToID("_TerrainGridOffsets");
    public static readonly int GridSizeId = Shader.PropertyToID("_TerrainCellGridSize");
    public static readonly int OriginId = Shader.PropertyToID("_TerrainCellOrigin");
    public static readonly int ViewOffsetId = Shader.PropertyToID("_TerrainCellViewOffset");

    private sealed class Channel<T>(TextureFormat format, string name)
        where T : struct
    {
        public Texture2D? Target;
        public Texture2D? Patch;
        public T[] Data = [];

        public void Allocate(int width, int height)
        {
            Target = Create(width, height, format, name);
            Data = new T[width * height];
        }

        public void UploadAll()
        {
            NativeArray<T> pixels = Target!.GetPixelData<T>(0);
            pixels.CopyFrom(Data);
            Target.Apply(false, false);
        }

        public void UploadRect(int x, int y, int width, int height)
        {
            int textureWidth = Target!.width;
            if (Patch == null || Patch.width < width || Patch.height < height)
            {
                DestroyTexture(ref Patch);
                Patch = Create(
                    Mathf.NextPowerOfTwo(Math.Max(width, 16)),
                    Mathf.NextPowerOfTwo(Math.Max(height, 16)),
                    format,
                    name + "Patch");
            }

            NativeArray<T> pixels = Patch.GetPixelData<T>(0);
            for (int row = 0; row < height; row++)
            {
                NativeArray<T>.Copy(Data, ((y + row) * textureWidth) + x, pixels, row * Patch.width, width);
            }

            Patch.Apply(false, false);
            Graphics.CopyTexture(Patch, 0, 0, 0, 0, width, height, Target, 0, 0, x, y);
        }

        public void Destroy()
        {
            DestroyTexture(ref Target);
            DestroyTexture(ref Patch);
            Data = [];
        }
    }

    private readonly Channel<Color32> _color = new(TextureFormat.RGBA32, "TerrainCellColor");
    private readonly Channel<Color32> _meta = new(TextureFormat.RGBA32, "TerrainCellMeta");
    private readonly Channel<TerrainHalfTexel> _atlasRect = new(TextureFormat.RGBAHalf, "TerrainCellAtlasRect");
    private readonly Channel<TerrainHalfTexel> _tileSize = new(TextureFormat.RGBAHalf, "TerrainCellTileSize");
    private readonly Channel<TerrainHalfTexel> _animation = new(TextureFormat.RGBAHalf, "TerrainCellAnimation");
    private readonly Channel<Vector4> _world = new(TextureFormat.RGBAFloat, "TerrainCellWorld");
    private readonly Channel<Vector4> _glow = new(TextureFormat.RGBAFloat, "TerrainCellGlow");
    private readonly Channel<Vector4> _gridOffsets = new(TextureFormat.RGBAFloat, "TerrainGridOffsets");

    private readonly TerrainDirtyRegion _dirty = new();
    private bool _offsetsDirty;

    public int MeshWidth { get; private set; }

    public int MeshHeight { get; private set; }

    public bool IsAllocated => _color.Target != null;

    public static int Ring(int value, int size)
    {
        int remainder = value % size;
        return remainder < 0 ? remainder + size : remainder;
    }

    public void EnsureCapacity(int meshWidth, int meshHeight)
    {
        if (IsAllocated && MeshWidth == meshWidth && MeshHeight == meshHeight)
        {
            return;
        }

        Dispose();
        MeshWidth = meshWidth;
        MeshHeight = meshHeight;
        int height = meshHeight * TerrainCellDataPacker.LayersPerCell;
        _color.Allocate(meshWidth, height);
        _meta.Allocate(meshWidth, height);
        _atlasRect.Allocate(meshWidth, height);
        _tileSize.Allocate(meshWidth, height);
        _animation.Allocate(meshWidth, height);
        _world.Allocate(meshWidth, height);
        _glow.Allocate(meshWidth, height);
        _gridOffsets.Allocate(meshWidth + 1, meshHeight + 1);
        _offsetsDirty = true;
        _dirty.Reset(meshWidth, meshHeight);
    }

    // Полная сборка пишет клетки из нескольких потоков: прямоугольник по
    // ним не копится, выгружается всё.
    public void MarkAllDirty() => _dirty.MarkAll();

    // Прямоугольник клеток в кольцевых координатах; на шве кольца он
    // разрезается, чтобы не растягиваться на всю текстуру.
    public void MarkCells(int ringX, int ringY, int width, int height) =>
        _dirty.MarkCells(ringX, ringY, width, height);

    public void SetCell(int ringX, int ringY, int layer, TerrainCellTexels texels)
    {
        int row = (ringY * TerrainCellDataPacker.LayersPerCell) + layer;
        int index = (row * MeshWidth) + ringX;
        _color.Data[index] = texels.Color;
        _meta.Data[index] = texels.Meta;
        _atlasRect.Data[index] = texels.AtlasRect;
        _tileSize.Data[index] = texels.TileSize;
        _animation.Data[index] = texels.Animation;
        _world.Data[index] = texels.World;
        _glow.Data[index] = texels.Glow;
    }

    // offsets — локальные узлы окна [0, W] × [0, H], minX/minY — мировой узел (0, 0).
    public void WriteGridOffsets(Vector3[,] offsets, int minX, int minY)
    {
        if (!IsAllocated)
        {
            return;
        }

        int nodesWide = MeshWidth + 1;
        int nodesHigh = MeshHeight + 1;
        int width = Math.Min(offsets.GetLength(0), nodesWide);
        int height = Math.Min(offsets.GetLength(1), nodesHigh);
        for (int x = 0; x < width; x++)
        {
            int ringX = Ring(minX + x, nodesWide);
            for (int y = 0; y < height; y++)
            {
                Vector3 offset = offsets[x, y];
                _gridOffsets.Data[(Ring(minY + y, nodesHigh) * nodesWide) + ringX] =
                    new Vector4(offset.x, offset.y, offset.z, 0f);
            }
        }

        _offsetsDirty = true;
    }

    public void Apply()
    {
        if (!IsAllocated)
        {
            return;
        }

        if (_offsetsDirty)
        {
            _offsetsDirty = false;
            _gridOffsets.UploadAll();
        }

        if (_dirty.IsEmpty)
        {
            return;
        }

        int textureHeight = MeshHeight * TerrainCellDataPacker.LayersPerCell;

        // Изменённое больше половины текстуры выгружается целиком: копии по
        // кускам тогда дороже одной полной выгрузки.
        bool full = _dirty.IsAll ||
            _dirty.Area * 2 >= (long)MeshWidth * textureHeight ||
            SystemInfo.copyTextureSupport == CopyTextureSupport.None;
        if (full)
        {
            _color.UploadAll();
            _meta.UploadAll();
            _atlasRect.UploadAll();
            _tileSize.UploadAll();
            _animation.UploadAll();
            _world.UploadAll();
            _glow.UploadAll();
        }
        else
        {
            for (int i = 0; i < _dirty.Count; i++)
            {
                RectInt rect = _dirty[i];
                _color.UploadRect(rect.x, rect.y, rect.width, rect.height);
                _meta.UploadRect(rect.x, rect.y, rect.width, rect.height);
                _atlasRect.UploadRect(rect.x, rect.y, rect.width, rect.height);
                _tileSize.UploadRect(rect.x, rect.y, rect.width, rect.height);
                _animation.UploadRect(rect.x, rect.y, rect.width, rect.height);
                _world.UploadRect(rect.x, rect.y, rect.width, rect.height);
                _glow.UploadRect(rect.x, rect.y, rect.width, rect.height);
            }
        }

        _dirty.Clear();
    }

    // Глобально, а не в материал: свойства вне UnityPerMaterial выключили бы
    // SRP Batcher на всём шейдере террейна.
    public void BindGlobals(float cellSize, int originX, int originY, bool distortion)
    {
        if (!IsAllocated)
        {
            return;
        }

        Shader.SetGlobalTexture(ColorId, _color.Target);
        Shader.SetGlobalTexture(MetaId, _meta.Target);
        Shader.SetGlobalTexture(AtlasRectId, _atlasRect.Target);
        Shader.SetGlobalTexture(TileSizeId, _tileSize.Target);
        Shader.SetGlobalTexture(AnimationId, _animation.Target);
        Shader.SetGlobalTexture(WorldId, _world.Target);
        Shader.SetGlobalTexture(GlowId, _glow.Target);
        Shader.SetGlobalTexture(GridOffsetsId, _gridOffsets.Target);
        Shader.SetGlobalVector(GridSizeId, new Vector4(MeshWidth, MeshHeight, cellSize, distortion ? 1f : 0f));
        Shader.SetGlobalVector(OriginId, new Vector4(originX, originY, 0f, 0f));
    }

    public void Dispose()
    {
        _color.Destroy();
        _meta.Destroy();
        _atlasRect.Destroy();
        _tileSize.Destroy();
        _animation.Destroy();
        _world.Destroy();
        _glow.Destroy();
        _gridOffsets.Destroy();
        MeshWidth = 0;
        MeshHeight = 0;
    }

    private static Texture2D Create(int width, int height, TextureFormat format, string name) =>
        new(width, height, format, mipChain: false, linear: true)
        {
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
        };

    private static void DestroyTexture(ref Texture2D? texture)
    {
        if (texture == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(texture);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }

        texture = null;
    }
}
