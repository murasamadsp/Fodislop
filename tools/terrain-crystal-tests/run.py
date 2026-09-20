#!/usr/bin/env python3
"""Auxiliary CPU checks of production HLSL. Does NOT validate Unity rendering."""
from pathlib import Path
import re
import subprocess
import tempfile
from generate import bake, field, TARGET

root = Path(__file__).resolve().parents[2]
assert TARGET.read_bytes() == bake(), 'Phase asset differs from its generator'
for i in range(101):
    t = i / 100
    for a, b in ((field(0, t), field(1, t)), (field(t, 0), field(t, 1))):
        assert max(abs(x-y) for x, y in zip(a, b)) < 1e-12
shader = (root / 'Assets/Shaders/TerrainPrismaticCrystal.hlsl').read_text()
shader = re.sub(r'^#.*$', '', shader, flags=re.M)
shader = re.sub(r'\b(float[234])\(', r'make_\1(', shader)
shim = (root / 'tools/lighting-tests/NativeTransportShim.cpp').read_text()
extra = '\nfloat dot(float3 a, float3 b) { return a.x*b.x+a.y*b.y+a.z*b.z; }\n'
scenario = r'''
int main() {
    // Shared horizontal/vertical edges must sample the same field, including
    // negative coordinates, chunk boundaries, and displaced contour points.
    for (int x : {-257,-32,-1,0,31,32,255})
    for (int y : {-257,-32,-1,0,31,32,255})
    for (int i=0;i<=32;i++) {
        float t=i/32.f;
        float2 a=PrismaticCrystalFlowUV(make_float2(float(x),float(y)),make_float2(1.125f,t));
        float2 b=PrismaticCrystalFlowUV(make_float2(float(x+1),float(y)),make_float2(.125f,t));
        float2 c=PrismaticCrystalFlowUV(make_float2(float(x),float(y)),make_float2(t,-.125f));
        float2 d=PrismaticCrystalFlowUV(make_float2(float(x),float(y+1)),make_float2(t,.875f));
        if (dot(a-b,a-b)>1e-10f || dot(c-d,c-d)>1e-10f) return 1;
    }
    float largestChange=0;
    for (int palette=1;palette<=5;palette++)
    for (int t=0;t<256;t++)
    for (int level=0;level<=32;level++) {
        float v=level/32.f;
        float3 base={v,v*.8f,v*.6f};
        float3 flow={.8f,.9f,.7f};
        float3 color=EvaluatePrismaticCrystal(base,flow,palette,t*.05f);
        float3 next=EvaluatePrismaticCrystal(base,flow,palette,t*.05f+.005f);
        for(int k=0;k<3;k++) {
            if (!std::isfinite(color[k]) || color[k]<base[k]*.5999f || color[k]>1.001f) return 2;
            if (v==0 && color[k]!=0) return 3;
            if (std::abs(color[k]-next[k])>.025f) return 4;
            largestChange=std::max(largestChange,std::abs(color[k]-base[k]));
        }
    }
    if(largestChange<.08f) return 5; // catches a disabled effect
    puts("Crystal math: continuity, bounded color, dark crevices and temporal change passed.");
}
'''
with tempfile.TemporaryDirectory(prefix='kern-crystal-') as tmp:
    cpp=Path(tmp)/'check.cpp'; exe=Path(tmp)/'check'
    for mutation in (False, True):
        source=shader.replace('serverCell.y - localPosition.y', 'serverCell.y + localPosition.y') if mutation else shader
        cpp.write_text(shim+extra+source+scenario)
        subprocess.run(['clang++','-std=c++20','-O2',str(cpp),'-o',str(exe)],check=True)
        result=subprocess.run([str(exe)])
        assert (result.returncode != 0) if mutation else (result.returncode == 0)
print('Phase asset reproducible; periodic seams passed; wrong-Y mutation rejected.')

subprocess.run(['python3', str(Path(__file__).parent / 'lava.py')], check=True)

subprocess.run(['python3', str(Path(__file__).parent / 'reflection.py')], check=True)
