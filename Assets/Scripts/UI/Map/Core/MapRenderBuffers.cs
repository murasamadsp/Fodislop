#nullable enable

using System;
using UnityEngine;

namespace Kern.UI;

/// <summary>
/// Owns the GPU buffers and the dummy fallback texture used by the viewport
/// renderer, so resource growth and release live in one place.
/// </summary>
internal sealed class MapRenderBuffers : IDisposable
{
    private readonly Vector4[] _palette = new Vector4[256];

    private ComputeBuffer? _paletteBuffer;
    private ComputeBuffer? _chunkDataBuffer;
    private ComputeBuffer? _chunkLookupBuffer;
    private uint[]? _chunkData;
    private int[]? _chunkLookup;
    private Texture2D? _dummyMipTexture;

    public Vector4[] Palette => _palette;

    public uint[] ChunkData => _chunkData ??
        throw new InvalidOperationException("[MapRenderBuffers] Chunk buffers were not ensured.");

    public int[] ChunkLookup => _chunkLookup ??
        throw new InvalidOperationException("[MapRenderBuffers] Chunk buffers were not ensured.");

    public ComputeBuffer PaletteBuffer
    {
        get
        {
            EnsurePaletteBuffer();
            return _paletteBuffer!;
        }
    }

    public ComputeBuffer ChunkDataBuffer => _chunkDataBuffer ??
        throw new InvalidOperationException("[MapRenderBuffers] Chunk buffers were not ensured.");

    public ComputeBuffer ChunkLookupBuffer => _chunkLookupBuffer ??
        throw new InvalidOperationException("[MapRenderBuffers] Chunk buffers were not ensured.");

    public void UploadPalette()
    {
        EnsurePaletteBuffer();
        _paletteBuffer!.SetData(_palette);
    }

    /// <summary>
    /// Ensures a lookup table of at least <paramref name="lookupSize"/> entries
    /// and room for <paramref name="chunkSlots"/> packed chunks.
    /// </summary>
    public void EnsureChunkBuffers(int lookupSize, int chunkSlots)
    {
        lookupSize = Math.Max(1, lookupSize);
        chunkSlots = Math.Max(1, chunkSlots);

        if (_chunkLookupBuffer == null || _chunkLookupBuffer.count < lookupSize)
        {
            _chunkLookupBuffer?.Release();
            _chunkLookupBuffer = new ComputeBuffer(lookupSize, sizeof(int));
            _chunkLookup = new int[lookupSize];
        }

        int requiredChunkUints = checked(chunkSlots * MapGpuChunkPacker.PackedUIntCount);
        if (_chunkDataBuffer == null || _chunkDataBuffer.count < requiredChunkUints)
        {
            _chunkDataBuffer?.Release();
            _chunkDataBuffer = new ComputeBuffer(requiredChunkUints, sizeof(uint));
            _chunkData = new uint[requiredChunkUints];
        }
    }

    public void UploadLookup(int entryCount) =>
        _chunkLookupBuffer!.SetData(_chunkLookup!, 0, 0, entryCount);

    public void UploadChunkData(int uintCount) =>
        _chunkDataBuffer!.SetData(_chunkData!, 0, 0, uintCount);

    public Texture2D GetDummyMipTexture()
    {
        if (_dummyMipTexture == null)
        {
            _dummyMipTexture = RuntimeTextureFactory.CreateRGBA32NoMip(
                1,
                1,
                "WorldMapDummyMipTexture",
                RuntimeTextureColorSpace.Srgb,
                FilterMode.Point,
                TextureWrapMode.Clamp);
            _dummyMipTexture.SetPixel(0, 0, Color.black);
            _dummyMipTexture.Apply(false, false);
        }

        return _dummyMipTexture;
    }

    public void Dispose()
    {
        _paletteBuffer?.Release();
        _paletteBuffer = null;

        _chunkDataBuffer?.Release();
        _chunkDataBuffer = null;

        _chunkLookupBuffer?.Release();
        _chunkLookupBuffer = null;

        _chunkData = null;
        _chunkLookup = null;

        if (_dummyMipTexture != null)
        {
            UnityEngine.Object.Destroy(_dummyMipTexture);
            _dummyMipTexture = null;
        }
    }

    private void EnsurePaletteBuffer()
    {
        if (_paletteBuffer == null)
        {
            _paletteBuffer = new ComputeBuffer(256, sizeof(float) * 4);
            _paletteBuffer.SetData(_palette);
        }
    }
}
