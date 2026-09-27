#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern;
using Kern.Core.Interfaces;
using MinesServer.Networking.Server.Packets.GUI.Components.Visual;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;

namespace Kern.UI.Builders;
public class ImagePacketBuilder : PacketUIBuilderBase<ImagePacket>
{
    protected override VisualElement BuildTyped(ImagePacket imagePacket, PacketUIBuilder builder)
    {
        VisualElement element = BuildUriImage(imagePacket.URI, builder);
        element.style.width = imagePacket.Width;
        element.style.height = imagePacket.Height;
        return element;
    }

    internal static VisualElement BuildUriImage(string uri, PacketUIBuilder builder)
    {
        var element = new VisualElement();

        var cts = new CancellationTokenSource();
        element.RegisterCallback<DetachFromPanelEvent>(_ =>
        {
            cts.Cancel();
            cts.Dispose();
        });

        builder.Operations.Run(
            $"load_packet_image_{uri}",
            supervisorToken => LoadImage(
                element,
                uri,
                builder.AssetLoader,
                cts.Token,
                supervisorToken));

        return element;
    }

    private static async UniTask LoadImage(
        VisualElement element,
        string uri,
        IAssetLoader loader,
        CancellationToken elementToken,
        CancellationToken supervisorToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            elementToken,
            supervisorToken);
        CancellationToken token = linkedCancellation.Token;
        Texture2D? texture;
        try
        {
            texture = await GetTexture(uri, loader, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[PacketUI] Image '{uri}' failed to load: {exception}");
            return;
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        if (texture == null)
        {
            Debug.LogWarning(
                $"[ImagePacketBuilder] Optional image '{uri}' returned no texture; skipped.");
            return;
        }

        if (element != null)
        {
            element.style.backgroundImage = new StyleBackground(texture);
        }
    }

    private static async UniTask<Texture2D?> GetTexture(
        string uri,
        IAssetLoader loader,
        CancellationToken token)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? absolute) ||
            (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps))
        {
            return await loader.GetTextureAsync(uri, token);
        }

        using UnityWebRequest request = UnityWebRequest.Get(uri);
        await request.SendWebRequest().WithCancellation(token);
        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new InvalidOperationException(
                $"HTTP image request failed: {request.error} ({request.responseCode}).");
        }

        byte[] bytes = request.downloadHandler.data;
        if (absolute.AbsolutePath.EndsWith(".m3g", StringComparison.OrdinalIgnoreCase))
        {
            return M3gImageDecoder.Decode(bytes);
        }

        return RuntimeTextureFactory.DecodeEncodedImageToRGBA32NoMip(
            bytes,
            $"PacketUI_{absolute.AbsolutePath}",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp,
            makeNoLongerReadable: true);
    }
}

internal static class M3gImageDecoder
{
    private const int HeaderLength = 14;
    private const int MaximumDecodedBytes = 64 * 1024 * 1024;

    public static Texture2D Decode(byte[] source)
    {
        if (source.Length < HeaderLength + 1)
        {
            throw new InvalidOperationException("M3G image has an incomplete header.");
        }

        int width = source[0] | source[1] << 8;
        int height = source[2] | source[3] << 8;
        long pixelBytes = (long)width * height * 4;
        if (width == 0 || height == 0 || pixelBytes > MaximumDecodedBytes)
        {
            throw new InvalidOperationException($"Invalid M3G dimensions {width}x{height}.");
        }

        byte[] data = source[HeaderLength..];
        for (int index = 4; index < HeaderLength; index++)
        {
            switch (source[index])
            {
                case 0:
                    break;
                case 1:
                    byte sum = 0;
                    for (int offset = 0; offset < data.Length; offset++)
                    {
                        sum = unchecked((byte)(sum + data[offset]));
                        data[offset] = sum;
                    }

                    break;
                case 2:
                    data = DecodeNgrams(data);
                    break;
                case 3:
                    data = DecodeRuns(data);
                    break;
                case 4:
                case 5:
                    return CreateTexture(data, width, height, source[index] == 5);
                default:
                    throw new InvalidOperationException($"Unsupported M3G operation {source[index]}.");
            }
        }

        throw new InvalidOperationException("M3G image has no pixel packing operation.");
    }

