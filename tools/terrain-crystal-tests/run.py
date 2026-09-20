#!/usr/bin/env python3
"""Auxiliary production HLSL comparison against OpenMines animType 5 math.
Does not validate Unity compilation, texture binding or final rendering.
"""
from pathlib import Path
import colorsys
import hashlib
import math
import random
import re
import struct
import subprocess
import tempfile

root=Path(__file__).resolve().parents[2]
raw=(root/'Assets/Resources/PrismaticFlowMap.bytes').read_bytes()
assert hashlib.sha256(raw).hexdigest() == '1800d15643ec50e669ef483b053db98a651b18072e46872c2e47fb65f37a4756', 'Original phase data changed'
shader=(root/'Assets/Shaders/TerrainPrismaticCrystal.hlsl').read_text()
shader=re.sub(r'^#.*$', '', shader, flags=re.M)
shader=re.sub(r'\b(float[234])\(',r'make_\1(',shader)
shim=(root/'tools/lighting-tests/NativeTransportShim.cpp').read_text()
extra='\nfloat dot(float3 a,float3 b){return a.x*b.x+a.y*b.y+a.z*b.z;}\n'
# Independently evaluated gamma-space equation transcribed from OpenMines
# Unlit_TerrainShader.shader:335-373; palette from CellRender.cs:395-434.
palette=[(.2,1,.2),(.2,.2,1),(1,1,1),(.1,1,1),(1,0,0)]
rng=random.Random(71)
cases=bytearray()
for i in range(12000):
    offset=rng.randrange(len(raw)//4)*4
    flow=tuple(c/255 for c in raw[offset:offset+3])
    base=tuple(rng.random() for _ in range(3)) if i%5 else (0.,0.,0.)
    index=i%5
    phase=rng.random()*20
    hue=colorsys.rgb_to_hsv(*flow)[0]
    body=((math.sin(-(hue*6.283185+phase))+1)*.5)**3
    inverse=1-sum(x*w for x,w in zip(base,(.3,.59,.11)))
    expected=tuple(b*(1-body)+c*(max(flow)-min(flow))*body*inverse**3 for b,c in zip(base,palette[index]))
    cases.extend(struct.pack('11f',*base,*flow,index+1,phase,*expected))
scenario=r'''
int main(int argc,char** argv){
    FILE* f=fopen(argv[1],"rb");
    float v[11];int count=0;
    while(fread(v,sizeof(float),11,f)==11){
        auto actual=EvaluatePrismaticCrystal(make_float3(v[0],v[1],v[2]),make_float3(v[3],v[4],v[5]),v[6],v[7]);
        for(int k=0;k<3;k++) if(!std::isfinite(actual[k]) || std::abs(actual[k]-v[k+8])>0.00002f) return 1;
        count++;
    }
    fclose(f);
    for(int x : {-33,0,31,32}) for(int y : {-33,0,31,32}){
        auto a=PrismaticCrystalFlowUV(make_float2(x,y),make_float2(.2f,-.125f));
        auto b=PrismaticCrystalFlowUV(make_float2(x,y+1),make_float2(.2f,.875f));
        if(dot(a-b,a-b)>1e-10) return 2;
    }
    printf("OpenMines reference: %d color samples matched; phase continuity passed.\n",count);
}
'''
with tempfile.TemporaryDirectory(prefix='kern-original-shimmer-') as tmp:
    cpp=Path(tmp)/'test.cpp';exe=Path(tmp)/'test';fixture=Path(tmp)/'cases.bin'
    fixture.write_bytes(cases)
    for mutation in (None,'wrong-y','wrong-luma'):
        candidate=shader
        if mutation=='wrong-y': candidate=candidate.replace('serverCell.y - localPosition.y','serverCell.y + localPosition.y')
        if mutation=='wrong-luma': candidate=candidate.replace('float inverseLuma = 1.0 - dot','float inverseLuma = dot')
        cpp.write_text(shim+extra+candidate+scenario)
        subprocess.run(['clang++','-std=c++20','-O2',str(cpp),'-o',str(exe)],check=True)
        result=subprocess.run([str(exe),str(fixture)])
        assert result.returncode != 0 if mutation else result.returncode == 0
        if mutation: print('Rejected:',mutation)
subprocess.run(['python3',str(Path(__file__).parent/'lava.py')],check=True)
