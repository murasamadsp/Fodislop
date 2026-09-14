#!/usr/bin/env python3
"""Compile HLSL without Unity. Pass the path to glslang/glslangValidator."""
from pathlib import Path
import re
import subprocess
import sys
import tempfile

root = Path(__file__).resolve().parents[2]
validator = sys.argv[1] if len(sys.argv)>1 else 'glslangValidator'
compute = root/'Assets/Resources/Shaders/Lighting/WorldLighting.compute'
with tempfile.TemporaryDirectory(prefix='fodinae-hlsl-') as directory:
    work = Path(directory)
    def compile_shader(path, stage, entry):
        result = subprocess.run([validator,'-D','-V','-S',stage,'-e',entry,str(path),'-o',str(work/(entry+'.spv'))],capture_output=True,text=True)
        if result.returncode:
            print(result.stdout+result.stderr)
            raise SystemExit(result.returncode)
        print(entry+': PASS')
    for kernel in re.findall(r'^#pragma kernel (\w+)',compute.read_text(),re.M):
        compile_shader(compute,'comp',kernel)
    source = (root/'Assets/Resources/Shaders/Lighting/DynamicEmission.shader').read_text()
    source = source.split('HLSLPROGRAM',1)[1].split('ENDHLSL',1)[0]
    # Validate our vertex/fragment bodies; URP's transform is a fixture, not an import.
    source = re.sub(r'#include[^\n]*', 'float4 TransformWorldToHClip(float3 p) { return float4(p, 1.0); }', source)
    path=work/'DynamicEmission.hlsl'
    path.write_text(source)
    compile_shader(path,'vert','DynamicEmissionVert')
    compile_shader(path,'frag','DynamicEmissionFrag')
