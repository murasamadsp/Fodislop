#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using unity.libwebp;
using unity.libwebp.Interop;
using UnityEngine;

namespace Fodinae.World
{
    public static class AnimationContainerDecoder
    {
        public enum ContainerType
        {
            None,
            PNG,
            GIF,
            WebP,
        }

        public static ContainerType DetectType(byte[] data)
        {
            if (data == null || data.Length < 12)
            {
                return ContainerType.None;
            }

            if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
            {
                return ContainerType.PNG;
            }

            if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x38)
            {
                return ContainerType.GIF;
            }

            if (data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
                data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
            {
                return ContainerType.WebP;
            }

            return ContainerType.None;
        }

        public static Sprite[] Decode(Texture2D atlas, int width, int height, int frameCount)
        {
            if (atlas == null)
            {
                throw new ArgumentNullException(nameof(atlas));
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(width),
                    "Sprite frame dimensions must be positive.");
            }

            if (frameCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(frameCount),
                    "Sprite frame count must be positive.");
            }

            if (atlas.width < width || atlas.height < height)
            {
                throw new InvalidDataException(
                    $"Sprite atlas {atlas.width}x{atlas.height} is smaller than frame {width}x{height}.");
            }

            Sprite[] frames = new Sprite[frameCount];
            int framesPerRow = atlas.width / width;
            if (framesPerRow <= 0 ||
                (int)Math.Ceiling(frameCount / (double)framesPerRow) * height > atlas.height)
            {
                throw new InvalidDataException(
                    $"Sprite atlas {atlas.width}x{atlas.height} cannot contain " +
                    $"{frameCount} frames of {width}x{height}.");
            }

            for (int i = 0; i < frameCount; i++)
            {
                int x = (i % framesPerRow) * width;
                int y = (i / framesPerRow) * height;

                // DecodeGif/DecodeWebP place frame zero at the bottom of the
                // Unity texture and append later frames upwards. Re-inverting Y
                // here returned the animation in reverse order.
                frames[i] = Sprite.Create(
                    atlas,
                    new Rect(x, y, width, height),
                    new Vector2(0.5f, 0.5f),
                    RenderingConstants.PIXELS_PER_UNIT);
            }

