#nullable enable

using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Terrain;

// Текстуры данных клетки террейна: по текселю на квад, по две строки на
// клетку (фон, передний план), включая канонические четыре угла геометрии.
//
// Адрес кольцевой: тексель клетки — её мировая координата по модулю размера
// сетки. Окно камеры покрывает ровно один полный круг, поэтому записанная
// клетка остаётся на месте, пока видна.
//
// Сборка идёт в управляемые массивы (их можно заполнять из Parallel.For).
// На GPU уходит только изменённый прямоугольник: он копируется в маленькую
// текстуру-заплатку и переносится Graphics.CopyTexture. Геометрия входит в
// тот же прямоугольник, поэтому cell-data и форма клетки не расходятся.
public sealed class TerrainCellDataTextures : IDisposable
{
    // UploadRect() always applies at least a 16x16 patch. Treat that fixed
    // setup/copy cost as texel work when choosing between many patches and
    // one full upload; this is a cost governor, not a patch-count cutoff.
    private const long PatchSetupEquivalentTexels = 16L * 16L;

    public static readonly int ColorID = Shader.PropertyToID("_TerrainCellColor");
    public static readonly int MetaID = Shader.PropertyToID("_TerrainCellMeta");
    public static readonly int AtlasRectID = Shader.PropertyToID("_TerrainCellAtlasRect");
    public static readonly int TileSizeID = Shader.PropertyToID("_TerrainCellTileSize");
    public static readonly int AnimationID = Shader.PropertyToID("_TerrainCellAnimation");
    public static readonly int WorldID = Shader.PropertyToID("_TerrainCellWorld");
    public static readonly int GlowID = Shader.PropertyToID("_TerrainCellGlow");
    public static readonly int GeometryXID = Shader.PropertyToID("_TerrainCellGeometryX");
    public static readonly int GeometryYID = Shader.PropertyToID("_TerrainCellGeometryY");
    public static readonly int GridSizeID = Shader.PropertyToID("_TerrainCellGridSize");
    public static readonly int OriginID = Shader.PropertyToID("_TerrainCellOrigin");
    public static readonly int ViewOffsetID = Shader.PropertyToID("_TerrainCellViewOffset");

