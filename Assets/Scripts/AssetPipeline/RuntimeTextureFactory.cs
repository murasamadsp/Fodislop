#nullable enable

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fodinae;

public enum RuntimeTextureColorSpace
{
    Srgb,
    Linear,
}

public static class RuntimeTextureFactory
{
    public static bool SupportsTexture2DGpuCopy =>
        (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;

    public static Texture2D CreateRgba32NoMip(
        int width,
        int height,
        string name,
        RuntimeTextureColorSpace colorSpace,
        FilterMode filterMode,
        TextureWrapMode wrapMode)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                "Runtime texture width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                "Runtime texture height must be positive.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Runtime texture name cannot be null or whitespace.",
                nameof(name));
        }

        if (width > SystemInfo.maxTextureSize)
        {
            string message = $"Runtime texture '{name}' width exceeds " +
                $"the GPU limit {SystemInfo.maxTextureSize}.";
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                message);
        }

        if (height > SystemInfo.maxTextureSize)
        {
            string message = $"Runtime texture '{name}' height exceeds " +
                $"the GPU limit {SystemInfo.maxTextureSize}.";
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                message);
        }

        var texture = new Texture2D(
            width,
            height,
            TextureFormat.RGBA32,
            mipChain: false,
            linear: colorSpace == RuntimeTextureColorSpace.Linear)
        {
            name = name,
        };
        ApplySampling(texture, filterMode, wrapMode);
        return texture;
    }

    public static Texture2DArray CreateRgba32ArrayNoMip(
        int width,
        int height,
        int depth,
        string name,
        RuntimeTextureColorSpace colorSpace,
        FilterMode filterMode,
        TextureWrapMode wrapMode)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Runtime texture array width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "Runtime texture array height must be positive.");
        }

        if (depth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), depth, "Runtime texture array depth must be positive.");
        }

        var array = new Texture2DArray(
            width,
            height,
            depth,
            TextureFormat.RGBA32,
            mipChain: false,
            linear: colorSpace == RuntimeTextureColorSpace.Linear)
        {
            name = name,
            filterMode = filterMode,
            wrapMode = wrapMode,
        };
        return array;
    }

    public static Texture2D DecodeEncodedImageToRgba32NoMip(
        byte[] data,
        string name,
        RuntimeTextureColorSpace colorSpace,
        FilterMode filterMode,
        TextureWrapMode wrapMode,
        bool makeNoLongerReadable)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        if (data.Length == 0)
        {
            throw new ArgumentException(
                "Encoded image data cannot be empty.",
                nameof(data));
        }

        Texture2D staging = CreateRgba32NoMip(
            2,
            2,
            $"Decoding_{name}",
            colorSpace,
            filterMode,
            wrapMode);
        try
        {
            if (!staging.LoadImage(data, markNonReadable: false))
            {
                throw new InvalidOperationException(
                    $"Encoded image '{name}' could not be decoded by Unity.");
            }

            return CopyToRgba32NoMip(
                staging,
                name,
                colorSpace,
                filterMode,
                wrapMode,
                makeNoLongerReadable);
        }
        finally
        {
            DestroyRuntimeObject(staging);
        }
    }

    public static Texture2D CopyToRgba32NoMip(
        Texture2D source,
        string name,
        RuntimeTextureColorSpace colorSpace,
        FilterMode filterMode,
        TextureWrapMode wrapMode,
        bool makeNoLongerReadable)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (!source.isReadable)
        {
            throw new InvalidOperationException(
                $"Texture '{source.name}' must be readable before conversion to RGBA32.");
        }

        Texture2D result = CreateRgba32NoMip(
            source.width,
            source.height,
            name,
            colorSpace,
            filterMode,
            wrapMode);
        try
        {
            result.SetPixels32(source.GetPixels32());
            result.Apply(
                updateMipmaps: false,
                makeNoLongerReadable: makeNoLongerReadable);
            return result;
        }
        catch
        {
            DestroyRuntimeObject(result);
            throw;
        }
    }

    public static void ApplySampling(
        Texture texture,
        FilterMode filterMode,
        TextureWrapMode wrapMode)
    {
        if (texture == null)
        {
            throw new ArgumentNullException(nameof(texture));
        }

        texture.filterMode = filterMode;
        texture.wrapMode = wrapMode;
        texture.anisoLevel = 0;
    }

    private static void DestroyRuntimeObject(UnityEngine.Object runtimeObject)
    {
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(runtimeObject);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(runtimeObject);
        }
    }
}
