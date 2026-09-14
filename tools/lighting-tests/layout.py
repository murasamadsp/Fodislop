#!/usr/bin/env python3
"""Compile production layout/binder C# against disposable non-Unity fixtures."""
from pathlib import Path
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
files = [
    'Assets/Scripts/World/Lighting/Core/LightingCascadeLayout.cs',
    'Assets/Scripts/World/Lighting/Core/CascadeLayoutBuilder.cs',
    'Assets/Scripts/World/Lighting/Core/CascadeCostCalculator.cs',
    'Assets/Scripts/World/Lighting/Core/LightingComputeBinder.cs',
]
shim = '''
#nullable enable
using System;
namespace UnityEngine
{
    public static class Mathf
    {
        public static float Abs(float x) => MathF.Abs(x);
        public static float Sqrt(float x) => MathF.Sqrt(x);
        public static float Log(float x) => MathF.Log(x);
        public static int CeilToInt(float x) => (int)MathF.Ceiling(x);
        public static int Min(int a, int b) => Math.Min(a,b);
        public static float Min(float a, float b) => Math.Min(a,b);
        public static float Max(float a, float b) => Math.Max(a,b);
        public static int Max(int a, int b) => Math.Max(a,b);
        public static float Clamp(float a, float b, float c) => Math.Clamp(a,b,c);
        public static int Clamp(int a, int b, int c) => Math.Clamp(a,b,c);
    }
    public readonly record struct Vector4(float x, float y, float z, float w);
    public readonly record struct Color(float r, float g, float b, float a=1f)
    {
        public static Color white => new(1,1,1,1);
        public static Color operator *(Color c,float m) => new(c.r*m,c.g*m,c.b*m,c.a*m);
    }
    public sealed class ComputeShader { }
    public sealed class RenderTexture { }
    public static class Shader { public static int PropertyToID(string name) => name.GetHashCode(); }
    public static class SystemInfo { public static bool graphicsUVStartsAtTop => false; }
}
namespace UnityEngine.Rendering
{
    public sealed class CommandBuffer
    {
        public readonly System.Collections.Generic.Dictionary<int, UnityEngine.Color> Colors = new();
        public void SetComputeVectorParam(UnityEngine.ComputeShader c,int id,UnityEngine.Color value) => Colors[id]=value;
        public void SetComputeVectorParam(params object[] args) { }
        public void SetComputeTextureParam(params object[] args) { }
        public void SetComputeFloatParam(params object[] args) { }
        public void SetComputeIntParams(params object[] args) { }
        public void SetComputeIntParam(params object[] args) { }
    }
}
namespace Fodinae.Core { public readonly struct GraphicsQualitySettings { } }
namespace Fodinae.Rendering { }
namespace Fodinae.World.Terrain
{
    public static class TerrainLook
    {
        public const float AmbientOcclusionMip=1.5f,AmbientOcclusionStrength=1f;
    }
}
namespace Fodinae.World.Lighting.Quality { public enum LightingQualityMode { PerBlock,PerPixel } }
namespace Fodinae.World.Lighting { public class LightingEngine { public enum DebugView { FinalLighting } } }
'''
program = '''
#nullable enable
using System;
using System.Collections.Generic;
using Fodinae.World.Lighting;
using UnityEngine;
using UnityEngine.Rendering;
int checks=0;
void Check(bool value, string label) { checks++; if(!value)throw new Exception(label); }
var random=new Random(7103);
for(int i=0;i<1000;i++)
{
    int width=random.Next(1,2049),height=random.Next(1,2049),atlas=random.Next(64,2049);
    var cascades=new List<CascadeLayout>();
    CascadeLayoutBuilder.BuildCascadeLayouts(width,height,atlas,cascades);
    Check(cascades[^1].IntervalEnd >= MathF.Sqrt(width*width+height*height),"last interval must cover field");
    long expected=CascadeLayoutBuilder.CalculateCascadeEntryCount(width,height,CascadeLayoutBuilder.GetMaximumCascadeCount(atlas));
    Check((long)cascades[^1].Offset+cascades[^1].EntryCount==expected,"same atlas budget");
    var costs=new List<CascadeCostSample>();var other=new List<CascadeCostSample>();
    CascadeCostCalculator.CollectCascadeCosts(cascades,1,costs);
    CascadeCostCalculator.CollectCascadeCosts(cascades,64,other);
    Check(costs.Count==cascades.Count,"cost count");
    for(int c=0;c<costs.Count;c++)
    {
        Check(costs[c]==other[c],"legacy budget cannot affect physical traversal");
        Check(costs[c].StepCount >= 2,"DDA boundary allowance");
    }
    for(int c=1;c<cascades.Count;c++)Check(cascades[c].IntervalStart==cascades[c-1].IntervalEnd,"contiguous intervals");
}
Check(LightingComputeBinder.ResolveTransmittanceDebugDistance()==1f,"fixed debug distance");
Check(LightingComputeBinder.ResolveBlockPropagationIterations(32,32)>20,"no fixed 20-step cutoff");
Check(LightingComputeBinder.ResolveBlockPropagationIterations(2,2)<=3,"path bound");
Check(LightingComputeBinder.ResolveBlockPropagationIterations(1,1)==0,"single cell");
var cb=new CommandBuffer();LightingComputeBinder.BindExtinction(cb,new ComputeShader());
Check(cb.Colors[LightingComputeBinder.EmptyExtinctionRGBID].r==.2f,"empty binding");
Check(cb.Colors[LightingComputeBinder.SolidExtinctionRGBID].r==1600f,"solid binding");
Console.WriteLine($"{checks} C# layout and binding checks passed (production sources, no Unity).");
'''
visual = (root/'Assets/Scripts/Rendering/PostProcessing/ColorGrading/Contracts/VisualTuning.cs').read_text()
start = visual.index('namespace Fodinae.World.Lighting\n')
end = visual.index('\nnamespace ', start+1)
with tempfile.TemporaryDirectory(prefix='fodinae-lighting-layout-') as directory:
    work=Path(directory)
    (work/'Tests.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup></Project>')
    for file in files:
        (work/Path(file).name).write_text((root/file).read_text())
    (work/'Shim.cs').write_text(shim)
    (work/'Tuning.cs').write_text('using UnityEngine;\n'+visual[start:end])
    (work/'Program.cs').write_text(program)
    subprocess.run(['dotnet','run','--project',str(work/'Tests.csproj'),'--configuration','Release','--verbosity','quiet'],check=True,timeout=90)
