#nullable enable

using System;
using System.Threading;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.World.Lighting;
using MinesServer.Data;
using UnityEngine;

namespace Kern.Game;

// D and G share their original sprite animation. Audio remains owned by SFX.
public sealed class CellDestructionEffect : IServerWorldEffect
{
    // RobotLighting owns positive ids; transient effect sources own negative ids.
    private static int s_nextLightId;
    private readonly CellDestructionClip _clip;
    private readonly LightingEngine _lighting;
    private readonly int _lightId;
    private readonly double _createdAtSeconds;
    private readonly GameObject _object;
    private readonly SpriteRenderer _renderer;
    private float _elapsedSeconds;
    private int _frameIndex;

    public CellDestructionEffect(SFX variant, Vector3 position,
        CellDestructionClip clip, ISceneObjectFactory objects, LightingEngine lighting)
    {
        if (!IsCellDestruction(variant))
        {
            throw new ArgumentOutOfRangeException(nameof(variant));
        }

        _clip = clip;
        _lighting = lighting;
        _lightId = Interlocked.Decrement(ref s_nextLightId);
        _createdAtSeconds = Time.timeAsDouble;
        _object = objects.Create(variant == SFX.Destroy ? "CellDestructionD" : "CellDestructionG", RuntimeOwner.VFX);
        _object.transform.SetPositionAndRotation(position, Quaternion.identity);
        _object.transform.localScale = Vector3.one;
        _renderer = _object.AddComponent<SpriteRenderer>();
        _renderer.sharedMaterial = clip.Material;
        _renderer.sortingOrder = clip.SortingOrder;
        _renderer.color = Color.white;
        _renderer.sprite = clip.Frames[0];
        SubmitEmission();
    }

    public bool IsDisposed { get; private set; }
    internal SpriteRenderer Renderer => _renderer;

    public static bool IsCellDestruction(SFX effect) => effect is SFX.Destroy or SFX.Geology;

    public void Update() => SetElapsed((float)(Time.timeAsDouble - _createdAtSeconds));

    internal void Advance(float deltaTimeSeconds) => SetElapsed(_elapsedSeconds + deltaTimeSeconds);

    private void SetElapsed(float elapsedSeconds)
    {
        if (IsDisposed)
        {
            return;
        }

        // The packet can arrive partway through a frame. Counting the whole
        // frame's delta on birth would age the effect before it ever rendered.
        _elapsedSeconds = elapsedSeconds;
        int frameIndex = _clip.FrameIndexAt(_elapsedSeconds);
        if (frameIndex < 0)
        {
            Dispose();
            return;
        }

        if (_frameIndex != frameIndex)
        {
            _frameIndex = frameIndex;
            _renderer.sprite = _clip.Frames[frameIndex];
        }

        // Also re-submit unchanged frames after a lighting generation reset.
        // The light owner suppresses unchanged source data itself.
        SubmitEmission();
    }

    private void SubmitEmission() => _lighting.SetDynamicLight(
        // The dynamic-light authoring integral is independent of the sprite
        // compositing conversion. Apply the normal world-source gain once.
        _lightId, _object.transform.position, _clip.EmissionAt(_frameIndex), 1f);

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        _lighting.RemoveDynamicLight(_lightId);
        _renderer.enabled = false;
        UnityEngine.Object.Destroy(_object);
    }
}
