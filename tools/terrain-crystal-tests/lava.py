"""CPU execution of production UV addressing, not a GPU render test."""
from pathlib import Path
import re
import subprocess
import tempfile
root=Path(__file__).resolve().parents[2]
shim=(root/'tools/lighting-tests/NativeTransportShim.cpp').read_text()
extra='''
float clamp(float a,float b,float c){return std::clamp(a,b,c);}
float2 clamp(float2 a,float2 b,float2 c){return {clamp(a.x,b.x,c.x),clamp(a.y,b.y,c.y)};}
float2 clamp(float2 a,float b,float2 c){return clamp(a,make_float2(b),c);}
float2 max(float2 a,float b){return max(a,make_float2(b));}
'''
source='\n'.join((root/'Assets/Shaders'/n).read_text() for n in ['TerrainTileAddressing.hlsl','TerrainSampling.hlsl'])
source=re.sub(r'^#.*$', '',source,flags=re.M)
source=re.sub(r'\b(float[234])\(',r'make_\1(',source)
# Scalar vector splats are implicit in HLSL, explicit with clang vectors.
source=re.sub(r'(res\.(?:tileOffsetUV|availableTileSize|finalUV|minTileUV|maxTileUV)) = 0.0;',r'\1 = make_float2(0.0);',source)
with tempfile.TemporaryDirectory(prefix='kern-lava-') as tmp:
    cpp=Path(tmp)/'test.cpp';exe=Path(tmp)/'test'
    for mutation in (False,True):
        candidate=source.replace('if ((int)(animData.w + 0.5) == 2)','if (false)') if mutation else source
        cpp.write_text(shim+extra+candidate+(Path(__file__).parent/'lava.cpp').read_text())
        subprocess.run(['clang++','-std=c++20','-O2',str(cpp),'-o',str(exe)],check=True)
        result=subprocess.run([str(exe)])
        assert result.returncode != 0 if mutation else result.returncode == 0
print('Old cell-local lava addressing mutation rejected.')