    private sealed class Channel<T>(TextureFormat format, string name)
        where T : struct
    {
        // Одна промежуточная текстура на канал, ПОСТОЯННОГО размера.
        //
        // ЗАЧЕМ ИМЕННО ТАК. Загрузить в Texture2D кусок нельзя: Apply()
        // отправляет текстуру целиком. Поэтому прямоугольник сначала
        // набивается в маленькую текстуру, а потом переносится на место
        // командой GPU.
        //
        // НИКАКИХ ПРЕДПОЛОЖЕНИЙ О ФОРМЕ. Раньше размер подгонялся под
        // прямоугольник, и текстура пересоздавалась, как только форма
        // менялась, — девять штук за кадр, по 30+ мс. Пул под «ожидаемые»
        // формы это лечил ровно до первого неожиданного прямоугольника, а
        // прямоугольники приходят с сервера: любой поток изменений мира даёт
        // любую форму, и тогда пул промахивается каждый кадр.
        //
        // Постоянный размер снимает вопрос. Ширина — во всю текстуру, потому
        // что шире прямоугольник быть не может; высота — фиксированная
        // полоска. Любой прямоугольник режется на такие полоски по высоте и
        // грузится за несколько переносов. Текстура создаётся один раз и
        // живёт, сколько живёт окно.
        //
        // Лишняя площадь при этом грузится (полоска 33 текселя шириной
        // занимает её всю), и это осознанно: замер показал, что загрузка
        // стоит 0.0-0.1 мс, а создание текстуры — десятки миллисекунд.
        // Плата за предсказуемость берётся там, где она почти бесплатна.
        private const int StagingRows = 128;

        public Texture2D? Target;
        public Texture2D? Staging;
        public T[] Data = [];

        public static long CopyTicks;
        public static long ApplyTicks;

        public void Allocate(int width, int height)
        {
            Target = Create(width, height, format, name);
            Staging = Create(width, Math.Min(StagingRows, height), format, name + "Staging");
            Data = new T[width * height];
        }

        public void UploadAll()
        {
            NativeArray<T> pixels = Target!.GetPixelData<T>(0);
            pixels.CopyFrom(Data);
            Target.Apply(false, false);
        }

        /// <summary>Высота полоски, которой режется прямоугольник любой формы.</summary>
        public int StagingHeight => Staging!.height;

        /// <summary>Набить полоску прямоугольника и отдать её на GPU.</summary>
        ///
        /// Разделено с переносом намеренно. Apply() — это загрузка с
        /// синхронизацией, CopyTexture — команда GPU; когда они чередуются по
        /// девяти каналам, кадр платит за девять точек синхронизации вместо
        /// одной. Сначала набиваются все каналы, потом переносятся все.
        public void StageStrip(int x, int y, int width, int height)
        {
            long copyStart = System.Diagnostics.Stopwatch.GetTimestamp();
            int textureWidth = Target!.width;
            NativeArray<T> pixels = Staging!.GetPixelData<T>(0);
            for (int row = 0; row < height; row++)
            {
                NativeArray<T>.Copy(Data, ((y + row) * textureWidth) + x, pixels, row * Staging.width, width);
            }

            CopyTicks += System.Diagnostics.Stopwatch.GetTimestamp() - copyStart;

            long applyStart = System.Diagnostics.Stopwatch.GetTimestamp();
            Staging.Apply(false, false);
            ApplyTicks += System.Diagnostics.Stopwatch.GetTimestamp() - applyStart;
        }

        public void CopyStagedStrip(int x, int y, int width, int height)
        {
            Graphics.CopyTexture(Staging!, 0, 0, 0, 0, width, height, Target!, 0, 0, x, y);
        }

        public void Destroy()
        {
            DestroyTexture(ref Target);
            DestroyTexture(ref Staging);
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
    private readonly Channel<TerrainHalfTexel> _geometryX = new(TextureFormat.RGBAHalf, "TerrainCellGeometryX");
    private readonly Channel<TerrainHalfTexel> _geometryY = new(TextureFormat.RGBAHalf, "TerrainCellGeometryY");

    private readonly TerrainDirtyRegion _dirty = new();

    public int MeshWidth { get; private set; }

    public int MeshHeight { get; private set; }

    public bool IsAllocated => _color.Target != null;

    /// <summary>Чем была последняя выгрузка: сколько прямоугольников и текселей.</summary>
    ///
    /// Ноль прямоугольников при ненулевых текселях означает выгрузку целиком.
    /// Цена выгрузки — самая крупная незакрытая статья в модели стоимости
    /// пересборки, и без этих двух чисел из лога нельзя отличить «выгрузили
    /// полосу» от «выгрузили девять текстур целиком».
    public int LastUploadRectCount { get; private set; }

    public long LastUploadTexels { get; private set; }

    /// <summary>На сколько полосок разошлась последняя выгрузка.</summary>
    ///
    /// Прямоугольник любой формы грузится полосками постоянного размера,
    /// поэтому число полосок — это вся зависимость выгрузки от формы. Растёт
    /// линейно с высотой изменённой области и ни от чего больше не зависит.
    public int LastUploadStrips { get; private set; }

    /// <summary>Сколько из выгрузки ушло в набивку и загрузку промежуточных текстур.</summary>
    public float LastStageMs { get; private set; }

    /// <summary>Из набивки: копирование строк в промежуточную текстуру.</summary>
    public float LastStageCopyMs { get; private set; }

    /// <summary>Из набивки: загрузка промежуточной текстуры на GPU.</summary>
    public float LastStageApplyMs { get; private set; }

    private static void ResetStageCounters()
    {
        Channel<Color32>.CopyTicks = 0;
        Channel<Color32>.ApplyTicks = 0;
        Channel<TerrainHalfTexel>.CopyTicks = 0;
        Channel<TerrainHalfTexel>.ApplyTicks = 0;
        Channel<Vector4>.CopyTicks = 0;
        Channel<Vector4>.ApplyTicks = 0;
    }

    private static float TicksToMs(long ticks) =>
        (float)(ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

    private static float ElapsedMs(long startTimestamp) =>
        (float)((System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 /
            System.Diagnostics.Stopwatch.Frequency);

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
        _geometryX.Allocate(meshWidth, height);
        _geometryY.Allocate(meshWidth, height);
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
        _geometryX.Data[index] = texels.GeometryX;
        _geometryY.Data[index] = texels.GeometryY;
    }

    // Снимок текселя из управляемых массивов. Это ровно то, что уходит на
    // GPU в Apply(): ни одного преобразования между этим чтением и загрузкой
    // нет. Нужен тестам сборки как сравнимый результат.
    internal TerrainCellTexels GetCell(int ringX, int ringY, int layer)
    {
        int row = (ringY * TerrainCellDataPacker.LayersPerCell) + layer;
        int index = (row * MeshWidth) + ringX;
        return new TerrainCellTexels(
            _color.Data[index],
            _meta.Data[index],
            _atlasRect.Data[index],
            _tileSize.Data[index],
            _animation.Data[index],
            _world.Data[index],
            _glow.Data[index],
            _geometryX.Data[index],
            _geometryY.Data[index]);
    }

    public void Apply()
    {
        if (!IsAllocated)
        {
            return;
        }

        if (_dirty.IsEmpty)
        {
            return;
        }

        int textureHeight = MeshHeight * TerrainCellDataPacker.LayersPerCell;
        long patchWork = _dirty.Area + (_dirty.Count * PatchSetupEquivalentTexels);

        // Area includes both changed texels and the fixed setup cost of each
        // patch command, so a full upload is selected when it is cheaper.
        bool full = _dirty.IsAll ||
            patchWork >= (long)MeshWidth * textureHeight ||
            SystemInfo.copyTextureSupport == CopyTextureSupport.None;
        if (full)
        {
            LastUploadRectCount = 0;
            LastUploadTexels = (long)MeshWidth * textureHeight;
            LastUploadStrips = 0;
            LastStageMs = 0f;
            LastStageCopyMs = 0f;
            LastStageApplyMs = 0f;
            _color.UploadAll();
            _meta.UploadAll();
            _atlasRect.UploadAll();
            _tileSize.UploadAll();
            _animation.UploadAll();
            _world.UploadAll();
            _glow.UploadAll();
            _geometryX.UploadAll();
            _geometryY.UploadAll();
        }
        else
        {
            LastUploadRectCount = _dirty.Count;
            LastUploadTexels = _dirty.Area;
            LastUploadStrips = 0;
            ResetStageCounters();
            long stageStart = System.Diagnostics.Stopwatch.GetTimestamp();
            int stagingRows = _color.StagingHeight;
            for (int i = 0; i < _dirty.Count; i++)
            {
                RectInt rect = _dirty[i];

                // Прямоугольник любой формы режется по высоте на полоски
                // постоянного размера (см. TerrainUploadStrips).
                int strips = TerrainUploadStrips.Count(rect.height, stagingRows);
                for (int strip = 0; strip < strips; strip++)
                {
                    RectInt band = TerrainUploadStrips.At(rect, stagingRows, strip);
                    int stripHeight = band.height;
                    int stripY = band.y;
                    LastUploadStrips++;

                    _color.StageStrip(rect.x, stripY, rect.width, stripHeight);
                    _meta.StageStrip(rect.x, stripY, rect.width, stripHeight);
                    _atlasRect.StageStrip(rect.x, stripY, rect.width, stripHeight);
                    _tileSize.StageStrip(rect.x, stripY, rect.width, stripHeight);
                    _animation.StageStrip(rect.x, stripY, rect.width, stripHeight);
                    _world.StageStrip(rect.x, stripY, rect.width, stripHeight);
                    _glow.StageStrip(rect.x, stripY, rect.width, stripHeight);
                    _geometryX.StageStrip(rect.x, stripY, rect.width, stripHeight);
                    _geometryY.StageStrip(rect.x, stripY, rect.width, stripHeight);

                    _color.CopyStagedStrip(rect.x, stripY, rect.width, stripHeight);
                    _meta.CopyStagedStrip(rect.x, stripY, rect.width, stripHeight);
                    _atlasRect.CopyStagedStrip(rect.x, stripY, rect.width, stripHeight);
                    _tileSize.CopyStagedStrip(rect.x, stripY, rect.width, stripHeight);
                    _animation.CopyStagedStrip(rect.x, stripY, rect.width, stripHeight);
                    _world.CopyStagedStrip(rect.x, stripY, rect.width, stripHeight);
                    _glow.CopyStagedStrip(rect.x, stripY, rect.width, stripHeight);
                    _geometryX.CopyStagedStrip(rect.x, stripY, rect.width, stripHeight);
                    _geometryY.CopyStagedStrip(rect.x, stripY, rect.width, stripHeight);
                }
            }

            LastStageMs = ElapsedMs(stageStart);
            LastStageCopyMs = TicksToMs(
                Channel<Color32>.CopyTicks + Channel<TerrainHalfTexel>.CopyTicks +
                Channel<Vector4>.CopyTicks);
            LastStageApplyMs = TicksToMs(
                Channel<Color32>.ApplyTicks + Channel<TerrainHalfTexel>.ApplyTicks +
                Channel<Vector4>.ApplyTicks);
        }

        _dirty.Clear();
    }

    // Глобально, а не в материал: свойства вне UnityPerMaterial выключили бы
    // SRP Batcher на всём шейдере террейна.
    public void BindGlobals(float cellSize, int originX, int originY)
    {
        if (!IsAllocated)
        {
            return;
        }

        Shader.SetGlobalTexture(ColorID, _color.Target);
        Shader.SetGlobalTexture(MetaID, _meta.Target);
        Shader.SetGlobalTexture(AtlasRectID, _atlasRect.Target);
        Shader.SetGlobalTexture(TileSizeID, _tileSize.Target);
        Shader.SetGlobalTexture(AnimationID, _animation.Target);
        Shader.SetGlobalTexture(WorldID, _world.Target);
        Shader.SetGlobalTexture(GlowID, _glow.Target);
        Shader.SetGlobalTexture(GeometryXID, _geometryX.Target);
        Shader.SetGlobalTexture(GeometryYID, _geometryY.Target);
        Shader.SetGlobalVector(GridSizeID, new Vector4(MeshWidth, MeshHeight, cellSize, 0f));
        Shader.SetGlobalVector(OriginID, new Vector4(originX, originY, 0f, 0f));
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
        _geometryX.Destroy();
        _geometryY.Destroy();
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
