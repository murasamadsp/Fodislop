#!/usr/bin/env python3
"""Run the actual HLSL transport functions as float32 C++ with texture fixtures.
No Unity, Editor assemblies, import, or GPU state. Requires clang++ and Python 3.
The shim supplies HLSL vectors/textures; it does not reimplement the solver.
"""
from pathlib import Path
import re
import subprocess
import tempfile

root = Path(__file__).resolve().parents[2]
shader = (root / 'Assets/Resources/Shaders/Lighting/WorldLighting.compute').read_text()
names = ['SegmentExtinction', 'SegmentTransmission', 'Max3', 'OutputUv', 'MaterialUv',
         'SampleOccupancy', 'PathLengthInCells', 'SampleCellSolid', 'CheckCellSolid', 'BuildCellSolidMask',
         'CheckDiagonalStepOccluded',
         'AbsorbedFraction', 'CellEmissionWeight', 'TraceLightSegment', 'TraceRadianceSegment',
         'GatherDynamicSource', 'SolveDynamicLighting',
         'PackRadiance', 'UnpackRadiance', 'PackInterval', 'UnpackTransmittance', 'SolveCascade',
         'InterleavedGradientNoise', 'BuildBounceTaps', 'SolveDiffuseBounce', 'BuildBounceFilter',
         'SampleBounceFiltered', 'SurfaceReflection',
         'SeedBlockLighting', 'PropagateBlockLighting', 'ResolveBlockLighting']
functions = []
for name in names:
    match = re.search(r'^(?:float[234]?|uint[23]|void) ' + name + r'\(', shader, re.M)
    assert match, name
    begin = shader.index('{', match.start())
    depth, end = 1, begin + 1
    while depth:
        depth += (shader[end] == '{') - (shader[end] == '}')
        end += 1
    functions.append(shader[match.start():end])
code = '\n'.join(functions)
code = re.sub(r'\bout (float[234]?|bool) (\w+)', r'\1& \2', code)
code = re.sub(r'\[(?:loop|unroll)\]', '', code)
code = code.replace(' : SV_DispatchThreadID', '').replace('(uint2)_BounceSize', '__builtin_convertvector(_BounceSize, uint2)').replace('(uint2)_FieldSize', '__builtin_convertvector(_FieldSize, uint2)').replace('(uint2)_CellGridSize', '__builtin_convertvector(_CellGridSize, uint2)')
code = re.sub(r'\b(float[234]|int[23]|uint[23])\(', r'make_\1(', code)

