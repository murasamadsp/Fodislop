#nullable enable

using UnityEngine;

namespace Fodinae;
public readonly struct AnimatedSpriteData
{
    public AnimatedSpriteData(Sprite[] frames, float fps, int frameHeight)
    {
        Frames = frames;
        FPS = fps;
        FrameHeight = frameHeight;
    }

    public Sprite[] Frames { get; }
    public float FPS { get; }
    public int FrameHeight { get; }

    public float FrameDuration => 1f / Mathf.Max(1f, FPS);
}
