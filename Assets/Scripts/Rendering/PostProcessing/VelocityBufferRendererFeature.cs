#nullable enable

using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Rendering.PostProcessing
{

public class VelocityBufferRendererFeature : ScriptableRendererFeature
{
    private VelocityBufferRenderPass? _pass;
    private Shader? _velocityShader;

    public override void Create()
    {
        _velocityShader = Shader.Find("Fodinae/PostProcessing/Velocity");
        if (_velocityShader == null)
        {
            _velocityShader = Resources.Load<Shader>("Shaders/PostProcessing/Velocity");
        }

        if (_velocityShader != null)
        {
            _pass = new VelocityBufferRenderPass(_velocityShader);
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_pass != null)
        {
            renderer.EnqueuePass(_pass);
        }
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
        _pass = null;
    }
}
}
