float2 lava(float2 cell, float2 local, float time, float anchored, float descriptor) {
    auto tile=ResolveTerrainTileUV(
        make_float2(.7f,.3f), make_float4(.25f,.125f,.15625f,.125f),
        make_float4(.015625f,.015625f,1,8), make_float4(cell.x,cell.y,descriptor,1),
        make_float4(4,10,0,2), make_float4(anchored,local.x,local.y,0), time,
        make_float2(1.f/2048));
    return ClampTerrainTileUV(tile.finalUV,tile);
}
int main() {
    int checks=0;
    for(int x : {-32,-1,0,9,31,32})
    for(int y : {-32,-1,0,7,31,32})
    for(float time : {0.f,.17f,1.7f,19.3f})
    for(int i=0;i<32;i++) {
        float t=(i+.5f)/32;
        auto a=lava(make_float2(x,y),make_float2(1.125f,t),time,1,3);
        auto b=lava(make_float2(x+1,y),make_float2(.125f,t),time,0,7);
        auto c=lava(make_float2(x,y),make_float2(t,-.125f),time,1,3);
        auto d=lava(make_float2(x,y+1),make_float2(t,.875f),time,0,7);
        if(dot(a-b,a-b)>1e-10 || dot(c-d,c-d)>1e-10) return 1;
        checks++;
    }
    auto a=lava(make_float2(2,3),make_float2(.5f,.5f),0,0,0);
    auto b=lava(make_float2(2,3),make_float2(.5f,.5f),1,0,0);
    if(dot(a-b,a-b)<1e-6) return 2;
    printf("Lava production UV math: %d adjacency checks, carrier/descriptor independence and motion passed.\n", checks);
}