shim = r'''
#include <algorithm>
#include <cmath>
#include <cstring>
#include <iostream>
#include <random>
#include <stdexcept>
#include <vector>
using float2 = float __attribute__((ext_vector_type(2)));
using float3 = float __attribute__((ext_vector_type(3)));
using float4 = float __attribute__((ext_vector_type(4)));
using int2 = int __attribute__((ext_vector_type(2)));
using int3 = int __attribute__((ext_vector_type(3)));
using uint = unsigned;
using uint2 = unsigned __attribute__((ext_vector_type(2)));
using uint3 = unsigned __attribute__((ext_vector_type(3)));
uint2 make_uint2(uint a,uint b) { return {a,b}; }
uint3 make_uint3(uint a,uint b,uint c) { return {a,b,c}; }
uint f32tof16(float f) { _Float16 h=f; unsigned short bits; std::memcpy(&bits,&h,2); return bits; }
float f16tof32(uint u) { unsigned short bits=u; _Float16 h; std::memcpy(&h,&bits,2); return h; }
void sincos(float a,float& s,float& c) { s=std::sin(a); c=std::cos(a); }
constexpr float PI=3.14159265359f;
constexpr float PI2=6.28318530718f;
std::vector<uint3> _RadianceAtlas;
int _CascadeOffset,_CascadeProbeSpacing,_CascadeDirectionCount,_FarCascadeOffset;
int _FarCascadeProbeSpacing,_FarCascadeDirectionCount,_HasFarCascade,_CascadeEntryCount,_CascadeDispatchRowWidth;
int2 _CascadeProbeSize,_FarCascadeProbeSize;
float2 _CascadeInterval,_FarCascadeInterval;
float2 make_float2(float a, float b) { return {a,b}; }
float2 make_float2(float a) { return {a,a}; }
float2 make_float2(int2 a) { return __builtin_convertvector(a, float2); }
int2 make_int2(int a, int b) { return {a,b}; }
int2 make_int2(float2 a) { return __builtin_convertvector(a, int2); }
int2 make_int2(uint2 a) { return __builtin_convertvector(a, int2); }
int3 make_int3(int2 a, int b) { return {a.x,a.y,b}; }
float3 make_float3(float a, float b, float c) { return {a,b,c}; }
float4 make_float4(float2 a,float2 b) { return {a.x,a.y,b.x,b.y}; }
float4 make_float4(float3 a,float b) { return {a.x,a.y,a.z,b}; }
float4 make_float4(float a,float b,float c,float d) { return {a,b,c,d}; }
float min(float a,float b) { return std::min(a,b); }
float max(float a,float b) { return std::max(a,b); }
float2 min(float2 a,float2 b) { return {min(a.x,b.x),min(a.y,b.y)}; }
float2 max(float2 a,float2 b) { return {max(a.x,b.x),max(a.y,b.y)}; }
float3 max(float3 a,float3 b) { return {max(a.x,b.x),max(a.y,b.y),max(a.z,b.z)}; }
float3 min(float3 a,float b) { return {min(a.x,b),min(a.y,b),min(a.z,b)}; }
float3 max(float3 a,float b) { return {max(a.x,b),max(a.y,b),max(a.z,b)}; }
float2 abs(float2 a) { return {abs(a.x),abs(a.y)}; }
float2 floor(float2 a) { return {floor(a.x),floor(a.y)}; }
float2 ceil(float2 a) { return {std::ceil(a.x),std::ceil(a.y)}; }
float2 frac(float2 a) { return a-floor(a); }
float frac(float a) { return a-floor(a); }
float3 exp(float3 a) { return {exp(a.x),exp(a.y),exp(a.z)}; }
float dot(float2 a,float2 b) { return a.x*b.x+a.y*b.y; }
float length(float2 a) { return std::sqrt(a.x*a.x+a.y*a.y); }
float2 sign(float2 a) { return {(float)((a.x>0)-(a.x<0)),(float)((a.y>0)-(a.y<0))}; }
int clamp(int a,int lo,int hi) { return std::clamp(a,lo,hi); }
int2 clamp(int2 a,int2 lo,int2 hi) { return {clamp(a.x,lo.x,hi.x),clamp(a.y,lo.y,hi.y)}; }
float step(float edge,float x) { return x>=edge ? 1.f : 0.f; }
float saturate(float a) { return std::clamp(a,0.f,1.f); }
float3 saturate(float3 a) { return {saturate(a.x),saturate(a.y),saturate(a.z)}; }
float2 saturate(float2 a) { return {saturate(a.x),saturate(a.y)}; }
float3 lerp(float3 a,float3 b,float t) { return a+(b-a)*t; }
bool all(int2 a) { return a.x && a.y; }
bool any(int2 a) { return a.x || a.y; }
bool any(uint2 a) { return a.x || a.y; }
long textureReads=0;
struct Texture {
    int width=1,height=1;
    std::vector<float4> data;
    void reset(int w,int h) { width=w; height=h; data.assign(w*h,float4{}); }
    float4& operator[](int2 p) { return data.at(p.y*width+p.x); }
    float4 Load(int3 p) const {
        ++textureReads;
        if(p.x<0 || p.y<0 || p.x>=width || p.y>=height)
            throw std::runtime_error("out-of-bounds texture read "+std::to_string(p.x)+","+std::to_string(p.y));
        return data[p.y*width+p.x];
    }
    float4 SampleLevel(int,float2 uv,float) const {
        float2 p=uv*float2{(float)width,(float)height}-.5f;
        int2 b=make_int2(floor(p)); float2 f=p-floor(p);
        auto at=[&](int x,int y){return Load({clamp(x,0,width-1),clamp(y,0,height-1),0});};
        return (at(b.x,b.y)*(1-f.x)+at(b.x+1,b.y)*f.x)*(1-f.y)
             +(at(b.x,b.y+1)*(1-f.x)+at(b.x+1,b.y+1)*f.x)*f.y;
    }
};
Texture _MaterialField, _EmissionField, _DirectInput, _StaticDirectInput, _BounceInput, _BounceTexture;
int2 _BounceSize;
Texture _BlockLightInput,_BlockLightOutput,_Result,_DirectTexture;
struct DynamicLight { float4 positionRadius,colorIntensity; };
std::vector<DynamicLight> _DynamicLights;
int _DynamicLightCount=0;
float4 _AmbientColor={.25f,.25f,.25f,0};
int _DebugView=0,_EnableDiffuseBounce=1;
float _BounceStrength=1;
int sampler_LinearClamp=0, _MaterialYFlip=0;
int2 _FieldSize;
float4 _WorldRect, _EmptyExtinctionRGB, _SolidExtinctionRGB;
float _CellSize=1, _EmissionScale=1;
static const float InvisibleLampRadiance = 1e-6f;
Texture _CellSolidMask,_CellSolidMaskOutput;
int2 _CellGridSize;
std::vector<float4> _BounceTaps,_BounceFilterWeights;
'''
tests = r'''
int checks=0;
void near(float got,float expected,float tolerance,const char* label) {
    ++checks;
    if(!std::isfinite(got) || std::abs(got-expected)>tolerance)
        throw std::runtime_error(std::string(label)+": got "+std::to_string(got)+", expected "+std::to_string(expected));
}
void setup(int w,int h,int scale=1) {
    _FieldSize={w*scale,h*scale}; _WorldRect={0,0,(float)w,(float)h};
    _MaterialField.reset(w*scale,h*scale); _EmissionField.reset(w*scale,h*scale);
    _MaterialYFlip=0; _EmptyExtinctionRGB={.2f,.1f,.4f,0}; _SolidExtinctionRGB={4.f,8.f,16.f,0};
}
// Rebuilds the per-cell solid mask from the current material fixture, as the
// engine does whenever the material field is redrawn.
void buildMask() {
    _CellGridSize={(int)(_WorldRect.z/_CellSize),(int)(_WorldRect.w/_CellSize)};
    _CellSolidMaskOutput.reset(_CellGridSize.x,_CellGridSize.y);
    for(int y=0;y<_CellGridSize.y;y++)for(int x=0;x<_CellGridSize.x;x++)BuildCellSolidMask(uint3{(uint)x,(uint)y,0});
    _CellSolidMask=_CellSolidMaskOutput;
}
float3 trace(float2 a,float2 b,float3* radiance=nullptr) {
    buildMask();
    float3 r,t;
    try { TraceRadianceSegment(a,b,true,r,t); }
    catch(...) { std::cerr<<"ray "<<a.x<<","<<a.y<<" -> "<<b.x<<","<<b.y<<"\n"; throw; }
    if(radiance)*radiance=r; return t;
}
struct Cascade {int offset,w,h,spacing,dirs;float start,end;};
void solveField(int count=3) {
    buildMask();
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
float direct(int x,int y) {
    float value=0;for(int d=0;d<4;d++)value+=UnpackRadiance(_RadianceAtlas[(y*_FieldSize.x+x)*4+d].xy).x*.25f;
    return value;
}
int main() {
    try {
        for(int scale: {1,2,3,4}) {
            setup(32,8,scale);
            float2 a=float2{2.5f,3.5f}*scale,b=float2{28.5f,3.5f}*scale;
            auto t=trace(a,b);
            near(t.x,std::exp(-.2f*26),2e-6f,"empty distance / resolution");
            near(t.y,std::exp(-.1f*26),2e-6f,"RGB independence");
            for(int y=0;y<_FieldSize.y;y++)for(int x=12*scale;x<13*scale;x++)
                _MaterialField.data[y*_FieldSize.x+x].w=1;
            t=trace(a,b);
            near(t.x,std::exp(-.2f*25-4),2e-6f,"one-cell wall");
            auto reverse=trace(b,a); near(t.x,reverse.x,2e-6f,"reciprocity");
            _SolidExtinctionRGB={1600,1600,1600,0};
            t=trace(a,b); near(t.x,0,1e-30f,"solid=1600 wall blocks");
            _EmissionField.data[(3*scale)*_FieldSize.x+20*scale]={16,16,16,0};
            float3 light; trace(a,b,&light); near(light.x,0,1e-30f,"source behind wall cannot bypass absorption");
        }
        setup(32,8); _EmptyExtinctionRGB={0,0,0,0};
        near(trace(float2{0,4},float2{32,4}).x,1,0,"zero absorption");
        for(float sigma: {0.f,1e-8f,1e-4f,.2f,4.f,1600.f}) {
            setup(8,8); _EmptyExtinctionRGB={sigma,sigma,sigma,0};
            for(auto& e:_EmissionField.data)e={1,1,1,0};
            float3 whole,left,right;
            auto tw=trace(float2{2,4},float2{3,4},&whole);
            auto tl=trace(float2{2,4},float2{2.37f,4},&left);
            auto tr=trace(float2{2.37f,4},float2{3,4},&right);
            near(whole.x,1,2e-5f,"one-cell source normalization");
            near(whole.x,left.x+tl.x*right.x,3e-5f,"emission split invariance");
            near(tw.x,tl.x*tr.x,2e-6f,"transmission split invariance");
        }
        setup(8,8); _MaterialField.data[2*8+3].w=1; _SolidExtinctionRGB={20,20,20,0};
        auto t=trace(float2{1.5f,2.5f},float2{6.5f,2.5f});
        _MaterialYFlip=1;
        auto flipped=trace(float2{1.5f,5.5f},float2{6.5f,5.5f});
        near(t.x,flipped.x,1e-9f,"material Y flip");
        // Independent line/box intersection oracle for non-axis-aligned rays.
        std::mt19937 random(7103); std::uniform_real_distribution<float> pos(.01f,31.99f);
        for(int i=0;i<3000;i++) {
            setup(32,32); _SolidExtinctionRGB={2,2,2,0};
            for(int y=0;y<32;y++)_MaterialField.data[y*32+16].w=1;
            float2 a={pos(random),pos(random)},b={pos(random),pos(random)};
            float d=length(b-a), solidLength=0;
            if(std::abs(b.x-a.x)>1e-8f) {
                float t0=(16-a.x)/(b.x-a.x),t1=(17-a.x)/(b.x-a.x);
                solidLength=max(0.f,min(1.f,max(t0,t1))-max(0.f,min(t0,t1)))*d;
            } else if(a.x>=16&&a.x<17) solidLength=d;
            near(trace(a,b).x,std::exp(-.2f*(d-solidLength)-2*solidLength),3e-5f,"random wall intersection");
        }
        // Clipped rays, exact grid boundaries, and near-axis directions must terminate safely.
        setup(8,8);
        for(float2 a: {float2{-3,4},float2{0,4},float2{8,4},float2{4,0},float2{4,8},float2{-2,-2}})
        for(float2 b: {float2{10,4},float2{0,4},float2{8,4},float2{4,0},float2{4,8},float2{10,10}})
            near(trace(a,b).x,std::exp(-.2f*length(b-a)),2e-6f,"clipped ray");
        // Entire production cascade merge and half-float atlas, not just exp().
        setup(24,24); _EmptyExtinctionRGB={.2f,.2f,.2f,0};
        _EmissionField.data[12*24+18]={16,16,16,0};
        solveField(); float open=direct(8,12);
        if(open<=0)throw std::runtime_error("cascade source missing in open air");
        for(int y=0;y<24;y++)_MaterialField.data[y*24+12].w=1;
        _SolidExtinctionRGB={1600,1600,1600,0}; solveField();
        float blocked=direct(8,12);
        _DirectInput.reset(24,24); _StaticDirectInput.reset(24,24);
        for(int y=0;y<24;y++)for(int x=0;x<24;x++) {
            float light=direct(x,y);_StaticDirectInput.data[y*24+x]={light,light,light,0};
        }
        _BounceSize={12,12}; _BounceTexture.reset(12,12);
        _BounceTaps.assign(12*12*16,float4{});
        for(int y=0;y<12;y++)for(int x=0;x<12;x++) BuildBounceTaps(uint3{(uint)x,(uint)y,0});
        for(int y=0;y<12;y++)for(int x=0;x<12;x++) SolveDiffuseBounce(uint3{(uint)x,(uint)y,0});
        _BounceInput=_BounceTexture;
        _BounceFilterWeights.assign(24*24*4,float4{});
        for(int y=0;y<24;y++)for(int x=0;x<24;x++) BuildBounceFilter(uint3{(uint)x,(uint)y,0});
        near(SampleBounceFiltered(int2{8,12},float2{8.5f/24,12.5f/24}).x,0,1e-6f,"bounce does not cross the wall");
        near(blocked,0,1e-6f,"full cascade wall occlusion");
        std::cout<<"Cascade fixture: open="<<open<<", solid1600="<<blocked<<"\n";
        _SolidExtinctionRGB={0,0,0,0}; solveField();
        if(direct(8,12)<open*.9f)throw std::runtime_error("solid coefficient has no effect");
        ++checks;
        float previous=direct(8,12);
        for(float solid: {.5f,2.f,8.f,1600.f}) {
            _SolidExtinctionRGB={solid,solid,solid,0};solveField();float value=direct(8,12);
            if(value>previous+1e-6f)throw std::runtime_error("solid sweep is not monotonic");
            previous=value;++checks;
        }
        for(auto& m:_MaterialField.data)m.w=0;
        previous=100;
        for(float empty: {.05f,.2f,.5f,1.f}) {
            _EmptyExtinctionRGB={empty,empty,empty,0};solveField();float value=direct(8,12);
            if(value<=0 || value>previous+1e-6f)throw std::runtime_error("empty sweep has a cutoff or is not monotonic");
            previous=value;++checks;
        }
        // Full four-cascade PerPixel fixture at four texels per world cell.
        setup(16,16,4);_SolidExtinctionRGB={1600,1600,1600,0};
        for(int y=32;y<36;y++)for(int x=48;x<52;x++)_EmissionField.data[y*64+x]={16,16,16,0};
        solveField(4);float pixelOpen=direct(18,34);
        if(pixelOpen<=0)throw std::runtime_error("four-cascade source missing");
        for(int y=0;y<64;y++)for(int x=32;x<36;x++)_MaterialField.data[y*64+x].w=1;
        solveField(4);near(direct(18,34),0,1e-6f,"four-cascade wall at four texels per cell");
        for(auto& m:_MaterialField.data)m.w=0;
        for(int y=32;y<36;y++)for(int x=48;x<52;x++)_MaterialField.data[y*64+x].w=1;
        solveField(4);
        if(direct(18,34)<pixelOpen*.25f)throw std::runtime_error("glowing solid suppressed by own extinction");
        ++checks;
        setup(16,16,4);_SolidExtinctionRGB={1600,1600,1600,0};
        DynamicLight lamp={{12.5f,8.5f,0,0},{16,16,16,1}};
        for(int y=32;y<36;y++)for(int x=48;x<52;x++)_EmissionField.data[y*64+x]={16,16,16,0};
        textureReads=0;solveField(4);long cascadeReads=textureReads;
        _DynamicLights={lamp};_DynamicLightCount=1;_DirectTexture.reset(64,64);
        textureReads=0;
        for(int y=0;y<64;y++)for(int x=0;x<64;x++)SolveDynamicLighting(uint3{(uint)x,(uint)y,0});
        long targetedReads=textureReads;
        std::cout<<"Moving lamp: cascade reads="<<cascadeReads<<", targeted reads="<<targetedReads
                 <<", reduction="<<(double)cascadeReads/targetedReads<<"x\n";
        if(targetedReads*5>=cascadeReads)throw std::runtime_error("targeted lighting did not remove cascade work");
        ++checks;
        for(float2 receiver: {float2{18.5f,34.5f},float2{18.5f,18.5f},float2{50.5f,18.5f}}) {
            float fast=GatherDynamicSource(receiver,lamp,8).x;
            float reference=GatherDynamicSource(receiver,lamp,1024).x;
            near(fast,reference,reference*.06f,"targeted quadrature against dense angular integration");
        }
        for(int y=0;y<64;y++)for(int x=32;x<36;x++)_MaterialField.data[y*64+x].w=1;
        buildMask();
        near(GatherDynamicSource(float2{18.5f,34.5f},lamp,8).x,0,1e-30f,"targeted lamp blocked by wall");
        for(auto& m:_MaterialField.data)m.w=0;
        buildMask();
        _DynamicLights={lamp,lamp};_DynamicLightCount=2;
        SolveDynamicLighting(uint3{18,34,0});
        near(_DirectTexture.Load(int3{18,34,0}).x,2*GatherDynamicSource(float2{18.5f,34.5f},lamp,8).x,1e-6f,"two lamps add without counting the other lamp along the ray");
        _DynamicLights.clear();_DynamicLightCount=0;
        SolveDynamicLighting(uint3{18,34,0});near(_DirectTexture.Load(int3{18,34,0}).x,0,0,"removed lamp clears output");
        setup(2,2);_SolidExtinctionRGB={1600,1600,1600,0};
        _MaterialField.data[1].w=1;_MaterialField.data[2].w=1;
        _EmissionField.data[3]={16,16,16,0};
        float3 cornerLight;trace(float2{.5f,.5f},float2{1.5f,1.5f},&cornerLight);
        near(cornerLight.x,0,1e-30f,"emitter behind closed diagonal corner");
        setup(8,3);_SolidExtinctionRGB={1600,1600,1600,0};
        _EmissionField.data[1*8+1]={1,1,1,0};
        for(int y=0;y<3;y++)_MaterialField.data[y*8+3].w=1;
        _BlockLightOutput.reset(8,3);
        for(int y=0;y<3;y++)for(int x=0;x<8;x++)SeedBlockLighting(uint3{(uint)x,(uint)y,0});
        for(int i=0;i<8;i++) {
            _BlockLightInput=_BlockLightOutput;
            for(int y=0;y<3;y++)for(int x=0;x<8;x++)PropagateBlockLighting(uint3{(uint)x,(uint)y,0});
        }
        near(_BlockLightOutput.Load(int3{3,1,0}).x,std::exp(-.4f),1e-6f,"block wall face receives light");
        near(_BlockLightOutput.Load(int3{4,1,0}).x,0,1e-30f,"block wall does not relay face illumination");
        _BlockLightInput=_BlockLightOutput;_Result.reset(8,3);
        ResolveBlockLighting(uint3{4,1,0});
        near(_Result.Load(int3{4,1,0}).x,.25f,1e-6f,"ambient added exactly once");
        // Surface reflection follows the light along the face texel by texel,
        // never one flat value per block, and still reaches only one cell deep.
        setup(8,4,4); _EmptyExtinctionRGB={.2f,.2f,.2f,0};
        for(int y=0;y<16;y++)for(int x=12;x<24;x++)_MaterialField.data[y*32+x].w=1;
        _DirectInput.reset(32,16); _StaticDirectInput.reset(32,16);
        for(int y=0;y<16;y++)for(int x=0;x<12;x++)_StaticDirectInput.data[y*32+x]={(float)y,(float)y,(float)y,0};
        float faceTransmission=std::exp(-.2f*.5f);
        for(int y: {1,6,13}) {
            near(SurfaceReflection(float2{12.5f,y+.5f},float3{1,1,1}).x,y*faceTransmission,1e-5f,"surface light follows the face per texel");
            near(SurfaceReflection(float2{15.5f,y+.5f},float3{1,1,1}).x,y*faceTransmission,1e-5f,"whole first cell reads its own row at the face");
            near(SurfaceReflection(float2{16.5f,y+.5f},float3{1,1,1}).x,0,0,"surface light stays one cell deep");
        }
        std::cout<<checks<<" transport checks passed (actual HLSL functions, float32).\n";
    } catch(const std::exception& e) {std::cerr<<e.what()<<"\n";return 1;}
}
'''
with tempfile.TemporaryDirectory(prefix='fodinae-lighting-') as directory:
    cpp=Path(directory)/'transport.cpp'
    exe=Path(directory)/'transport'
    cpp.write_text(shim+'\n'+code+'\n'+tests)
    subprocess.run(['clang++','-std=c++20','-O2',str(cpp),'-o',str(exe)],check=True)
    subprocess.run([str(exe)],check=True,timeout=60)