    private static byte[] DecodeRuns(byte[] source)
    {
        if (source.Length < 2)
        {
            throw new InvalidOperationException("M3G run dictionary is incomplete.");
        }

        byte marker = source[0];
        byte longMarker = source[1];
        var output = new System.Collections.Generic.List<byte>(source.Length);
        for (int index = 2; index < source.Length;)
        {
            byte value = source[index++];
            if (value == marker || value == longMarker)
            {
                int count;
                if (value == marker)
                {
                    Require(source, index, 2);
                    count = source[index++];
                }
                else
                {
                    Require(source, index, 3);
                    count = source[index++] | source[index++] << 8;
                }

                byte repeated = source[index++];
                RequireOutput(output.Count, count);
                for (int repeat = 0; repeat < count; repeat++)
                {
                    output.Add(repeated);
                }
            }
            else
            {
                RequireOutput(output.Count, 1);
                output.Add(value);
            }
        }

        return [.. output];
    }

    private static byte[] DecodeNgrams(byte[] source)
    {
        const int dictionaryLimit = 1560;
        if (source.Length < 2)
        {
            throw new InvalidOperationException("M3G ngram dictionary is incomplete.");
        }

        byte separator = source[0];
        int[] dictionary = new int[256];
        int index = 1;
        byte current = source[index++];
        while (index < dictionaryLimit && current != separator)
        {
            dictionary[current] = index;
            while (index < dictionaryLimit && current != separator)
            {
                Require(source, index, 1);
                current = source[index++];
            }

            if (index < dictionaryLimit)
            {
                Require(source, index, 1);
                current = source[index++];
            }
        }

        if (index >= dictionaryLimit)
        {
            throw new InvalidOperationException("M3G ngram dictionary exceeds its header limit.");
        }

        var output = new System.Collections.Generic.List<byte>(source.Length);
        while (index < source.Length)
        {
            current = source[index++];
            int dictionaryIndex = dictionary[current];
            if (dictionaryIndex > 0)
            {
                while (true)
                {
                    Require(source, dictionaryIndex, 1);
                    byte value = source[dictionaryIndex++];
                    if (value == separator)
                    {
                        break;
                    }

                    RequireOutput(output.Count, 1);
                    output.Add(value);
                }
            }
            else if (current == separator)
            {
                Require(source, index, 1);
                RequireOutput(output.Count, 1);
                output.Add(source[index++]);
            }
            else
            {
                RequireOutput(output.Count, 1);
                output.Add(current);
            }
        }

        return [.. output];
    }

    private static Texture2D CreateTexture(byte[] source, int width, int height, bool planar)
    {
        int pixels = checked(width * height);
        Require(source, 0, checked(pixels * 4));
        byte[] rgba = new byte[pixels * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int sourcePixel = y * width + x;
                int destination = ((height - 1 - y) * width + x) * 4;
                if (planar)
                {
                    rgba[destination] = source[sourcePixel];
                    rgba[destination + 1] = source[pixels + sourcePixel];
                    rgba[destination + 2] = source[2 * pixels + sourcePixel];
                    rgba[destination + 3] = source[3 * pixels + sourcePixel];
                }
                else
                {
                    int offset = sourcePixel * 4;
                    rgba[destination] = source[offset];
                    rgba[destination + 1] = source[offset + 1];
                    rgba[destination + 2] = source[offset + 2];
                    rgba[destination + 3] = source[offset + 3];
                }
            }
        }

        Texture2D texture = RuntimeTextureFactory.CreateRGBA32NoMip(
            width,
            height,
            "PacketUI_M3G",
            RuntimeTextureColorSpace.Srgb,
            FilterMode.Point,
            TextureWrapMode.Clamp);
        texture.LoadRawTextureData(rgba);
        texture.Apply(false, true);
        return texture;
    }

    private static void Require(byte[] source, int index, int count)
    {
        if (index < 0 || count < 0 || index > source.Length - count)
        {
            throw new InvalidOperationException("M3G image data is truncated.");
        }
    }

    private static void RequireOutput(int count, int additional)
    {
        if (additional < 0 || count > MaximumDecodedBytes - additional)
        {
            throw new InvalidOperationException("M3G decoded image exceeds the size limit.");
        }
    }
}
