#nullable enable

using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Player;
internal sealed class CameraPixelGridAligner
{
    private readonly IClientConfigManager? _clientConfig;

    public CameraPixelGridAligner(IClientConfigManager? clientConfig)
    {
        _clientConfig = clientConfig;
    }

    public PixelSamplingMode Mode =>
        _clientConfig?.Config?.Display?.PixelSampling ?? PixelSamplingMode.SmoothFiltered;

    public float ResolveOrthographicSize(float desiredSize, float minimumZoom, float maximumZoom)
    {
        return PixelSamplingRules.QuantizesZoom(Mode)
            ? PixelGrid.QuantizeOrthographicSize(
                desiredSize,
                EffectiveRenderHeight(),
                minimumZoom,
                maximumZoom)
            : desiredSize;
    }

    public Vector3 SnapPosition(Vector3 position, float orthographicSize)
    {
        if (!PixelSamplingRules.SnapsCameraPosition(Mode))
        {
            return position;
        }

        float snapUnit = PixelGrid.SnapUnit(orthographicSize, EffectiveRenderHeight());
        if (snapUnit <= 0f)
        {
            return position;
        }

        Vector2 snapped = PixelGrid.Snap(new Vector2(position.x, position.y), snapUnit);
        return new Vector3(snapped.x, snapped.y, position.z);
    }

    private static int EffectiveRenderHeight()
    {
        float renderScale = GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp
            ? urp.renderScale
            : 1f;
        return PixelGrid.RenderHeight(Screen.height, renderScale);
    }
}
