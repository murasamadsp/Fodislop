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
# Coverage functions first; the relief rim is appended after the lighting
# transport because it decodes the packed contour word.
rim = contour[contour.index('// Кайма рельефа'):contour.index('// The visible terrain')]
contour = contour[:contour.index('float2 QuantizeTerrainFaceUV')]
quantize_uv = '''
float2 QuantizeTerrainFaceUV(float2 uv)
{
    float2 pixel = floor(uv * KERN_TERRAIN_FACE_GRID_SIZE);
    return (pixel + 0.5) / KERN_TERRAIN_FACE_GRID_SIZE;
}
'''
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
    for mutation in (None, "polygon-carrier", "ao-wide-mip", "relief-rim-inverted", "relief-rim-steep",
                     "relief-rim-unclamped"):
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
        candidate_rim = rim
        if mutation == "relief-rim-inverted":
            # Reproduce the defect the equality rule fixes: the rim lands on
            # the sides that belong to the same family instead of the foreign
            # ones, so a seam is drawn through the middle of a solid mass.
            candidate_rim = candidate_rim.replace(
                'int foreignSides = (~(reliefCode - 1)) & 0x0F;',
                'int foreignSides = (reliefCode - 1) & 0x0F;')
            if candidate_rim == rim:
                raise RuntimeError('Relief mutation no longer matches the production source')
        if mutation == "relief-rim-unclamped":
            # Reproduce the black band along every distorted cell: the rim read
            # the carrier coordinate, which reaches past the cell on an
            # anchored quad, and bottomed out at zero there.
            candidate_rim = candidate_rim.replace(
                'saturate(QuantizeTerrainFaceUV(contourSample))',
                'QuantizeTerrainFaceUV(contourSample)')
            if candidate_rim == rim:
                raise RuntimeError('Relief clamp mutation no longer matches the production source')
        if mutation == "relief-rim-steep":
            # Reproduce the first shipped defect: the rim scaled by half a side
            # instead of half a diagonal, bottoming out at pure black and
            # outlining every crystal cell separately.
            candidate_rim = candidate_rim.replace(
                '* KERN_TERRAIN_RELIEF_RIM_SCALE;', '* 2.0;')
            if candidate_rim == rim:
                raise RuntimeError('Relief scale mutation no longer matches the production source')
        cpp.write_text(
            shim + extra + ao_shim +
            translate(candidate + contour + lighting + quantize_uv + candidate_rim + candidate_ao) +
            scenario)
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
