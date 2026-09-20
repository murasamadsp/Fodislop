// Independent triangle rasterization, followed by the production fragment mask.
// Sample on both sides of every logical pixel center: checking centers alone
// cannot distinguish a staircase from an ordinary diagonal edge.
float cross2(float2 a, float2 b) { return a.x*b.y-a.y*b.x; }
bool triangle(float2 p, float2 a, float2 b, float2 c, float3& weights)
{
    float area = cross2(b-a,c-a);
    weights.y = cross2(p-a,c-a)/area;
    weights.z = cross2(b-a,p-a)/area;
    weights.x = 1-weights.y-weights.z;
    return weights.x >= -1e-6f && weights.y >= -1e-6f && weights.z >= -1e-6f;
}
bool oracle(float2 p, float4 xs, float4 ys)
{
    // All generated quadrilaterals are convex and counter-clockwise.
    for (int i=0; i<4; ++i)
    {
        int j=(i+1)%4;
        if (cross2(float2{xs[j]-xs[i],ys[j]-ys[i]},p-float2{xs[i],ys[i]}) < -1e-6f)
            return false;
    }
    return true;
}
bool rendered(float2 p, const TerrainCellVertex* vertices, float4 xs, float4 ys)
{
    for(int tri=0;tri<2;++tri)
    {
        int a=0,b=tri+1,c=tri+2;
        float3 w;
        if(!triangle(p,vertices[a].positionOS.xy,vertices[b].positionOS.xy,vertices[c].positionOS.xy,w)) continue;
        float2 sample=vertices[a].packedData.yz*w.x+vertices[b].packedData.yz*w.y+vertices[c].packedData.yz*w.z;
        return TerrainGeometryCoverage(sample,xs,ys,1)>.5f;
    }
    return false;
}
// Кайма рельефа. Проверяется ровно то, ради чего маска считается: шов идёт
// по границе между семьями, внутри семьи его нет, и обе стороны шва темнеют
// одинаково — иначе граница выглядит сдвинутой на полклетки.
static float packContour(int reliefCode)
{
    return float(reliefCode * 64);
}

static void checkReliefRim()
{
    const float2 centre{0.5f, 0.5f};
    const float2 nearTop{0.5f, 0.99f};
    const float2 nearBottom{0.5f, 0.01f};
    const float2 nearLeft{0.01f, 0.5f};
    const float2 nearRight{0.99f, 0.5f};

    // Клетка без рельефа не трогается нигде.
    for(float2 p : {centre, nearTop, nearBottom, nearLeft, nearRight})
        if(TerrainReliefRim(p, packContour(0)) != 1.0f)
            throw std::runtime_error("Relief rim darkened a cell without a relief group");

    // Вся семья вокруг: код 16 — маска 15 со сдвигом на единицу.
    for(float2 p : {centre, nearTop, nearBottom, nearLeft, nearRight})
        if(TerrainReliefRim(p, packContour(16)) != 1.0f)
            throw std::runtime_error("Relief rim darkened the interior of a solid mass");

    // Чужой только сверху: маска 0b1110 = 14, код 15.
    float topForeign = packContour(15);
    float darkest = TerrainReliefRim(nearTop, topForeign);
    if(darkest >= 0.25f)
        throw std::runtime_error("Relief rim missing on the foreign side");
    // Нижняя граница так же обязательна, как верхняя: кайма, севшая в
    // чёрный, режет массив на отдельные плитки. Оригинальный масштаб по
    // половине диагонали не даёт ей упасть ниже 0.125.
    if(darkest <= 0.1f)
        throw std::runtime_error("Relief rim is darker than the original scale allows");
    if(TerrainReliefRim(nearBottom, topForeign) != 1.0f ||
       TerrainReliefRim(nearLeft, topForeign) != 1.0f ||
       TerrainReliefRim(nearRight, topForeign) != 1.0f)
        throw std::runtime_error("Relief rim leaked onto a side of the same family");
    if(TerrainReliefRim(centre, topForeign) < 0.99f)
        throw std::runtime_error("Relief rim reached the centre of the cell");

    // Шов симметричен: у соседа снизу чужой ровно низ (маска 0b1011 = 11,
    // код 12), и обе клетки темнеют у общего ребра одинаково.
    float bottomForeign = packContour(12);
    float above = TerrainReliefRim(nearTop, topForeign);
    float below = TerrainReliefRim(nearBottom, bottomForeign);
    if(std::fabs(above - below) > 1e-6f)
        throw std::runtime_error("Relief seam is darker on one side than the other");

    // Координата несущего прямоугольника. У якорной клетки она выходит за
    // пределы клетки на величину смещения углов, и кайма обязана остаться
    // той же самой: без зажима она садилась в ноль и рисовала чёрную полосу
    // по краю каждой искажённой клетки. Проверять только 0..1 значило
    // проверять ровно тот случай, который и так работал.
    for(int side=0; side<4; ++side)
    {
        float foreign = packContour(((~(1 << side)) & 0x0F) + 1);
        for(float over : {-0.1875f, -0.09f, 1.09f, 1.1875f})
        {
            for(float along : {-0.1875f, 0.25f, 0.5f, 0.75f, 1.1875f})
            {
                float v = TerrainReliefRim(float2{along, over}, foreign);
                if(v < 0.1f || v > 1.0f)
                    throw std::runtime_error(
                        "Relief rim leaves its range on an anchored carrier sample");
                v = TerrainReliefRim(float2{over, along}, foreign);
                if(v < 0.1f || v > 1.0f)
                    throw std::runtime_error(
                        "Relief rim leaves its range on an anchored carrier sample");
            }
        }
    }

    // Падение монотонно от центра к краю: иначе кайма читается полосой,
    // а не гранью.
    float previous = 2.0f;
    for(int step=0; step<=16; ++step)
    {
        float v = TerrainReliefRim(float2{0.5f, 0.5f + step * (0.5f/16.f)}, topForeign);
        if(v > previous + 1e-6f)
            throw std::runtime_error("Relief rim is not monotonic towards the edge");
        previous = v;
    }
}

