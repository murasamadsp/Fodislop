#nullable enable

using System;
using MinesServer.Data;
using UnityEngine;

namespace Kern.UI;

/// <summary>
/// Pure CPU mip pyramid over cell averages. Level 0 holds one texel per
/// <see cref="MipBlockSize"/>-sized block of world cells; every parent level is
/// an area-weighted average of its children, with partial edge blocks weighted
/// by their real in-world cell count. A smaller block keeps the overview sharp
/// at moderate zoom instead of magnifying one texel per storage chunk. No GPU or
/// texture concerns live here so the math is unit-testable without Unity.
/// </summary>
internal sealed class WorldMapMipPyramid
{
    private const long MaxCacheBytes = 64L * 1024L * 1024L;

    // Managed level data plus the readable texture and its weight plane.
    private const int BytesPerMipTexel = sizeof(uint) + sizeof(float) + (2 * sizeof(uint));

    private readonly Color32[][] _levels;
    private readonly int[] _levelWidths;
    private readonly int[] _levelHeights;
    private readonly float[][] _levelWeights;
    private readonly Color32[] _cellColorTable;
    private readonly Color32 _unloadedColor;
    private readonly int _chunkSize;
    private readonly int _blockSize;
    private readonly int _blocksPerChunkAxis;
    private readonly int _worldWidth;
    private readonly int _worldHeight;

    private readonly double[] _blockRed;
    private readonly double[] _blockGreen;
    private readonly double[] _blockBlue;
    private readonly double[] _blockAlpha;
    private readonly int[] _blockCount;

    private int _currentChunkIndex = -1;
    private int _decodedCellCount;

