#nullable enable

using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Kern.Rendering.UrpIntegration;

/// <summary>Explicit frame-local depth ownership for the world raster target.</summary>
public static class WorldRasterAttachmentBinding
{
    // Compiled into URP through the adjacent asmref: the active attachment
    // selector is internal. Keep this adapter free of game assembly references.
    public static void SetCameraDepth(UniversalResourceData resources, TextureHandle depth)
    {
        resources.cameraDepth = depth;
        resources.activeDepthID = UniversalResourceData.ActiveID.Camera;
    }
}
