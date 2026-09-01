#nullable enable

using System;
using System.IO;
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
            return GifAnimationDecoder.Decode(data);
        }

        public static DecodedAnimation DecodeWebP(byte[] data)
        {
            return WebPAnimationDecoder.Decode(data);
        }

        public struct DecodedAnimation
        {
            public Texture2D Atlas { get; set; }

            public int FrameCount { get; set; }

            public int FrameHeight { get; set; }

            public float FPS { get; set; }
        }
    }
}
