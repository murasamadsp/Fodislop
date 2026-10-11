#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Kern.Rendering;

// Stock URP owns the world-sized color target and point-filtered final blit.
// Preserve the public camera pose/projection for UI and input outside rendering.
internal sealed class WorldRenderGridBinding : IDisposable
{
    private static readonly int s_densityId = Shader.PropertyToID("_KernWorldGridDensity");
    private readonly Func<Camera, bool> _rendersCamera;
    private readonly Dictionary<Camera, PixelPerfectCamera> _cameras = new();
    private readonly Dictionary<Camera, CameraState> _states = new();

    private readonly record struct CameraState(float Size, float Aspect, Rect Rect);

    public WorldRenderGridBinding(Func<Camera, bool> rendersCamera)
    {
        _rendersCamera = rendersCamera;
        RenderPipelineManager.beginContextRendering += PrepareCameras;
        RenderPipelineManager.beginCameraRendering += BeginRendering;
        RenderPipelineManager.endCameraRendering += EndRendering;
    }

    private void PrepareCameras(ScriptableRenderContext context, List<Camera> cameras)
    {
        foreach (Camera camera in cameras)
        {
            if (_rendersCamera(camera) && camera.orthographic)
            {
                EnsureCamera(camera);
            }
        }
    }

    private PixelPerfectCamera EnsureCamera(Camera camera)
    {
        if (_cameras.TryGetValue(camera, out PixelPerfectCamera? pixels) && pixels != null)
        {
            return pixels;
        }

        if (camera.TryGetComponent<PixelPerfectCamera>(out _))
        {
            throw new InvalidOperationException("World render grid requires sole ownership of PixelPerfectCamera.");
        }

        // Register the native camera callback before its first camera-render
        // event. Adding the component inside that event misses the first frame.
        Rect viewport = camera.pixelRect;
        pixels = camera.gameObject.AddComponent<PixelPerfectCamera>();
        camera.pixelRect = viewport;
        _cameras[camera] = pixels;
        RenderPipelineManager.beginCameraRendering -= FinishCameraSetup;
        RenderPipelineManager.beginCameraRendering += FinishCameraSetup;
        return pixels;
    }

    private void BeginRendering(ScriptableRenderContext context, Camera camera)
    {
        if (!_rendersCamera(camera) || !camera.orthographic)
        {
            return;
        }

        _states[camera] = new CameraState(camera.orthographicSize, camera.aspect, camera.rect);
        PixelPerfectCamera pixels = EnsureCamera(camera);

        pixels.assetsPPU = WorldRenderGrid.PixelsPerCell;
        pixels.refResolutionY = Mathf.Max(2,
            Mathf.CeilToInt(camera.orthographicSize * WorldRenderGrid.PixelsPerCell) * 2);
        pixels.refResolutionX = Mathf.Max(2, Mathf.CeilToInt(pixels.refResolutionY * camera.aspect / 2f) * 2);
        pixels.cropFrame = PixelPerfectCamera.CropFrame.StretchFill;
        pixels.gridSnapping = PixelPerfectCamera.GridSnapping.UpscaleRenderTexture;
        Shader.SetGlobalFloat(s_densityId, WorldRenderGrid.PixelsPerCell);
    }

    private void FinishCameraSetup(ScriptableRenderContext context, Camera camera)
    {
        if (!_states.TryGetValue(camera, out CameraState state))
        {
            return;
        }

        PixelPerfectCamera pixels = _cameras[camera];
        // PixelPerfectCamera calculates the display viewport and owns its final
        // blit. Keep that viewport so URP can scale the reference image to it.
        camera.aspect = (float)pixels.refResolutionX / pixels.refResolutionY;
        camera.ResetProjectionMatrix();
    }

    private void EndRendering(ScriptableRenderContext context, Camera camera)
    {
        if (!_states.Remove(camera, out CameraState state))
        {
            return;
        }

        camera.orthographicSize = state.Size;
        camera.rect = state.Rect;
        camera.aspect = state.Aspect;
        camera.ResetWorldToCameraMatrix();
        camera.ResetProjectionMatrix();
        Shader.SetGlobalFloat(s_densityId, 0f);
    }

    public void Dispose()
    {
        RenderPipelineManager.beginContextRendering -= PrepareCameras;
        RenderPipelineManager.beginCameraRendering -= BeginRendering;
        RenderPipelineManager.beginCameraRendering -= FinishCameraSetup;
        RenderPipelineManager.endCameraRendering -= EndRendering;
        foreach (PixelPerfectCamera pixels in _cameras.Values)
        {
            if (pixels != null)
            {
                pixels.enabled = false;
                UnityEngine.Object.Destroy(pixels);
            }
        }

        _cameras.Clear();
        _states.Clear();
        Shader.SetGlobalFloat(s_densityId, 0f);
    }
}
