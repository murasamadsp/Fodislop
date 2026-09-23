// CPU model of the texture's generated mip pyramid and trilinear SampleLevel.
// Only sampling is emulated here; AO response comes from production HLSL.
struct AoTexture
{
    std::vector<Texture> levels;
    void generate(Texture base)
    {
        levels.clear();
        levels.push_back(std::move(base));
        while(levels.back().width > 1)
        {
            const Texture& previous=levels.back();
            Texture next;
            next.reset(previous.width/2,previous.height/2);
            for(int y=0;y<next.height;++y) for(int x=0;x<next.width;++x)
            {
                next.data[y*next.width+x]=(previous.Load(int3{2*x,2*y,0})+
                    previous.Load(int3{2*x+1,2*y,0})+
                    previous.Load(int3{2*x,2*y+1,0})+
                    previous.Load(int3{2*x+1,2*y+1,0}))*.25f;
            }
            levels.push_back(std::move(next));
        }
    }
    float4 SampleLevel(int sampler, float2 uv, float mip) const
    {
        mip=std::clamp(mip,0.f,float(levels.size()-1));
        int low=int(std::floor(mip)),high=std::min(low+1,int(levels.size()-1));
        float fraction=mip-low;
        return levels[low].SampleLevel(sampler,uv,0)*(1-fraction)+
            levels[high].SampleLevel(sampler,uv,0)*fraction;
    }
};
