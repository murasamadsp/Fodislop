float2 lava(float2 cell, float2 local, float time, float anchored, float descriptor) {
    auto tile=ResolveTerrainTileUV(
        make_float2(.7f,.3f), make_float4(.25f,.125f,.15625f,.125f),
        make_float4(.015625f,.015625f,1,8), make_float4(cell.x,cell.y,descriptor,1),
        make_float4(4,10,0,2), make_float4(anchored,local.x,local.y,0), time,
        make_float2(1.f/2048));
    return ClampTerrainTileUV(tile.finalUV,tile);
}

// Кристаллы и камень: сплошной лист по мировой координате (бит 2 в w).
// Проверяется ровно «стыкуются»: фрагмент у общего ребра двух соседних
// клеток обязан попасть в один и тот же тексель атласа, в том числе когда
// одна из клеток искажена, а другая нет.
// w остаётся тем, чем был: 0 или 1. Значение больше 1.5 — чужой маркер,
// по которому фрагмент отбрасывают целиком, и признак листа туда класть
// нельзя. Он живёт битом 5 в z, над колонкой тайлгруппы.
float2 sheet(float2 cell, float2 local, float anchored, float descriptor, float tiling) {
    auto tile=ResolveTerrainTileUV(
        make_float2(.7f,.3f), make_float4(.25f,.125f,.15625f,.15625f),
        make_float4(.015625f,.015625f,1,1), make_float4(cell.x,cell.y,descriptor+32,tiling),
        make_float4(0,10,0,3), make_float4(anchored,local.x,local.y,0), 0.f,
        make_float2(1.f/2048));
    return ClampTerrainTileUV(tile.finalUV,tile);
}
int checkSheet() {
    int checks=0;
    for(int x : {-32,-1,0,9,10,31,32})
    for(int y : {-32,-1,0,9,10,31,32})
    for(int i=0;i<32;i++) {
        float t=(i+.5f)/32;
        // Горизонтальный шов: правый край левой клетки против левого края
        // правой. Смещение узла уводит выборку за край своего тайла, и она
        // обязана продолжиться тем же куском листа.
        auto a=sheet(make_float2(x,y),make_float2(1.125f,t),1,3,0);
        auto b=sheet(make_float2(x+1,y),make_float2(.125f,t),0,7,1);
        auto c=sheet(make_float2(x,y),make_float2(t,-.125f),1,3,1);
        auto d=sheet(make_float2(x,y+1),make_float2(t,.875f),0,7,0);
        if(dot(a-b,a-b)>1e-10 || dot(c-d,c-d)>1e-10) return 0;
        checks++;
    }
    // Лист прибит к миру, а не к клетке: одна и та же мировая точка даёт
    // один тексель, с какой бы из двух клеток её ни спрашивали.
    for(int x : {0,5,9,10})
    for(int y : {0,5,9,10}) {
        auto a=sheet(make_float2(x,y),make_float2(1.5f,.5f),1,0,1);
        auto b=sheet(make_float2(x+1,y),make_float2(.5f,.5f),0,0,0);
        if(dot(a-b,a-b)>1e-10) return 0;
        checks++;
    }
    return checks;
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
    float minimum=100, maximum=-100;
    for(int t=0;t<200;t++) {
        auto color=EvaluateMoltenHeat(make_float3(1,.03f,0),make_float2(2.5f,3.5f),t*.12f);
        minimum=std::min(minimum,color.x);maximum=std::max(maximum,color.x);
        auto samePixel=EvaluateMoltenHeat(make_float3(1,.03f,0),make_float2(2.51f,3.51f),t*.12f);
        if(dot(color.xy-samePixel.xy,color.xy-samePixel.xy)>1e-10) return 3;
    }
    if(maximum-minimum<.5f) return 4;
    int sheetChecks=checkSheet();
    if(sheetChecks==0) return 5;
    printf("Crystal/rock sheet addressing: %d seam checks passed.\n", sheetChecks);
    printf("Lava production UV math: %d adjacency checks, carrier/descriptor independence and motion passed.\n", checks);
}