void checkAo()
{
    _WorldAmbientOcclusionYFlip=0;
    _TerrainAmbientOcclusionStrength=1;
    float2 corners[]={{0,0},{1,0},{1,1},{0,1}};
    float previousDifference=-1;
    for(int density : {8,16,32,64})
    {
        float samples[2];
        for(int shape=0;shape<2;++shape)
        {
            float4 xs={0,1,shape ? .5f : 1.f,0},ys={0,0,1,1};
            _TerrainCellGeometryX.data[1]=xs;
            _TerrainCellGeometryY.data[1]=ys;
            TerrainCellVertex vertices[4];
            for(int i=0;i<4;++i)
                vertices[i]=LoadTerrainCellVertex(float3{3,3,1},corners[i]);
            Texture field;
            field.reset(8*density,8*density);
            for(int y=0;y<field.height;++y) for(int x=0;x<field.width;++x)
            {
                float2 p={(x+.5f)/density,(y+.5f)/density};
                field.data[y*field.width+x].a=rendered(p,vertices,xs,ys) ? 1 : 0;
            }
            _WorldAmbientOcclusionTexture.generate(std::move(field));
            _WorldAmbientOcclusionTexelsPerCell=density;
            samples[shape]=KernSampleTerrainAmbientOcclusion(float2{4.0625f,3.875f},float4{0,0,8,8});
            float far=KernSampleTerrainAmbientOcclusion(float2{6,6},float4{0,0,8,8});
            if(far!=0) throw std::runtime_error("Isolated block AO leaks beyond the contact neighbourhood");
            float mass=KernTerrainAmbientOcclusionMultiplier(64,float2{4.0625f,3.875f},float4{0,0,8,8});
            if(mass!=1) throw std::runtime_error("Physical foreground self-darkens");
        }
        float difference=samples[0]-samples[1];
        if(difference<.15f)
            throw std::runtime_error("AO lost the sloped silhouette: square="+std::to_string(samples[0])+" slope="+std::to_string(samples[1]));
        if(previousDifference>=0 && std::abs(difference-previousDifference)>.08f)
            throw std::runtime_error("AO footprint changed with field resolution");
        previousDifference=difference;
    }
    std::cout << "Production AO sampling passed: shape sensitivity, density 8/16/32/64, empty distance and foreground receiver.\n";
}
int runChecks()
{
    Texture* channels[] = {&_TerrainCellColor, &_TerrainCellMeta,
        &_TerrainCellAtlasRect, &_TerrainCellTileSize, &_TerrainCellWorld,
        &_TerrainCellAnimation, &_TerrainCellGlow, &_TerrainCellGeometryX,
        &_TerrainCellGeometryY};
    for (Texture* channel : channels) channel->reset(1,2);
    _TerrainCellGridSize = {1,1,1,0};
    _TerrainCellOrigin = {0,0,0,0};
    _TerrainCellViewOffset = {0,0,0,0};
    _TerrainCellMeta.data[1] = {1.f/255,228.f/255,0,1};
    _TerrainCellMeta.data[0] = {1.f/255,228.f/255,0,1}; // stale anchor must not move background
    std::mt19937 rng(0x32AABB);
    float2 corners[] = {{0,0},{1,0},{1,1},{0,1}};
    long checked=0, outward=0;
    for(int shape=0;shape<128;++shape)
    {
        float4 xs,ys;
        for(int i=0;i<4;++i)
        {
            // Диапазон обязан покрывать продакшен целиком. Свободный джиттер
            // внутри массива породы даёт +-3*DistortionStrengthSteps шагов,
            // то есть +-6/32; при +-4/32 две самые сильные ступени не
            // проверялись вовсе.
            xs[i]=corners[i].x+(int(rng()%13)-6)/32.f;
            ys[i]=corners[i].y+(int(rng()%13)-6)/32.f;
        }
        // Предпосылка oracle(): четырёхугольник выпуклый. При смещении до
        // 6/32 это выполняется с запасом — чтобы стать невыпуклым, углу надо
        // пересечь диагональ соседей, а это больше половины клетки. Проверяем,
        // а не предполагаем: поднимут амплитуду — падёт здесь, а не в виде
        // молчаливого расхождения с оракулом.
        for(int i=0;i<4;++i)
        {
            int j=(i+1)%4, k=(i+2)%4;
            float turn=cross2(float2{xs[j]-xs[i],ys[j]-ys[i]},
                              float2{xs[k]-xs[j],ys[k]-ys[j]});
            if(turn<=0) throw std::runtime_error(
                "Generated quad is not convex; oracle() precondition broken");
        }
        _TerrainCellGeometryX.data[1]=xs;
        _TerrainCellGeometryY.data[1]=ys;
        _TerrainCellGeometryX.data[0]=xs;
        _TerrainCellGeometryY.data[0]=ys;
        TerrainCellVertex vertices[4];
        for(int i=0;i<4;++i)
        {
            vertices[i]=LoadTerrainCellVertex(float3{0,0,1},corners[i]);
            auto background=LoadTerrainCellVertex(float3{0,0,0},corners[i]);
            if(background.positionOS.x!=corners[i].x || background.positionOS.y!=corners[i].y)
                throw std::runtime_error("Background was distorted");
        }
        for(int y=-5;y<37;++y) for(int x=-5;x<37;++x)
        {
            bool expected=oracle(float2{(x+.5f)/32,(y+.5f)/32},xs,ys);
            for(int sy=0;sy<4;++sy) for(int sx=0;sx<4;++sx)
            {
                float2 p={(x+(sx+.5f)/4)/32,(y+(sy+.5f)/4)/32};
                bool actual=rendered(p,vertices,xs,ys);
                ++checked;
                outward += expected && !oracle(p,xs,ys);
                if(actual!=expected)
                {
                    std::cerr << "shape=" << shape << " pixel=" << x << "," << y
                        << " subpixel=" << sx << "," << sy << " expected=" << expected << " actual=" << actual << '\n';
                    return 1;
                }
            }
        }
        // The right neighbour shares both endpoints of the displaced edge.
        // Rasterize both independently; their union must have no black seam.
        float4 nx={xs.y-1,1,1,xs.z-1}, ny={ys.y,0,1,ys.z};
        _TerrainCellGeometryX.data[1]=nx;
        _TerrainCellGeometryY.data[1]=ny;
        TerrainCellVertex neighbour[4];
        for(int i=0;i<4;++i)
            neighbour[i]=LoadTerrainCellVertex(float3{1,0,1},corners[i]);
        for(int y=32;y<96;++y) for(int x=96;x<160;++x)
        {
            float2 p={(x+.5f)/128,(y+.5f)/128};
            if(!rendered(p,vertices,xs,ys) && !rendered(p,neighbour,nx,ny))
                throw std::runtime_error("Uncovered shared edge between adjacent cells");
        }
    }
    checkAo();
    checkReliefRim();
    if(outward==0) throw std::runtime_error("No outward staircase samples exercised");
    std::cout << "Production HLSL carrier/mask passed: " << checked
        << " subpixels, including " << outward << " outside the original triangles; 512 background corners and 524288 adjacent-edge samples.\n";
    return 0;
}

int main()
{
    try
    {
        return runChecks();
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