            return frames;
        }

        public static DecodedAnimation DecodeGif(byte[] data)
        {
            try
            {
                return new GifInternalDecoder(data).Decode();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AnimationContainerDecoder] GIF decode failed; asset will be skipped: {e.Message}");
                throw new InvalidOperationException($"GIF decode failed: {e.Message}", e);
            }
        }

        public static unsafe DecodedAnimation DecodeWebP(byte[] data)
        {
            var frameTextures = new List<Texture2D>();
            Texture2D? atlas = null;
            try
            {
                if (data == null || data.Length < 12 || DetectType(data) != ContainerType.WebP)
                {
                    throw new InvalidDataException("WebP data is missing a valid RIFF/WEBP header.");
                }

                long declaredFileSize = BitConverter.ToUInt32(data, 4) + 8L;
                if (declaredFileSize < 12L || declaredFileSize > data.Length)
                {
                    throw new InvalidDataException(
                        $"WebP RIFF payload ends at byte {declaredFileSize}, outside the " +
                        $"{data.Length}-byte input.");
                }

                var delays = new List<int>();
                int width;
                int height;
                int expectedFrameCount;
                fixed (byte* dataPointer = data)
                {
                    var webpData = new WebPData
                    {
                        bytes = dataPointer,
                        size = (UIntPtr)data.Length,
                    };
                    WebPAnimDecoderOptions options = default;
                    if (NativeLibwebpdemux.WebPAnimDecoderOptionsInit(&options) == 0)
                    {
                        throw new InvalidDataException(
                            "libwebp could not initialize animation decoder options.");
                    }

                    options.color_mode = WEBP_CSP_MODE.MODE_RGBA;
                    options.use_threads = 1;
                    WebPAnimDecoder* decoder =
                        NativeLibwebpdemux.WebPAnimDecoderNew(&webpData, &options);
                    if (decoder == null)
                    {
                        throw new InvalidDataException(
                            "libwebp could not create an animation decoder.");
                    }

                    try
                    {
                        WebPAnimInfo info = default;
                        if (NativeLibwebpdemux.WebPAnimDecoderGetInfo(
                                decoder,
                                &info) == 0)
                        {
                            throw new InvalidDataException(
                                "libwebp could not read WebP animation metadata.");
                        }

                        width = checked((int)info.canvas_width);
                        height = checked((int)info.canvas_height);
                        expectedFrameCount = checked((int)info.frame_count);
                        if (width <= 0 || height <= 0 || expectedFrameCount <= 0)
                        {
                            throw new InvalidDataException(
                                $"WebP reports invalid canvas/frame metadata: " +
                                $"{width}x{height}, {expectedFrameCount} frame(s).");
                        }

                        if (width > SystemInfo.maxTextureSize ||
                            height > SystemInfo.maxTextureSize)
                        {
                            throw new InvalidDataException(
                                $"WebP canvas {width}x{height} exceeds the GPU " +
                                $"texture limit {SystemInfo.maxTextureSize}.");
                        }

                        int stride = checked(width * 4);
                        int byteCount = checked(stride * height);
                        int previousTimestamp = 0;
                        while (NativeLibwebpdemux.WebPAnimDecoderHasMoreFrames(
                                   decoder) != 0)
                        {
                            byte* frameBuffer = null;
                            int timestamp = 0;
                            if (NativeLibwebpdemux.WebPAnimDecoderGetNext(
                                    decoder,
                                    &frameBuffer,
                                    &timestamp) == 0 ||
                                frameBuffer == null)
                            {
                                throw new InvalidDataException(
                                    $"libwebp failed while decoding frame " +
                                    $"{frameTextures.Count}.");
                            }

                            int duration = timestamp - previousTimestamp;
                            if (expectedFrameCount > 1 && duration <= 0)
                            {
                                throw new InvalidDataException(
                                    $"WebP animation frame {frameTextures.Count} " +
                                    $"has non-positive duration {duration} ms.");
                            }

                            previousTimestamp = timestamp;
                            byte[] rawPixels = new byte[byteCount];
                            for (int sourceY = 0; sourceY < height; sourceY++)
                            {
                                int destinationY = height - 1 - sourceY;
                                Marshal.Copy(
                                    (IntPtr)(frameBuffer + (sourceY * stride)),
                                    rawPixels,
                                    destinationY * stride,
                                    stride);
                            }

                            Texture2D? frameTexture = RuntimeTextureFactory.CreateRgba32NoMip(
                                width,
                                height,
                                $"DecodedWebPFrame_{frameTextures.Count}",
                                RuntimeTextureColorSpace.Srgb,
                                FilterMode.Point,
                                TextureWrapMode.Clamp);
                            try
                            {
                                frameTexture.LoadRawTextureData(rawPixels);
                                bool makeNoLongerReadable =
                                    RuntimeTextureFactory.SupportsTexture2DGpuCopy;
                                frameTexture.Apply(
                                    updateMipmaps: false,
                                    makeNoLongerReadable: makeNoLongerReadable);
                                frameTextures.Add(frameTexture);
                                frameTexture = null;
                            }
                            finally
                            {
                                if (frameTexture != null)
                                {
                                    UnityEngine.Object.Destroy(frameTexture);
                                }
                            }

                            delays.Add(duration);
                        }

                        if (frameTextures.Count != expectedFrameCount)
                        {
                            throw new InvalidDataException(
                                $"libwebp decoded {frameTextures.Count} frame(s), but " +
                                $"the container declares {expectedFrameCount}.");
                        }
                    }
                    finally
                    {
                        NativeLibwebpdemux.WebPAnimDecoderDelete(decoder);
                    }
                }

                int frameCount = frameTextures.Count;
                if (frameCount == 1)
                {
                    Texture2D texture = frameTextures[0];
                    frameTextures.Clear();
                    return new DecodedAnimation
                    {
                        Atlas = texture,
                        FrameCount = 1,
                        FrameHeight = height,
                        FPS = 0f,
                    };
                }

                int atlasHeight = checked(height * frameCount);
                if (atlasHeight > SystemInfo.maxTextureSize)
                {
                    throw new InvalidDataException(
                        $"WebP animation atlas {width}x{atlasHeight} exceeds the GPU " +
                        $"texture limit {SystemInfo.maxTextureSize}.");
                }

                atlas = RuntimeTextureFactory.CreateRgba32NoMip(
                    width,
                    atlasHeight,
                    "DecodedWebPAtlas",
                    RuntimeTextureColorSpace.Srgb,
                    FilterMode.Point,
                    TextureWrapMode.Clamp);
                float totalDelay = 0;
                for (int i = 0; i < frameCount; i++)
                {
                    totalDelay += delays[i];
                }

                CopyFramesToAtlas(frameTextures, atlas, width, height);
                float avgDelay = totalDelay / frameCount;
                float fps = GetAnimationFps(
                    avgDelay,
                    frameCount,
                    "WebP");
                var result = new DecodedAnimation
                {
                    Atlas = atlas,
                    FrameCount = frameCount,
                    FrameHeight = height,
                    FPS = fps,
                };
                atlas = null;
                return result;
            }
            catch (Exception e)
            {
                DestroyTextures(frameTextures);
                if (atlas != null)
                {
                    UnityEngine.Object.Destroy(atlas);
                }

                Debug.LogWarning($"[AnimationContainerDecoder] WebP decode failed; asset will be skipped: {e.Message}");
                throw new InvalidOperationException($"WebP decode failed: {e.Message}", e);
            }
        }

        private static void CopyFramesToAtlas(
            List<Texture2D> frameTextures,
            Texture2D atlas,
            int width,
            int height)
        {
            bool useGpuCopy = RuntimeTextureFactory.SupportsTexture2DGpuCopy;
            for (int i = 0; i < frameTextures.Count; i++)
            {
                Texture2D frame = frameTextures[i];
                if (frame.width != width || frame.height != height)
                {
                    throw new InvalidDataException(
                        $"Animation frame {i} is {frame.width}x{frame.height}; " +
                        $"expected {width}x{height}.");
                }

                if (useGpuCopy)
                {
                    if (frame.graphicsFormat != atlas.graphicsFormat)
                    {
                        throw new InvalidDataException(
                            $"Animation frame {i} uses GPU format " +
                            $"{frame.graphicsFormat}, but atlas uses " +
                            $"{atlas.graphicsFormat}.");
                    }

                    Graphics.CopyTexture(
                        frame,
                        0,
                        0,
                        0,
                        0,
                        width,
                        height,
                        atlas,
                        0,
                        0,
                        0,
                        i * height);
                }
                else
                {
                    atlas.SetPixels32(
                        x: 0,
                        y: i * height,
                        blockWidth: width,
                        blockHeight: height,
                        colors: frame.GetPixels32());
                }
            }

            if (!useGpuCopy)
            {
                atlas.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            }

            DestroyTextures(frameTextures);
        }

        private static void DestroyTextures(List<Texture2D> textures)
        {
            for (int i = 0; i < textures.Count; i++)
            {
                if (textures[i] != null)
                {
                    UnityEngine.Object.Destroy(textures[i]);
                }
            }

            textures.Clear();
        }

        private static float GetAnimationFps(
            float averageDelay,
            int frameCount,
            string containerName)
        {
            if (frameCount <= 1)
            {
                return 0f;
            }

            if (averageDelay <= 0f || float.IsNaN(averageDelay) || float.IsInfinity(averageDelay))
            {
                throw new InvalidDataException(
                    $"{containerName} animation has {frameCount} frames but no positive frame delay.");
            }

            return containerName == "GIF"
                ? 100f / averageDelay
                : 1000f / averageDelay;
        }

        public struct DecodedAnimation
        {
            public Texture2D Atlas { get; set; }

            public int FrameCount { get; set; }

            public int FrameHeight { get; set; }

            public float FPS { get; set; }
        }

        private class GifInternalDecoder
        {
            private static readonly int[] InterlaceRowStarts = [0, 4, 2, 1];
            private static readonly int[] InterlaceRowSteps = [8, 8, 4, 2];
            private byte[] _data;
            private int _pos;
            private int _sw;
            private int _sh;
            private Color32[] _gt = Array.Empty<Color32>();
            private Color32[] _cv = Array.Empty<Color32>();
            private Color32[] _pv = Array.Empty<Color32>();

            public GifInternalDecoder(byte[] d)
            {
                this._data = d;
            }

            public DecodedAnimation Decode()
            {
                if (this._data.Length < 13 ||
                    this._data[0] != 'G' ||
                    this._data[1] != 'I' ||
                    this._data[2] != 'F')
                {
                    throw new InvalidDataException(
                        "GIF data is missing a complete header and logical screen descriptor.");
                }

                this._pos = 6;
                this._sw = this.ReadUInt16();
                this._sh = this.ReadUInt16();
                if (this._sw <= 0 || this._sh <= 0)
                {
                    throw new InvalidDataException(
                        $"GIF logical screen has invalid dimensions {this._sw}x{this._sh}.");
                }

                int pixelCount = checked(this._sw * this._sh);
                if (this._sw > SystemInfo.maxTextureSize ||
                    this._sh > SystemInfo.maxTextureSize)
                {
                    throw new InvalidDataException(
                        $"GIF logical screen {this._sw}x{this._sh} exceeds the GPU " +
                        $"texture limit {SystemInfo.maxTextureSize}.");
                }

                byte packedFields = this.ReadByte();
                int backgroundColorIndex = this.ReadByte();
                this.ReadByte(); // Pixel aspect ratio.

                if ((packedFields & 0x80) != 0)
                {
                    this._gt = this.ReadColorTable(
                        1 << ((packedFields & 0x07) + 1));
                }

                Color32 backgroundColor =
                    backgroundColorIndex >= 0 && backgroundColorIndex < this._gt.Length
                        ? this._gt[backgroundColorIndex]
                        : new Color32(0, 0, 0, 0);
                this._cv = new Color32[pixelCount];
                this._pv = new Color32[pixelCount];
                var frameTextures = new List<Texture2D>();
                var frameDelays = new List<int>();
                Texture2D? atlas = null;
                bool foundTrailer = false;
                int delay = 0;
                int transparentIndex = -1;
                int disposalMethod = 0;

                try
                {
                    while (this._pos < this._data.Length)
                    {
                        byte blockType = this.ReadByte();
                        if (blockType == 0x21)
                        {
                            byte extensionType = this.ReadByte();
                            if (extensionType == 0xF9)
                            {
                                int blockSize = this.ReadByte();
                                if (blockSize != 4)
                                {
                                    throw new InvalidDataException(
                                        $"GIF graphic control extension has size {blockSize}; expected 4.");
                                }

                                byte graphicControl = this.ReadByte();
                                disposalMethod = (graphicControl & 0x1C) >> 2;
                                delay = this.ReadUInt16();
                                transparentIndex = this.ReadByte();
                                if ((graphicControl & 0x01) == 0)
                                {
                                    transparentIndex = -1;
                                }

                                if (this.ReadByte() != 0)
                                {
                                    throw new InvalidDataException(
                                        "GIF graphic control extension has no zero terminator.");
                                }
                            }
                            else
                            {
                                this.SkipDataSubBlocks();
                            }
                        }
                        else if (blockType == 0x2C)
                        {
                            int left = this.ReadUInt16();
                            int top = this.ReadUInt16();
                            int width = this.ReadUInt16();
                            int height = this.ReadUInt16();
                            if (width <= 0 || height <= 0 ||
                                left > this._sw - width || top > this._sh - height)
                            {
                                throw new InvalidDataException(
                                    $"GIF frame rectangle {width}x{height} at {left},{top} " +
                                    $"does not fit the {this._sw}x{this._sh} canvas.");
                            }

                            byte imageFields = this.ReadByte();
                            Color32[] colorTable = (imageFields & 0x80) != 0
                                ? this.ReadColorTable(1 << ((imageFields & 0x07) + 1))
                                : this._gt;
                            if (colorTable.Length == 0)
                            {
                                throw new InvalidDataException(
                                    $"GIF frame {frameTextures.Count} has no color table.");
                            }

                            int minimumCodeSize = this.ReadByte();
                            byte[] colorIndices = Lzw(
                                this.ReadDataSubBlocks(),
                                minimumCodeSize,
                                checked(width * height));

                            if (disposalMethod == 3)
                            {
                                Array.Copy(this._cv, this._pv, this._cv.Length);
                            }

                            bool interlaced = (imageFields & 0x40) != 0;
                            this.CompositeFrame(
                                colorIndices,
                                colorTable,
                                left,
                                top,
                                width,
                                height,
                                transparentIndex,
                                interlaced);

                            Texture2D frameTexture = RuntimeTextureFactory.CreateRgba32NoMip(
                                this._sw,
                                this._sh,
                                "DecodedGifFrame",
                                RuntimeTextureColorSpace.Srgb,
                                FilterMode.Point,
                                TextureWrapMode.Clamp);
                            var flippedPixels = new Color32[pixelCount];
                            for (int y = 0; y < this._sh; y++)
                            {
                                Array.Copy(
                                    this._cv,
                                    y * this._sw,
                                    flippedPixels,
                                    (this._sh - 1 - y) * this._sw,
                                    this._sw);
                            }

                            frameTexture.SetPixels32(flippedPixels);
                            bool makeNoLongerReadable =
                                RuntimeTextureFactory.SupportsTexture2DGpuCopy;
                            frameTexture.Apply(
                                updateMipmaps: false,
                                makeNoLongerReadable: makeNoLongerReadable);
                            frameTextures.Add(frameTexture);
                            frameDelays.Add(delay);

                            if (disposalMethod == 2)
                            {
                                Color32 restoreColor = transparentIndex >= 0
                                    ? new Color32(0, 0, 0, 0)
                                    : backgroundColor;
                                this.ClearFrameRectangle(
                                    left,
                                    top,
                                    width,
                                    height,
                                    restoreColor);
                            }
                            else if (disposalMethod == 3)
                            {
                                Array.Copy(this._pv, this._cv, this._cv.Length);
                            }

                            delay = 0;
                            transparentIndex = -1;
                            disposalMethod = 0;
                        }
                        else if (blockType == 0x3B)
                        {
                            foundTrailer = true;
                            break;
                        }
                        else
                        {
                            throw new InvalidDataException(
                                $"GIF contains unknown block type 0x{blockType:X2} " +
                                $"at byte {this._pos - 1}.");
                        }
                    }

                    if (!foundTrailer)
                    {
                        throw new InvalidDataException(
                            "GIF stream ended before its trailer byte.");
                    }

                    if (frameTextures.Count == 0)
                    {
                        throw new InvalidDataException(
                            "GIF container was valid but contained no usable image frames.");
                    }

                    int frameCount = frameTextures.Count;
                    if (frameCount > 1)
                    {
                        for (int i = 0; i < frameDelays.Count; i++)
                        {
                            if (frameDelays[i] <= 0)
                            {
                                throw new InvalidDataException(
                                    $"GIF animation frame {i} has no positive delay.");
                            }
                        }
                    }

                    int atlasHeight = checked(this._sh * frameCount);
                    if (atlasHeight > SystemInfo.maxTextureSize)
                    {
                        throw new InvalidDataException(
                            $"GIF animation atlas {this._sw}x{atlasHeight} exceeds the GPU " +
                            $"texture limit {SystemInfo.maxTextureSize}.");
                    }

                    atlas = RuntimeTextureFactory.CreateRgba32NoMip(
                        this._sw,
                        atlasHeight,
                        "DecodedGifAtlas",
                        RuntimeTextureColorSpace.Srgb,
                        FilterMode.Point,
                        TextureWrapMode.Clamp);
                    float totalDelay = 0;
                    for (int i = 0; i < frameCount; i++)
                    {
                        totalDelay += frameDelays[i];
                    }

                    CopyFramesToAtlas(
                        frameTextures,
                        atlas,
                        this._sw,
                        this._sh);
                    float fps = GetAnimationFps(
                        totalDelay / frameCount,
                        frameCount,
                        "GIF");

                    var result = new DecodedAnimation
                    {
                        Atlas = atlas,
                        FrameCount = frameCount,
                        FrameHeight = this._sh,
                        FPS = fps,
                    };
                    atlas = null;
                    return result;
                }
                catch
                {
                    DestroyTextures(frameTextures);
                    if (atlas != null)
                    {
                        UnityEngine.Object.Destroy(atlas);
                    }

                    throw;
                }
            }

            private static byte[] Lzw(byte[] d, int m, int pc)
            {
                if (m < 2 || m > 8)
                {
                    throw new InvalidDataException(
                        $"GIF LZW minimum code size {m} is outside the supported range 2..8.");
                }

                if (pc <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(pc),
                        pc,
                        "GIF frame pixel count must be positive.");
                }

                int cc = 1 << m;
                int eoi = cc + 1;
                int nc = cc + 2;
                int cs = m + 1;
                int cm = (1 << cs) - 1;
                int[] pref = new int[4096];
                byte[] suff = new byte[4096];
                byte[] ps = new byte[4097];

                for (int i = 0; i < cc; i++)
                {
                    suff[i] = (byte)i;
                }

                byte[] o = new byte[pc];
                int op = 0;
                int bb = 0;
                int bc = 0;
                int dp = 0;
                int t = 0;
                int oc = -1;

                while (op < pc)
                {
                    while (bc < cs && dp < d.Length)
                    {
                        bb |= d[dp++] << bc;
                        bc += 8;
                    }

                    if (bc < cs)
                    {
                        break;
                    }

                    int c = bb & cm;
                    bb >>= cs;
                    bc -= cs;

                    if (c == cc)
                    {
                        cs = m + 1;
                        cm = (1 << cs) - 1;
                        nc = cc + 2;
                        oc = -1;
                        continue;
                    }

                    if (c == eoi)
                    {
                        break;
                    }

                    if (oc == -1)
                    {
                        if (c >= cc)
                        {
                            throw new InvalidDataException(
                                $"GIF LZW stream starts with invalid code {c}.");
                        }

                        o[op++] = suff[c];
                        oc = c;
                        continue;
                    }

                    int cur = c;
                    if (c > nc)
                    {
                        throw new InvalidDataException(
                            $"GIF LZW code {c} exceeds the next dictionary index {nc}.");
                    }

                    if (c == nc)
                    {
                        if (t >= ps.Length)
                        {
                            throw new InvalidDataException(
                                "GIF LZW expansion stack overflowed.");
                        }

                        ps[t++] = (byte)LzwFirst(oc, cc, pref, suff);
                        cur = oc;
                    }

                    while (cur >= cc)
                    {
                        if (cur >= nc || t >= ps.Length)
                        {
                            throw new InvalidDataException(
                                "GIF LZW dictionary chain is corrupt.");
                        }

                        ps[t++] = suff[cur];
                        cur = pref[cur];
                    }

                    if (cur < 0 || cur >= cc || t >= ps.Length)
                    {
                        throw new InvalidDataException(
                            "GIF LZW dictionary resolved to an invalid root code.");
                    }

                    ps[t++] = suff[cur];
                    byte f = ps[t - 1];
                    while (t > 0)
                    {
                        if (op >= o.Length)
                        {
                            throw new InvalidDataException(
                                "GIF LZW stream expands past the declared frame size.");
                        }

                        o[op++] = ps[--t];
                    }

                    if (nc < 4096)
                    {
                        pref[nc] = oc;
                        suff[nc] = f;
                        nc++;
                        if (nc == (1 << cs) && cs < 12)
                        {
                            cs++;
                            cm = (1 << cs) - 1;
                        }
                    }

                    oc = c;
                }

                if (op != pc)
                {
                    throw new InvalidDataException(
                        $"GIF LZW stream produced {op} pixels; expected {pc}.");
                }

                return o;
            }

            private static int LzwFirst(int c, int cc, int[] pref, byte[] suff)
            {
                int steps = 0;
                while (c >= cc)
                {
                    if (c < 0 || c >= pref.Length || steps++ >= pref.Length)
                    {
                        throw new InvalidDataException(
                            "GIF LZW dictionary contains a cyclic or invalid prefix chain.");
                    }

                    c = pref[c];
                }

                if (c < 0 || c >= suff.Length)
                {
                    throw new InvalidDataException(
                        "GIF LZW dictionary resolved outside the suffix table.");
                }

                return suff[c];
            }

            private void CompositeFrame(
                byte[] colorIndices,
                Color32[] colorTable,
                int left,
                int top,
                int width,
                int height,
                int transparentIndex,
                bool interlaced)
            {
                int sourceRow = 0;
                if (interlaced)
                {
                    for (int pass = 0; pass < InterlaceRowStarts.Length; pass++)
                    {
                        for (int targetRow = InterlaceRowStarts[pass];
                             targetRow < height;
                             targetRow += InterlaceRowSteps[pass])
                        {
                            this.CompositeFrameRow(
                                colorIndices,
                                colorTable,
                                left,
                                top,
                                width,
                                sourceRow++,
                                targetRow,
                                transparentIndex);
                        }
                    }
                }
                else
                {
                    for (int row = 0; row < height; row++)
                    {
                        this.CompositeFrameRow(
                            colorIndices,
                            colorTable,
                            left,
                            top,
                            width,
                            row,
                            row,
                            transparentIndex);
                        sourceRow++;
                    }
                }

                if (sourceRow != height)
                {
                    throw new InvalidDataException(
                        $"GIF interlace mapping consumed {sourceRow} rows; expected {height}.");
                }
            }

            private void CompositeFrameRow(
                byte[] colorIndices,
                Color32[] colorTable,
                int left,
                int top,
                int width,
                int sourceRow,
                int targetRow,
                int transparentIndex)
            {
                int sourceOffset = sourceRow * width;
                int destinationOffset = ((top + targetRow) * this._sw) + left;
                for (int x = 0; x < width; x++)
                {
                    int colorIndex = colorIndices[sourceOffset + x];
                    if (colorIndex == transparentIndex)
                    {
                        continue;
                    }

                    if (colorIndex >= colorTable.Length)
                    {
                        throw new InvalidDataException(
                            $"GIF frame references color {colorIndex}, but its table has " +
                            $"only {colorTable.Length} entries.");
                    }

                    this._cv[destinationOffset + x] = colorTable[colorIndex];
                }
            }

            private void ClearFrameRectangle(
                int left,
                int top,
                int width,
                int height,
                Color32 color)
            {
                for (int y = 0; y < height; y++)
                {
                    int rowOffset = ((top + y) * this._sw) + left;
                    for (int x = 0; x < width; x++)
                    {
                        this._cv[rowOffset + x] = color;
                    }
                }
            }

            private Color32[] ReadColorTable(int size)
            {
                if (size < 2 || size > 256)
                {
                    throw new InvalidDataException(
                        $"GIF color table has invalid size {size}.");
                }

                this.EnsureAvailable(checked(size * 3));
                var table = new Color32[size];
                for (int i = 0; i < size; i++)
                {
                    table[i] = new Color32(
                        this.ReadByte(),
                        this.ReadByte(),
                        this.ReadByte(),
                        255);
                }

                return table;
            }

            private void SkipDataSubBlocks()
            {
                while (true)
                {
                    int size = this.ReadByte();
                    if (size == 0)
                    {
                        return;
                    }

                    this.EnsureAvailable(size);
                    this._pos += size;
                }
            }

            private byte[] ReadDataSubBlocks()
            {
                using var stream = new MemoryStream();
                while (true)
                {
                    int size = this.ReadByte();
                    if (size == 0)
                    {
                        return stream.ToArray();
                    }

                    this.EnsureAvailable(size);
                    stream.Write(this._data, this._pos, size);
                    this._pos += size;
                }
            }

            private int ReadUInt16()
            {
                int low = this.ReadByte();
                int high = this.ReadByte();
                return low | (high << 8);
            }

            private byte ReadByte()
            {
                this.EnsureAvailable(1);
                return this._data[this._pos++];
            }

            private void EnsureAvailable(int byteCount)
            {
                if (byteCount < 0 || this._pos < 0 || this._pos > this._data.Length - byteCount)
                {
                    throw new InvalidDataException(
                        $"GIF stream is truncated at byte {this._pos}; " +
                        $"{byteCount} more byte(s) were required.");
                }
            }
        }
    }
}