    public WorldMapMipPyramid(
        int widthChunks,
        int heightChunks,
        int chunkSize,
        int worldWidth,
        int worldHeight,
        Color32[] cellColorTable,
        Color32 unloadedColor,
        int? blockSize = null)
    {
        if (widthChunks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthChunks));
        }

        if (heightChunks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heightChunks));
        }

        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize));
        }

        if (worldWidth <= (long)(widthChunks - 1) * chunkSize ||
            worldWidth > (long)widthChunks * chunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(worldWidth));
        }

        if (worldHeight <= (long)(heightChunks - 1) * chunkSize ||
            worldHeight > (long)heightChunks * chunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(worldHeight));
        }

        if (cellColorTable == null || cellColorTable.Length < 256)
        {
            throw new ArgumentException(
                "The map color table must contain all 256 cell entries.",
                nameof(cellColorTable));
        }

        int block = blockSize ?? chunkSize;
        if (block <= 0 || block > chunkSize || chunkSize % block != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(blockSize),
                block,
                "The mip block size must be a positive divisor of the storage chunk size.");
        }

        _chunkSize = chunkSize;
        _blockSize = block;
        _blocksPerChunkAxis = chunkSize / block;
        _worldWidth = worldWidth;
        _worldHeight = worldHeight;
        _cellColorTable = cellColorTable;
        _unloadedColor = unloadedColor;
        _blockRed = new double[_blocksPerChunkAxis * _blocksPerChunkAxis];
        _blockGreen = new double[_blockRed.Length];
        _blockBlue = new double[_blockRed.Length];
        _blockAlpha = new double[_blockRed.Length];
        _blockCount = new int[_blockRed.Length];

        int levelWidth = (worldWidth + block - 1) / block;
        int levelHeight = (worldHeight + block - 1) / block;
        int levelCount = 1;
        long totalPixels = (long)levelWidth * levelHeight;
        int w = levelWidth;
        int h = levelHeight;
        while (w > 1 || h > 1)
        {
            w = Mathf.Max(1, w / 2);
            h = Mathf.Max(1, h / 2);
            totalPixels += (long)w * h;
            levelCount++;
        }

        long cacheBytes = totalPixels * BytesPerMipTexel;
        if (cacheBytes > MaxCacheBytes)
        {
            throw new InvalidOperationException(
                $"World map mip cache needs {cacheBytes / (1024f * 1024f):F1} MiB; " +
                $"the per-session limit is {MaxCacheBytes / (1024f * 1024f):F0} MiB.");
        }

        _levels = new Color32[levelCount][];
        _levelWeights = new float[levelCount][];
        _levelWidths = new int[levelCount];
        _levelHeights = new int[levelCount];
        levelWidth = (worldWidth + block - 1) / block;
        levelHeight = (worldHeight + block - 1) / block;
        for (int level = 0; level < levelCount; level++)
        {
            _levelWidths[level] = levelWidth;
            _levelHeights[level] = levelHeight;
            _levels[level] = new Color32[checked(levelWidth * levelHeight)];
            _levelWeights[level] = new float[checked(levelWidth * levelHeight)];
            Array.Fill(_levels[level], _unloadedColor);
            if (level == 0)
            {
                for (int blockX = 0; blockX < levelWidth; blockX++)
                {
                    int validWidth = GetValidBlockWidth(blockX);
                    for (int blockY = 0; blockY < levelHeight; blockY++)
                    {
                        _levelWeights[level][blockY * levelWidth + blockX] =
                            validWidth * (float)GetValidBlockHeight(blockY);
                    }
                }
            }

            levelWidth = Mathf.Max(1, levelWidth / 2);
            levelHeight = Mathf.Max(1, levelHeight / 2);
        }

        for (int childLevel = 0; childLevel < levelCount - 1; childLevel++)
        {
            for (int parentY = 0; parentY < _levelHeights[childLevel + 1]; parentY++)
            {
                for (int parentX = 0; parentX < _levelWidths[childLevel + 1]; parentX++)
                {
                    _levelWeights[childLevel + 1][parentY * _levelWidths[childLevel + 1] + parentX] =
                        (float)CalculateParentWeight(childLevel, parentX, parentY);
                }
            }
        }
    }

    /// <summary>
    /// Finest power-of-two block divisor of <paramref name="chunkSize"/> whose
    /// full pyramid fits the per-session budget. Falls back to the storage chunk
    /// size when nothing smaller fits.
    /// </summary>
    public static int ChooseBlockSize(int worldWidth, int worldHeight, int chunkSize)
    {
        if (worldWidth <= 0 || worldHeight <= 0 || chunkSize <= 0)
        {
            return Math.Max(1, chunkSize);
        }

        for (int block = 1; block < chunkSize; block <<= 1)
        {
            if (chunkSize % block != 0)
            {
                break;
            }

            if (FitsCacheBudget(worldWidth, worldHeight, block))
            {
                return block;
            }
        }

        return chunkSize;
    }

    private static bool FitsCacheBudget(int worldWidth, int worldHeight, int blockSize)
    {
        int width = (worldWidth + blockSize - 1) / blockSize;
        int height = (worldHeight + blockSize - 1) / blockSize;
        long totalPixels = 0;
        while (true)
        {
            totalPixels += (long)width * height;
            if (width == 1 && height == 1)
            {
                break;
            }

            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
        }

        return totalPixels * BytesPerMipTexel <= MaxCacheBytes;
    }

    public int WidthChunks => (_worldWidth + _chunkSize - 1) / _chunkSize;

    public int HeightChunks => (_worldHeight + _chunkSize - 1) / _chunkSize;

    public int ChunkSize => _chunkSize;

    public int MipBlockSize => _blockSize;

    public int MipLevelCount => _levels.Length;

    public Color32[] GetLevel(int level) => _levels[level];

    public int GetLevelWidth(int level) => _levelWidths[level];

    public int GetLevelHeight(int level) => _levelHeights[level];

    public void AddStoredRun(int chunkIndex, CellType cellType, int runLength)
    {
        if ((uint)chunkIndex >= (long)WidthChunks * HeightChunks || runLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));
        }

        if (_currentChunkIndex != chunkIndex)
        {
            FlushCurrentChunk();
            _currentChunkIndex = chunkIndex;
        }

        int chunkArea = checked(_chunkSize * _chunkSize);
        if (runLength > chunkArea - _decodedCellCount)
        {
            throw new ArgumentOutOfRangeException(nameof(runLength), "Stored runs exceed the chunk area.");
        }

        Color32 color = GetColor(cellType);
        int chunkX = chunkIndex / HeightChunks;
        int chunkY = chunkIndex % HeightChunks;
        int validWidth = GetValidChunkWidth(chunkX);
        int validHeight = GetValidChunkHeight(chunkY);
        double red = DecodeSrgbChannel(color.r);
        double green = DecodeSrgbChannel(color.g);
        double blue = DecodeSrgbChannel(color.b);
        double alpha = color.a;

        // Split the run into per-block segments analytically: a run is a constant
        // colour, so each segment only contributes count * colour and we avoid
        // iterating every cell of a large stored world.
        int end = _decodedCellCount + runLength;
        int cell = _decodedCellCount;
        while (cell < end)
        {
            int localX = cell / _chunkSize;
            if (localX >= validWidth)
            {
                break;
            }

            int localY = cell % _chunkSize;
            if (localY >= validHeight)
            {
                cell = Math.Min(end, (localX + 1) * _chunkSize);
                continue;
            }

            int blockXi = localX / _blockSize;
            int blockYi = localY / _blockSize;
            int blockEnd = (localX * _chunkSize) + Math.Min(validHeight, (blockYi + 1) * _blockSize);
            int segmentEnd = Math.Min(end, blockEnd);
            int count = segmentEnd - cell;
            int blockIndex = blockYi + (blockXi * _blocksPerChunkAxis);
            _blockRed[blockIndex] += red * count;
            _blockGreen[blockIndex] += green * count;
            _blockBlue[blockIndex] += blue * count;
            _blockAlpha[blockIndex] += alpha * count;
            _blockCount[blockIndex] += count;
            cell = segmentEnd;
        }

        _decodedCellCount = end;
    }

    public void CompleteStoredScan() => FlushCurrentChunk();

    public void SetChunkCells(int chunkIndex, CellType[] cells)
    {
        if (cells == null)
        {
            throw new ArgumentNullException(nameof(cells));
        }

        ValidateChunkIndex(chunkIndex);
        int chunkArea = checked(_chunkSize * _chunkSize);
        int chunkX = chunkIndex / HeightChunks;
        int chunkY = chunkIndex % HeightChunks;
        int validWidth = GetValidChunkWidth(chunkX);
        int validHeight = GetValidChunkHeight(chunkY);
        if (cells.Length < chunkArea)
        {
            throw new ArgumentException(
                $"Chunk has {cells.Length} cells; expected at least {chunkArea}.",
                nameof(cells));
        }

        ResetBlockAccumulators();
        for (int localX = 0; localX < validWidth; localX++)
        {
            int columnOffset = localX * _chunkSize;
            for (int localY = 0; localY < validHeight; localY++)
            {
                Color32 color = GetColor(cells[columnOffset + localY]);
                AccumulateBlock(
                    localX,
                    localY,
                    DecodeSrgbChannel(color.r),
                    DecodeSrgbChannel(color.g),
                    DecodeSrgbChannel(color.b),
                    color.a);
            }
        }

        CommitChunkBlocks(chunkX, chunkY);
    }

    private void FlushCurrentChunk()
    {
        if (_currentChunkIndex < 0)
        {
            return;
        }

        if (_decodedCellCount != _chunkSize * _chunkSize)
        {
            throw new InvalidOperationException(
                $"Stored chunk {_currentChunkIndex} has {_decodedCellCount} decoded cells; " +
                $"expected {_chunkSize * _chunkSize}.");
        }

        CommitChunkBlocks(_currentChunkIndex / HeightChunks, _currentChunkIndex % HeightChunks);
        _currentChunkIndex = -1;
        _decodedCellCount = 0;
    }

    private void AccumulateBlock(int localX, int localY, double red, double green, double blue, byte alpha)
    {
        int blockIndex = (localY / _blockSize) + ((localX / _blockSize) * _blocksPerChunkAxis);
        _blockRed[blockIndex] += red;
        _blockGreen[blockIndex] += green;
        _blockBlue[blockIndex] += blue;
        _blockAlpha[blockIndex] += alpha;
        _blockCount[blockIndex]++;
    }

    private void CommitChunkBlocks(int chunkX, int chunkY)
    {
        int blockX0 = chunkX * _blocksPerChunkAxis;
        int blockY0 = chunkY * _blocksPerChunkAxis;
        int totalCells = 0;
        for (int blockXi = 0; blockXi < _blocksPerChunkAxis; blockXi++)
        {
            for (int blockYi = 0; blockYi < _blocksPerChunkAxis; blockYi++)
            {
                int blockIndex = blockYi + (blockXi * _blocksPerChunkAxis);
                int count = _blockCount[blockIndex];
                if (count <= 0)
                {
                    continue;
                }

                totalCells += count;
                SetBlockAverage(
                    blockX0 + blockXi,
                    blockY0 + blockYi,
                    CreateAverage(
                        _blockRed[blockIndex],
                        _blockGreen[blockIndex],
                        _blockBlue[blockIndex],
                        _blockAlpha[blockIndex],
                        count),
                    count);
            }
        }

        if (totalCells <= 0)
        {
            throw new InvalidOperationException(
                $"Chunk {chunkX},{chunkY} contributed no in-world cells to the mip pyramid.");
        }

        ResetBlockAccumulators();
    }

    private void ResetBlockAccumulators()
    {
        Array.Clear(_blockRed, 0, _blockRed.Length);
        Array.Clear(_blockGreen, 0, _blockGreen.Length);
        Array.Clear(_blockBlue, 0, _blockBlue.Length);
        Array.Clear(_blockAlpha, 0, _blockAlpha.Length);
        Array.Clear(_blockCount, 0, _blockCount.Length);
    }

    private void SetBlockAverage(int blockX, int blockY, Color32 average, int validCellCount)
    {
        ValidateBlockIndex(blockX, blockY);

        _levels[0][blockY * _levelWidths[0] + blockX] = average;
        _levelWeights[0][blockY * _levelWidths[0] + blockX] = validCellCount;
        int minChildX = blockX;
        int maxChildX = blockX;
        int minChildY = blockY;
        int maxChildY = blockY;
        for (int level = 1; level < _levels.Length; level++)
        {
            int childWidth = _levelWidths[level - 1];
            int childHeight = _levelHeights[level - 1];
            int parentWidth = _levelWidths[level];
            int parentHeight = _levelHeights[level];
            int minParentX = (int)((long)minChildX * parentWidth / childWidth);
            int maxParentX = (int)(((long)(maxChildX + 1) * parentWidth + childWidth - 1) / childWidth) - 1;
            int minParentY = (int)((long)minChildY * parentHeight / childHeight);
            int maxParentY = (int)(((long)(maxChildY + 1) * parentHeight + childHeight - 1) / childHeight) - 1;

            for (int parentY = minParentY; parentY <= maxParentY; parentY++)
            {
                for (int parentX = minParentX; parentX <= maxParentX; parentX++)
                {
                    Color32 parentAverage = AverageChildren(level - 1, parentX, parentY, out double parentWeight);
                    _levels[level][parentY * parentWidth + parentX] = parentAverage;
                    _levelWeights[level][parentY * parentWidth + parentX] = (float)parentWeight;
                }
            }

            minChildX = minParentX;
            maxChildX = maxParentX;
            minChildY = minParentY;
            maxChildY = maxParentY;
        }
    }

    private Color32 GetColor(CellType cellType) =>
        cellType == CellType.Unloaded ? _unloadedColor : _cellColorTable[(byte)cellType];

    private Color32 AverageChildren(int childLevel, int parentX, int parentY, out double totalWeight)
    {
        Color32[] children = _levels[childLevel];
        int childWidth = _levelWidths[childLevel];
        int childHeight = _levelHeights[childLevel];
        int parentWidth = _levelWidths[childLevel + 1];
        int parentHeight = _levelHeights[childLevel + 1];
        int firstChildX = parentX * childWidth / parentWidth;
        int endChildX = ((parentX + 1) * childWidth + parentWidth - 1) / parentWidth;
        int firstChildY = parentY * childHeight / parentHeight;
        int endChildY = ((parentY + 1) * childHeight + parentHeight - 1) / parentHeight;
        double red = 0;
        double green = 0;
        double blue = 0;
        double alpha = 0;
        totalWeight = 0;

        for (int childY = firstChildY; childY < endChildY; childY++)
        {
            int overlapY = Math.Min((childY + 1) * parentHeight, (parentY + 1) * childHeight) -
                           Math.Max(childY * parentHeight, parentY * childHeight);

            for (int childX = firstChildX; childX < endChildX; childX++)
            {
                int overlapX = Math.Min((childX + 1) * parentWidth, (parentX + 1) * childWidth) -
                               Math.Max(childX * parentWidth, parentX * childWidth);
                int childIndex = childY * childWidth + childX;
                double weight = _levelWeights[childLevel][childIndex] * overlapX * overlapY /
                                ((double)parentWidth * parentHeight);
                Color32 color = children[childIndex];
                red += DecodeSrgbChannel(color.r) * weight;
                green += DecodeSrgbChannel(color.g) * weight;
                blue += DecodeSrgbChannel(color.b) * weight;
                alpha += color.a * weight;
                totalWeight += weight;
            }
        }

        return CreateAverage(red, green, blue, alpha, totalWeight);
    }

    private double CalculateParentWeight(int childLevel, int parentX, int parentY)
    {
        int childWidth = _levelWidths[childLevel];
        int childHeight = _levelHeights[childLevel];
        int parentWidth = _levelWidths[childLevel + 1];
        int parentHeight = _levelHeights[childLevel + 1];
        int firstChildX = parentX * childWidth / parentWidth;
        int endChildX = ((parentX + 1) * childWidth + parentWidth - 1) / parentWidth;
        int firstChildY = parentY * childHeight / parentHeight;
        int endChildY = ((parentY + 1) * childHeight + parentHeight - 1) / parentHeight;
        double totalWeight = 0;

        for (int childY = firstChildY; childY < endChildY; childY++)
        {
            int overlapY = Math.Min((childY + 1) * parentHeight, (parentY + 1) * childHeight) -
                           Math.Max(childY * parentHeight, parentY * childHeight);

            for (int childX = firstChildX; childX < endChildX; childX++)
            {
                int overlapX = Math.Min((childX + 1) * parentWidth, (parentX + 1) * childWidth) -
                               Math.Max(childX * parentWidth, parentX * childWidth);
                int childIndex = childY * childWidth + childX;
                totalWeight += _levelWeights[childLevel][childIndex] * overlapX * overlapY /
                               ((double)parentWidth * parentHeight);
            }
        }

        return totalWeight;
    }

    private static Color32 CreateAverage(double red, double green, double blue, double alpha, double count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        return new Color32(
            EncodeSrgbChannel(red / count),
            EncodeSrgbChannel(green / count),
            EncodeSrgbChannel(blue / count),
            (byte)(alpha / count));
    }

    private static double DecodeSrgbChannel(byte channel) =>
        Mathf.GammaToLinearSpace(channel / 255f);

    private static byte EncodeSrgbChannel(double linearChannel) =>
        (byte)Mathf.Clamp(
            Mathf.RoundToInt(Mathf.LinearToGammaSpace((float)linearChannel) * 255f),
            0,
            255);

    private int GetValidChunkWidth(int chunkX) =>
        Math.Min(_chunkSize, _worldWidth - (chunkX * _chunkSize));

    private int GetValidChunkHeight(int chunkY) =>
        Math.Min(_chunkSize, _worldHeight - (chunkY * _chunkSize));

    private int GetValidBlockWidth(int blockX) =>
        Math.Min(_blockSize, _worldWidth - (blockX * _blockSize));

    private int GetValidBlockHeight(int blockY) =>
        Math.Min(_blockSize, _worldHeight - (blockY * _blockSize));

    private void ValidateChunkIndex(int chunkIndex)
    {
        if ((uint)chunkIndex >= (long)WidthChunks * HeightChunks)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkIndex));
        }
    }

    private void ValidateBlockIndex(int blockX, int blockY)
    {
        if ((uint)blockX >= (uint)_levelWidths[0] || (uint)blockY >= (uint)_levelHeights[0])
        {
            throw new ArgumentOutOfRangeException(nameof(blockX));
        }
    }
}
