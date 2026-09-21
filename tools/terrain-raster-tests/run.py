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
# Coverage first; the rim is appended after the lighting transport because it
# decodes the packed contour word.
rim = contour[contour.index('// Выключатель каймы.'):contour.index('// The visible terrain')]
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


# Оба прохода обязаны разбирать вершину одним и тем же способом.
#
# Координаты теперь ходят структурой TerrainSurfaceInputs, и подать в кайму
# «не ту» координату из прохода нечем — сырая форма называется
# TerrainReliefRimRaw и в шейдере не вызывается. Осталось проверить, что
# проход не собрал структуру дважды по-своему: разбор один на проход, и кайма
# с покрытием берут именно его.
terrain_shader = (root / 'Assets/Shaders/Terrain.shader').read_text()
if 'TerrainReliefRimRaw(' in terrain_shader:
    raise RuntimeError(
        'Terrain.shader must call TerrainReliefRim(surface), not the raw form')
rim_call_arguments = re.findall(r'TerrainReliefRim\(([A-Za-z0-9_.]+)\)', terrain_shader)
if rim_call_arguments != ['surface', 'surface']:
    raise RuntimeError(
        'Expected two relief rim call sites taking the parsed surface, found '
        f'{rim_call_arguments}')
builds = terrain_shader.count('BuildTerrainSurfaceInputs(')
if builds != 2:
    raise RuntimeError(
        f'Expected exactly one surface parse per pass, found {builds}')

# Отладочный вид обязан считаться до вырезания: после clip к сравнению
# доживают только фрагменты с покрытием выше половины, и вид «Силуэт клетки»
# заливает кадр одним цветом независимо от того, работает силуэт или нет.
debug_at = terrain_shader.index('KernTerrainDebugActive()')
clip_at = terrain_shader.index('clip(cellCoverage - 0.5)')
if debug_at > clip_at:
    raise RuntimeError('The terrain debug view must be returned before clip()')

fixture = Path(__file__).parent
scenario = (fixture / 'scenario.cpp').read_text()
ao_shim = (fixture / 'ao-pyramid.cpp').read_text()
ao = (root / 'Assets/Shaders/TerrainAmbientOcclusion.hlsl').read_text()
lighting = (root / 'Assets/Shaders/TerrainLightingData.hlsl').read_text()
with tempfile.TemporaryDirectory(prefix='kern-terrain-raster-') as directory:
    cpp = Path(directory) / 'test.cpp'
    executable = Path(directory) / 'test'
    for mutation in (None, "polygon-carrier", "ao-wide-mip", "ao-to-black",
                     "rim-sector-split", "rim-carrier-clamped"):
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
        if mutation == "ao-to-black":
            # Reproduce the band along every mass: contact occlusion taking the
            # floor all the way to black instead of stopping at its floor.
            candidate_ao = candidate_ao.replace(
                '1.0 - (occlusion * (1.0 - _TerrainAmbientOcclusionFloor))',
                '1.0 - occlusion')
            if candidate_ao == ao:
                raise RuntimeError('AO floor mutation no longer matches the production source')
        candidate_rim = rim
        if mutation == "rim-sector-split":
            # Reproduce the rim that does not tile: cut the bottom edge's
            # falloff by the cell diagonal, as an exclusive sector would, so
            # the band tapers away at every cell boundary.
            candidate_rim = candidate_rim.replace(
                'rim *= TerrainReliefRimSide(cellLocal.y);',
                'rim *= cellLocal.y < min(cellLocal.x, 1.0 - cellLocal.x) '
                '? TerrainReliefRimSide(cellLocal.y) : 1.0;')
            if candidate_rim == rim:
                raise RuntimeError('Rim sector mutation no longer matches the production source')
        if mutation == "rim-carrier-clamped":
            # Reproduce the over-dark protruding edge: clamp the carrier
            # coordinate into the unit square instead of normalising it by the
            # polygon bounds, which parks the whole overhang on the floor.
            candidate_rim = candidate_rim.replace(
                '(QuantizeTerrainFaceUV(cellSample) - boundsMin) / span);',
                'QuantizeTerrainFaceUV(cellSample));')
            if candidate_rim == rim:
                raise RuntimeError('Rim normalisation mutation no longer matches the production source')
        candidate_ao = candidate_ao.replace('Texture2D<float4>', 'AoTexture').replace('SamplerState', 'int')
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
