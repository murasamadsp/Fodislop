#!/usr/bin/env python3
"""Bit-exact comparison of two versions of WorldLighting.compute.

Usage: equivalence.py REFERENCE.compute [CANDIDATE.compute]
CANDIDATE defaults to the working-tree shader. Both run through the float32
shim from transport.py on identical random scenes; every static, dynamic,
bounce and filtered output must match bit for bit. Use it to prove that an
optimization changes cost only, never the image. Geometry caches are built
for a side only if its shader has them. Requires clang++ and Python 3."""
from pathlib import Path
import re, subprocess, sys, tempfile

root = Path(__file__).resolve().parents[2]
transport = (root / 'tools/lighting-tests/transport.py').read_text()
shim = re.search(r"shim = r'''(.*?)'''", transport, re.S).group(1)
names = ['SegmentExtinction', 'SegmentTransmission', 'Max3', 'OutputUv', 'MaterialUv',
         'SampleOccupancy', 'PathLengthInCells', 'SampleCellSolid', 'CheckCellSolid', 'BuildCellSolidMask',
         'CheckDiagonalStepOccluded', 'AbsorbedFraction', 'CellEmissionWeight', 'TraceLightSegment',
         'TraceRadianceSegment', 'GatherDynamicSource', 'SolveDynamicLighting',
         'PackRadiance', 'UnpackRadiance', 'PackInterval', 'UnpackTransmittance', 'SolveCascade',
         'InterleavedGradientNoise', 'BuildBounceTaps', 'SolveDiffuseBounce', 'BuildBounceFilter',
         'SampleBounceFiltered']

def program(shader_text):
    functions = []
    for name in names:
        match = re.search(r'^(?:float[234]?|uint[23]|void) ' + name + r'\(', shader_text, re.M)
        if not match:
            continue
        begin = shader_text.index('{', match.start())
        depth, end = 1, begin + 1
        while depth:
            depth += (shader_text[end] == '{') - (shader_text[end] == '}')
            end += 1
        functions.append(shader_text[match.start():end])
    code = '\n'.join(functions)
    code = re.sub(r'\bout (float[234]?|bool) (\w+)', r'\1& \2', code)
    code = re.sub(r'\[(?:loop|unroll)\]', '', code)
    code = code.replace(' : SV_DispatchThreadID', '')
    for size in ('_BounceSize', '_FieldSize', '_CellGridSize'):
        code = code.replace('(uint2)' + size, f'__builtin_convertvector({size}, uint2)')
    code = re.sub(r'\b(float[234]|int[23]|uint[23])\(', r'make_\1(', code)
    return code

