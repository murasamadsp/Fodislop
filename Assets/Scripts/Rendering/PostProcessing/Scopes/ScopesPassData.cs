#nullable enable

using UnityEngine;
using UnityEngine.Rendering.RenderGraphModule;

namespace Fodinae.Rendering.PostProcessing.Scopes;

internal sealed class ScopesPassData
{
    public ComputeShader ScopesCS = null!;
    public int KernelClear;
    public int KernelGather;
    public int KernelHistogram;
    public int KernelWaveform;
    public int KernelVectorscope;

    public ScopeResources Resources = null!;
    public TextureHandle SourceTexture;
    public int SourceWidth;
    public int SourceHeight;
    public float SignalScale;
}
