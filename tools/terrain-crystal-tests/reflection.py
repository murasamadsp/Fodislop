"""Auxiliary production-HLSL math tests; does not render the Unity pass."""
from pathlib import Path
import re
import subprocess
import tempfile
from bake_facets import bake, TARGET

assert TARGET.read_bytes() == bake(), 'Facet data differs from authored source bake'
root = Path(__file__).resolve().parents[2]
shim = (root/'tools/lighting-tests/NativeTransportShim.cpp').read_text()
extra = '''
float dot(float3 a,float3 b){return a.x*b.x+a.y*b.y+a.z*b.z;}
float3 make_float3(float2 a,float b){return {a.x,a.y,b};}
float2 normalize(float2 a){return a/length(a);}
float3 normalize(float3 a){return a/std::sqrt(dot(a,a));}
float _TestGradient=1;
int _WorldLightDebugView=0;
float4 GetWorldLightColor(float2 p){float v=1+_TestGradient*p.x;return {v,v,v,1};}
'''
reflection = (root/'Assets/Shaders/TerrainCrystalReflection.hlsl').read_text()
# Compile the lighting-enabled production branch.
reflection = re.sub(r'#if !defined\(KERN_WORLD_LIGHTING\).*?#else', '', reflection, flags=re.S)
heat = (root/'Assets/Shaders/TerrainMoltenHeat.hlsl').read_text()
source = re.sub(r'^#.*$', '', reflection+'\n'+heat, flags=re.M)
source = re.sub(r'\b(float[234])\(',r'make_\1(',source).replace('return 0.0;', 'return make_float3(0,0,0);')
scenario = r'''
float3 reflection(float3 facets) {
    return EvaluateXGreenReflection(facets,make_float2(0),make_float3(1,1,1),
        make_float2(1,0),make_float2(0,1),make_float2(1,0),make_float2(0,1));
}
int main(){
    float3 plane=make_float3(.825f,.5f,1);
    auto toward=reflection(plane);
    auto mirrored=EvaluateXGreenReflection(plane,make_float2(0),make_float3(1,1,1),
        make_float2(-1,0),make_float2(0,1),make_float2(1,0),make_float2(0,1));
    if(mirrored.y>toward.y*.1f) return 6;
    _TestGradient=-1;
    auto away=reflection(plane);
    if(toward.y<.5f || away.y>toward.y*.1f) return 1;
    _TestGradient=0;
    if(length(reflection(plane).xy)>1e-6) return 2;
    _TestGradient=1;
    if(length(reflection(make_float3(.825f,.5f,0)).xy)>1e-6) return 3;
    auto dark=EvaluateMoltenHeat(make_float3(0,0,0),make_float3(1,.5f,1),0);
    if(dot(dark,dark)>0) return 4;
    auto hot=EvaluateMoltenHeat(make_float3(.8f,.3f,.03f),make_float3(1,.5f,1),0);
    auto cold=EvaluateMoltenHeat(make_float3(.8f,.3f,.03f),make_float3(1,.5f,1),3.14159265f);
    if(hot.y-cold.y<.3f) return 5;
    puts("Reflection responds to light direction; matrix/flat light rejected; lava heat contrast passed.");
}
'''
with tempfile.TemporaryDirectory(prefix='kern-facets-') as tmp:
    cpp=Path(tmp)/'test.cpp';exe=Path(tmp)/'test'
    cpp.write_text(shim+extra+source+scenario)
    subprocess.run(['clang++','-std=c++20','-O2',str(cpp),'-o',str(exe)],check=True)
    subprocess.run([str(exe)],check=True)