scenario = r'''
#include <cstdio>
struct Cascade {int offset,w,h,spacing,dirs;float start,end;};
void solveField(int count) {
    std::vector<Cascade> levels; int offset=0,spacing=1,dirs=4; float start=0,end=1;
    for(int i=0;i<count;i++) {
        int w=(_FieldSize.x+spacing-1)/spacing,h=(_FieldSize.y+spacing-1)/spacing;
        levels.push_back({offset,w,h,spacing,dirs,start,i==count-1?length(make_float2(_FieldSize)):end});
        offset+=w*h*dirs; spacing*=2; dirs*=4; start=end; end*=4;
    }
    _RadianceAtlas.assign(offset,uint3{});
    for(int i=count-1;i>=0;i--) {
        auto c=levels[i],f=levels[std::min(i+1,count-1)];
        _CascadeOffset=c.offset; _CascadeProbeSize={c.w,c.h}; _CascadeProbeSpacing=c.spacing;
        _CascadeDirectionCount=c.dirs; _CascadeInterval={c.start,c.end}; _CascadeEntryCount=c.w*c.h*c.dirs;
        _FarCascadeOffset=f.offset; _FarCascadeProbeSize={f.w,f.h}; _FarCascadeProbeSpacing=f.spacing;
        _FarCascadeDirectionCount=f.dirs; _FarCascadeInterval={f.start,f.end}; _HasFarCascade=i<count-1;
        _CascadeDispatchRowWidth=_CascadeEntryCount;
        for(int n=0;n<_CascadeEntryCount;n++) SolveCascade(uint3{(uint)n,0,0});
    }
}
void emit(const char* label,int index,float3 v) { std::printf("%s %d %a %a %a\n",label,index,v.x,v.y,v.z); }
int main() {
    std::mt19937 rng(424242);
    std::uniform_real_distribution<float> unit(0.f,1.f);
    long dynamicReads=0,bounceReads=0,filterReads=0,cacheReads=0;
    for(int scene=0;scene<8;scene++) {
        int scale=(scene%2)?4:2, w=20, h=12;
        _FieldSize={w*scale,h*scale}; _WorldRect={0,0,(float)w,(float)h}; _CellSize=1;
        _MaterialField.reset(w*scale,h*scale); _EmissionField.reset(w*scale,h*scale);
        _MaterialYFlip=(scene%3)==2;
        _EmptyExtinctionRGB={.2f,.1f,.4f,0};
        _SolidExtinctionRGB=scene<4?float4{4,8,16,0}:float4{1600,1600,1600,0};
        for(int cy=0;cy<h;cy++)for(int cx=0;cx<w;cx++) {
            bool solid=unit(rng)<.35f;
            float3 albedo={unit(rng),unit(rng),unit(rng)};
            for(int y=0;y<scale;y++)for(int x=0;x<scale;x++) {
                bool edge=x==0||y==0||x==scale-1||y==scale-1;
                float a=solid?((edge&&unit(rng)<.3f)?.5f:1.f):0.f;
                _MaterialField.data[(cy*scale+y)*_FieldSize.x+cx*scale+x]={albedo.x,albedo.y,albedo.z,a};
            }
        }
        for(int i=0;i<6;i++) {
            int x=rng()%_FieldSize.x,y=rng()%_FieldSize.y; float v=1+15*unit(rng);
            _EmissionField.data[y*_FieldSize.x+x]={v,v*.7f,v*.4f,0};
        }
#ifdef CACHED
        _CellGridSize={w,h}; _CellSolidMaskOutput.reset(w,h);
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)BuildCellSolidMask(uint3{(uint)x,(uint)y,0});
        _CellSolidMask=_CellSolidMaskOutput;
#endif
        solveField(4);
        int pixels=_FieldSize.x*_FieldSize.y;
        _StaticDirectInput.reset(_FieldSize.x,_FieldSize.y);
        for(int p=0;p<pixels;p++) {
            float3 r=0.0; for(int d=0;d<4;d++) r+=UnpackRadiance(_RadianceAtlas[p*4+d].xy);
            float3 direct=r*.25f; _StaticDirectInput.data[p]={direct.x,direct.y,direct.z,1};
            emit("static",p,direct);
        }
        _DynamicLights.clear();
        for(int i=0;i<3;i++) _DynamicLights.push_back({{unit(rng)*w,unit(rng)*h,0,0},{unit(rng),unit(rng),unit(rng),1+3*unit(rng)}});
        _DynamicLightCount=3; _DirectTexture.reset(_FieldSize.x,_FieldSize.y);
        textureReads=0;
        for(int y=0;y<_FieldSize.y;y++)for(int x=0;x<_FieldSize.x;x++)SolveDynamicLighting(uint3{(uint)x,(uint)y,0});
        dynamicReads+=textureReads;
        for(int p=0;p<pixels;p++) emit("dynamic",p,_DirectTexture.data[p].xyz);
        _DirectInput=_DirectTexture;
        _BounceSize={(_FieldSize.x+1)/2,(_FieldSize.y+1)/2}; _BounceTexture.reset(_BounceSize.x,_BounceSize.y);
#ifdef CACHED
        textureReads=0;
        _BounceTaps.assign(_BounceSize.x*_BounceSize.y*16,float4{});
        for(int y=0;y<_BounceSize.y;y++)for(int x=0;x<_BounceSize.x;x++)BuildBounceTaps(uint3{(uint)x,(uint)y,0});
        _BounceFilterWeights.assign(pixels*4,float4{});
        for(int y=0;y<_FieldSize.y;y++)for(int x=0;x<_FieldSize.x;x++)BuildBounceFilter(uint3{(uint)x,(uint)y,0});
        cacheReads+=textureReads;
#endif
        textureReads=0;
        for(int y=0;y<_BounceSize.y;y++)for(int x=0;x<_BounceSize.x;x++)SolveDiffuseBounce(uint3{(uint)x,(uint)y,0});
        bounceReads+=textureReads;
        for(int p=0;p<_BounceSize.x*_BounceSize.y;p++) emit("bounce",p,_BounceTexture.data[p].xyz);
        _BounceInput=_BounceTexture;
        textureReads=0;
        for(int y=0;y<_FieldSize.y;y++)for(int x=0;x<_FieldSize.x;x++) {
            float2 uv=(make_float2(make_int2(x,y))+.5f)/make_float2(_FieldSize);
#ifdef CACHED
            emit("filtered",y*_FieldSize.x+x,SampleBounceFiltered(make_int2(x,y),uv));
#else
            emit("filtered",y*_FieldSize.x+x,SampleBounceFiltered(uv));
#endif
        }
        filterReads+=textureReads;
    }
    std::fprintf(stderr,"reads: dynamic=%ld bounce=%ld filter=%ld cacheBuild=%ld\n",dynamicReads,bounceReads,filterReads,cacheReads);
}
'''

def run(shader_path, name, directory):
    text = Path(shader_path).read_text()
    cached = re.search(r'^void BuildCellSolidMask\(', text, re.M) is not None
    cpp = Path(directory) / (name + '.cpp')
    exe = cpp.with_suffix('')
    cpp.write_text(('#define CACHED\n' if cached else '') + shim + program(text) + scenario)
    subprocess.run(['clang++', '-std=c++20', '-O1', '-ffp-contract=off', str(cpp), '-o', str(exe)], check=True)
    result = subprocess.run([str(exe)], check=True, capture_output=True, text=True, timeout=600)
    return result.stdout.splitlines(), result.stderr.strip()

with tempfile.TemporaryDirectory(prefix='fodinae-compare-') as directory:
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    candidate = sys.argv[2] if len(sys.argv) > 2 else root / 'Assets/Resources/Shaders/Lighting/WorldLighting.compute'
    before, before_reads = run(sys.argv[1], 'reference', directory)
    after, after_reads = run(candidate, 'candidate', directory)

print('reference', before_reads)
print('candidate', after_reads)
assert len(before) == len(after), (len(before), len(after))
mismatch = {}
for b, a in zip(before, after):
    if b != a:
        label = b.split()[0]
        mismatch.setdefault(label, []).append((b, a))
labels = sorted({line.split()[0] for line in before})
for label in labels:
    total = sum(1 for line in before if line.startswith(label + ' '))
    bad = mismatch.get(label, [])
    print(f'{label}: {total * 3} values, bit-identical={total * 3 - 3 * len(bad) if not bad else "NO"} mismatched texels={len(bad)}')
    for b, a in bad[:3]:
        print('   before', b)
        print('   after ', a)
sys.exit(1 if mismatch else 0)
