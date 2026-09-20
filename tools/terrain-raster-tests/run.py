#!/usr/bin/env python3
"""Execute production HLSL vertex loading and contour coverage with a CPU rasterizer.
This does not validate Unity shader compilation, binding, or the final camera image.
"""
from pathlib import Path
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
shim = (root / 'tools/lighting-tests/NativeTransportShim.cpp').read_text()
loader = (root / 'Assets/Shaders/TerrainCellData.hlsl').read_text()
contour = (root / 'Assets/Shaders/TerrainContour.hlsl').read_text()
# Only the polygon coverage functions are needed; no substitute implementation.
contour = contour[:contour.index('float2 QuantizeTerrainFaceUV')]
extra = '''
float2 round(float2 a) { return {std::round(a.x), std::round(a.y)}; }
float2 lerp(float2 a, float2 b, float2 t) { return a + (b-a)*t; }
float4 make_float4(float a, float2 b, float c) { return {a,b.x,b.y,c}; }
'''

def translate(source):
    source = re.sub(r'^#.*$', '', source, flags=re.M)
    source = source.replace('Texture2D<float4>', 'Texture')
    source = source.replace('(TerrainCellVertex)0', 'TerrainCellVertex{}')
    source = re.sub(r'\(int2\)round\(([^)]+)\)', r'make_int2(round(\1))', source)
    return re.sub(r'\b(float[234]|int[23]|uint[23])\(', r'make_\1(', source)

fixture = Path(__file__).parent
scenario = (fixture / 'scenario.cpp').read_text()
ao_shim = (fixture / 'ao-pyramid.cpp').read_text()
ao = (root / 'Assets/Shaders/TerrainAmbientOcclusion.hlsl').read_text()
lighting = (root / 'Assets/Shaders/TerrainLightingData.hlsl').read_text()
with tempfile.TemporaryDirectory(prefix='kern-terrain-raster-') as directory:
    cpp = Path(directory) / 'test.cpp'
    executable = Path(directory) / 'test'
    for mutation in (None, "polygon-carrier", "ao-wide-mip"):
        candidate = loader
        candidate_ao = ao
        if mutation == "polygon-carrier":
            # Reproduce the old defect: the polygon itself is the raster carrier.
            candidate = candidate.replace(
                'carrierCorner = lerp(boundsMin, boundsMax, cornerBase);',
                'carrierCorner = float2(geometryX[corner], geometryY[corner]);')
            if candidate == loader:
                raise RuntimeError('Mutation no longer matches the production source')
        if mutation == "ao-wide-mip":
            candidate_ao = candidate_ao.replace(
                'max(log2(texelsPerCell) - 1.0, 0.0)',
                '1.5 + log2(texelsPerCell)')
            if candidate_ao == ao:
                raise RuntimeError('AO mutation no longer matches the production source')
        candidate_ao = candidate_ao.replace('Texture2D<float4>', 'AoTexture').replace('SamplerState', 'int')
        cpp.write_text(shim + extra + ao_shim + translate(candidate + contour + lighting + candidate_ao) + scenario)
        subprocess.run(['clang++', '-std=c++20', '-O2', '-ffp-contract=off', str(cpp), '-o', str(executable)], check=True)
        result = subprocess.run([str(executable)], capture_output=True, text=True)
        if mutation:
            if result.returncode == 0:
                raise RuntimeError(f'Regression check accepted mutation: {mutation}')
            print(f'Mutation {mutation} rejected: ' + result.stderr.strip())
        else:
            print(result.stdout, end='')
            if result.returncode:
                raise RuntimeError(result.stderr)
