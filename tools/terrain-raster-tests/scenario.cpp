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
float segmentDistanceSquared(float2 p, float2 a, float2 b)
{
    float2 edge = b - a;
    float lengthSquared = std::max(dot(edge, edge), 1e-6f);
    float projection = std::clamp(dot(p - a, edge) / lengthSquared, 0.0f, 1.0f);
    float2 delta = p - (a + edge * projection);
    return dot(delta, delta);
}
bool sealedOracle(float2 p, float4 xs, float4 ys)
{
    if (oracle(p, xs, ys)) return true;
    const float seal = 0.5f / 32.0f;
    for (int i = 0; i < 4; ++i)
    {
        int j = (i + 1) % 4;
        if (segmentDistanceSquared(
                p,
                float2{xs[i], ys[i]},
                float2{xs[j], ys[j]}) <= seal * seal)
            return true;
    }
    return false;
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
bool expectedRendered(float2 p, const TerrainCellVertex* vertices, float4 xs, float4 ys)
{
    for(int tri=0;tri<2;++tri)
    {
        int a=0,b=tri+1,c=tri+2;
        float3 w;
        if(!triangle(p,vertices[a].positionOS.xy,vertices[b].positionOS.xy,vertices[c].positionOS.xy,w)) continue;
        float2 sample=vertices[a].packedData.yz*w.x+vertices[b].packedData.yz*w.y+vertices[c].packedData.yz*w.z;
        float2 quantized = float2{
            (std::floor(sample.x * 32.0f) + 0.5f) / 32.0f,
            (std::floor(sample.y * 32.0f) + 0.5f) / 32.0f};
        return sealedOracle(quantized,xs,ys);
    }
    return false;
}

// Затенение не имеет права гасить поверхность в ноль: полная занятость
// вокруг обязана оставить ровно пол, иначе тень читается дырой. Число
// совпадает с оригиналом (1 - z² при z = 0.7).
static void checkAmbientOcclusionFloor()
{
    _WorldAmbientOcclusionYFlip=0;
    _TerrainAmbientOcclusionStrength=1;
    _TerrainAmbientOcclusionFloor=0.51f;
    Texture solid;
    solid.reset(64,64);
    for(int i=0;i<64*64;++i) solid.data[i].a=1;
    _WorldAmbientOcclusionTexture.generate(std::move(solid));
    _WorldAmbientOcclusionTexelsPerCell=8;
    float darkest=KernTerrainAmbientOcclusionMultiplier(
        0, float2{4.0f,4.0f}, float4{0,0,8,8});
    if(std::fabs(darkest-0.51f)>1e-3f)
        throw std::runtime_error(
            "Contact occlusion does not stop at the floor: " + std::to_string(darkest));

    Texture empty;
    empty.reset(64,64);
    for(int i=0;i<64*64;++i) empty.data[i].a=0;
    _WorldAmbientOcclusionTexture.generate(std::move(empty));
    _WorldAmbientOcclusionTexelsPerCell=8;
    float brightest=KernTerrainAmbientOcclusionMultiplier(
        0, float2{4.0f,4.0f}, float4{0,0,8,8});
    if(std::fabs(brightest-1.0f)>1e-3f)
        throw std::runtime_error(
            "Contact occlusion darkens an empty neighbourhood");
}

// Упаковка слова контура — та же арифметика, что в TerrainLightingData.Pack:
// биты 0-1 флаги контура, 2-5 диагональные соседи, 6+ код рельефа. Хендмейдный
// reliefCode*64 проверял только шейдер и молчал о том, переживает ли код
// соседство с занятыми младшими битами.
static float packContourFull(int reliefCode, int contourFlags, int solidDiagonal)
{
    return float(contourFlags + solidDiagonal * 4 + reliefCode * 64);
}

static float packContour(int reliefCode) { return packContourFull(reliefCode, 0, 0); }

// Код рельефа из маски своих соседей — как это делает TerrainQuadBuilder:
// маска + 1, ноль оставлен под «клетка без рельефа».
static float packReliefMask(int reliefMask, int contourFlags, int solidDiagonal)
{
    return packContourFull((reliefMask & 0x0F) + 1, contourFlags, solidDiagonal);
}

// Канонические углы: несмещённая клетка.
static float rim(float2 p, float packed)
{
    return TerrainReliefRimRaw(p, float4{0,1,1,0}, float4{0,0,1,1}, packed);
}

// Смещённая клетка: падение обязано растянуться на её реальные границы, а
// не упереться в дно на всём выступе.
static float displacedRim(float2 p, float4 xs, float4 ys, float packed)
{
    return TerrainReliefRimRaw(p, xs, ys, packed);
}

// Кайма: три стороны, верх не затемняется никогда, дно падения 0.125, и
// координата несущего прямоугольника не должна выводить её из диапазона.
static void checkReliefRim()
{
    _TerrainReliefRimEnabled = 1.0f;
    const float2 nearTop{0.5f, 0.99f};
    const float2 nearBottom{0.5f, 0.01f};
    const float2 nearLeft{0.01f, 0.5f};
    const float2 nearRight{0.99f, 0.5f};
    const float2 centre{0.5f, 0.5f};

    // Нет рельефной группы — кайма не трогает ничего.
    for(float2 p : {centre, nearTop, nearBottom, nearLeft, nearRight})
        if(rim(p, packContour(0)) != 1.0f)
            throw std::runtime_error("Relief rim darkened a cell without a relief group");

    // Вся семья вокруг: код 16 — маска 15 со сдвигом.
    for(float2 p : {centre, nearTop, nearBottom, nearLeft, nearRight})
        if(rim(p, packContour(16)) != 1.0f)
            throw std::runtime_error("Relief rim darkened the interior of a solid mass");

    // Середина клетки не трогается ни при каких чужих сторонах.
    if(rim(centre, packContour(1)) < 0.99f)
        throw std::runtime_error("Relief rim reached the centre of the cell");

    // Все четыре соседа чужие: маска 0, код 1. Низ, лево и право темнеют,
    // верх обязан остаться нетронутым.
    float allForeign = packContour(1);
    for(float2 p : {nearTop, nearBottom, nearLeft, nearRight})
    {
        float v = rim(p, allForeign);
        if(v >= 0.25f)
            throw std::runtime_error("Relief rim missing on a foreign side");
        if(v <= 0.1f)
            throw std::runtime_error("Relief rim is darker than the original scale allows");
    }

    // Ровно одна сторона чужая — темнеет только её сектор.
    for(int side = 0; side < 4; ++side)
    {
        int mask = (~(1 << side)) & 0x0F;
        float one = packContour(mask + 1);
        const float2 probes[4] = {nearTop, nearLeft, nearBottom, nearRight};
        for(int other = 0; other < 4; ++other)
        {
            float v = rim(probes[other], one);
            bool expectDark = other == side;
            if(expectDark && v >= 0.25f)
                throw std::runtime_error("Relief rim missing on the single foreign side");
            // Не ровно единица: квантование сдвигает середину клетки на
            // полтексела, и противоположная грань даёт 0.9985. Утечкой это
            // не является, а вот заметное затемнение — является.
            if(!expectDark && v < 0.99f)
                throw std::runtime_error("Relief rim leaked onto a side of the same family");
        }
    }
    if(rim(centre, allForeign) < 0.99f)
        throw std::runtime_error("Relief rim reached the centre of the cell");

    // Смещённая клетка. Выступающая грань не имеет права темнеть сильнее
    // такой же грани ровной клетки: падение нормируется по границам
    // полигона, а не зажимается в единичный квадрат.
    {
        float4 xs{-0.1875f, 1.1875f, 1.1875f, -0.1875f};
        float4 ys{-0.1875f, -0.1875f, 1.1875f, 1.1875f};
        float2 span{1.375f, 1.375f};
        float2 polygonCentre{-0.1875f + span.x * 0.5f, -0.1875f + span.y * 0.5f};
        // Четверть высоты полигона: у самого края оба варианта упираются в
        // дно и разницы не видно, а здесь зажим уже расходится с нормировкой.
        float2 quarterUp{polygonCentre.x, -0.1875f + span.y * 0.25f};

        float flat = rim(float2{0.5f, 0.25f}, allForeign);
        float displaced = displacedRim(quarterUp, xs, ys, allForeign);
        if(std::fabs(flat - displaced) > 0.02f)
            throw std::runtime_error(
                "Displaced edge darkens differently from a flat one: flat=" +
                std::to_string(flat) + " displaced=" + std::to_string(displaced));

        if(displacedRim(polygonCentre, xs, ys, allForeign) < 0.99f)
            throw std::runtime_error("Relief rim reached the centre of a displaced cell");
    }

    // Кайма обязана тайлиться. Вдоль чужой грани значение постоянно по всей
    // длине, включая углы клетки: пока затухание резалось диагональю сектора,
    // полоса сходила на нет у каждого стыка с соседней клеткой, и граница
    // массива читалась пунктиром.
    {
        float bottomForeign = packContour((((~4) & 0x0F)) + 1);
        float reference = rim(float2{0.5f, 0.02f}, bottomForeign);
        for(int i = 0; i <= 20; ++i)
        {
            float along = i / 20.0f;
            float v = rim(float2{along, 0.02f}, bottomForeign);
            if(std::fabs(v - reference) > 1e-4f)
                throw std::runtime_error(
                    "Relief rim is not constant along a foreign edge: at " +
                    std::to_string(along) + " it is " + std::to_string(v) +
                    " against " + std::to_string(reference));
        }
    }

    // Угол двух чужих граней темнее каждой из них по отдельности.
    {
        float bottomOnly = packContour((((~4) & 0x0F)) + 1);
        float leftOnly = packContour((((~2) & 0x0F)) + 1);
        float both = packContour((((~6) & 0x0F)) + 1);
        float2 corner{0.02f, 0.02f};
        float a = rim(corner, bottomOnly);
        float b = rim(corner, leftOnly);
        float c = rim(corner, both);
        if(c >= a || c >= b)
            throw std::runtime_error("A corner of two foreign edges is not darker than either");
    }

    // Полная упаковка: младшие биты заняты, код обязан дойти целым.
    //
    // Это и есть шов, на котором кайма могла бы включаться через клетку.
    // Флаги контура и маска диагональных соседей меняются от клетки к клетке,
    // и если бы код рельефа стоял не на своём месте, кайма то появлялась бы,
    // то исчезала по соседству, которое к ней отношения не имеет. Здесь
    // прогоняются все 16 масок против всех 64 комбинаций младших битов.
    {
        const float2 sideProbes[4] = {nearTop, nearLeft, nearBottom, nearRight};
        for(int reliefMask = 0; reliefMask <= 0x0F; ++reliefMask)
        {
            float clean = packReliefMask(reliefMask, 0, 0);
            for(int contourFlags = 0; contourFlags <= 3; ++contourFlags)
            for(int solidDiagonal = 0; solidDiagonal <= 0x0F; ++solidDiagonal)
            {
                float packed = packReliefMask(reliefMask, contourFlags, solidDiagonal);
                for(int side = 0; side < 4; ++side)
                {
                    float expected = rim(sideProbes[side], clean);
                    float actual = rim(sideProbes[side], packed);
                    if(std::fabs(expected - actual) > 1e-5f)
                        throw std::runtime_error(
                            "Relief code does not survive the packed word: mask=" +
                            std::to_string(reliefMask) + " flags=" +
                            std::to_string(contourFlags) + " diagonal=" +
                            std::to_string(solidDiagonal) + " side=" +
                            std::to_string(side) + " expected=" +
                            std::to_string(expected) + " actual=" +
                            std::to_string(actual));
                }

                // И содержательно: своя сторона не темнеет, чужая темнеет.
                for(int side = 0; side < 4; ++side)
                {
                    bool sameFamily = (reliefMask & (1 << side)) != 0;
                    float v = rim(sideProbes[side], packed);
                    if(sameFamily && v < 0.99f)
                        throw std::runtime_error(
                            "Relief rim darkens a side whose neighbour is the same family: mask=" +
                            std::to_string(reliefMask) + " side=" + std::to_string(side));
                    if(!sameFamily && v >= 0.25f)
                        throw std::runtime_error(
                            "Relief rim missing on a foreign side: mask=" +
                            std::to_string(reliefMask) + " side=" + std::to_string(side));
                }
            }
        }
    }

    // Выключатель снимает кайму целиком.
    _TerrainReliefRimEnabled = 0.0f;
    if(rim(nearBottom, packContour(1)) != 1.0f)
        throw std::runtime_error("Disabled relief rim still darkens the frame");
    _TerrainReliefRimEnabled = 1.0f;

    // Вырожденные углы: путь вершин без геометрии кладёт нули, и нормировка
    // по размаху обязана это пережить. Пока размах зажимался в эпсилон,
    // клеточная координата становилась (1,1), и оверлей дверей гасился
    // целиком в 1/8 яркости.
    {
        float4 zero{0, 0, 0, 0};
        for(float2 p : {centre, nearTop, nearBottom, nearLeft, nearRight})
            if(displacedRim(p, zero, zero, allForeign) != 1.0f)
                throw std::runtime_error("Degenerate polygon bounds still darken the cell");
    }

    // Координата несущего прямоугольника выходит за клетку у смещённых
    // клеток; кайма обязана остаться в своём диапазоне.
    for(float over : {-0.1875f, 1.1875f})
        for(float along : {-0.1875f, 0.5f, 1.1875f})
        {
            float a = rim(float2{along, over}, allForeign);
            float b = rim(float2{over, along}, allForeign);
            // Нижняя граница — квадрат дна одной грани: на углу двух чужих
            // граней множители перемножаются, и 0.125² законны. Смысл
            // проверки в том, что координата несущего прямоугольника не
            // выбрасывает результат за пределы вовсе.
            if(a < 0.015f || a > 1.0f || b < 0.015f || b > 1.0f)
                throw std::runtime_error("Relief rim leaves its range on an anchored carrier sample");
        }
}

void checkAo()
{
    _WorldAmbientOcclusionYFlip=0;
    _TerrainAmbientOcclusionStrength=1;
    // Тот же пол, что в TerrainLook: множитель обязан останавливаться на нём.
    _TerrainAmbientOcclusionFloor=0.51f;
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
            for(int sy=0;sy<4;++sy) for(int sx=0;sx<4;++sx)
            {
                float2 p={(x+(sx+.5f)/4)/32,(y+(sy+.5f)/4)/32};
                bool actual=rendered(p,vertices,xs,ys);
                ++checked;
                bool expected=expectedRendered(p,vertices,xs,ys);
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

        // Верхний сосед. Проверялся только правый, то есть только
        // вертикальный стык; горизонтальный не проверял никто, а в кадре
        // именно он и читается — тонкой тёмной чертой поперёк породы там,
        // где не достаётся ни одной клетке и наружу смотрит фон мира.
        //
        // Нижняя грань соседа — это наша верхняя, сдвинутая на клетку вниз:
        // углы общие, так их строит TerrainCellGeometry.FromOffsets из одного
        // и того же GridVertexOffsets.
        float4 tx={xs.w,xs.z,1,0}, ty={ys.w-1,ys.z-1,1,1};
        _TerrainCellGeometryX.data[1]=tx;
        _TerrainCellGeometryY.data[1]=ty;
        TerrainCellVertex above[4];
        for(int i=0;i<4;++i)
            above[i]=LoadTerrainCellVertex(float3{0,1,1},corners[i]);
        for(int y=96;y<160;++y) for(int x=32;x<96;++x)
        {
            float2 p={(x+.5f)/128,(y+.5f)/128};
            if(!rendered(p,vertices,xs,ys) && !rendered(p,above,tx,ty))
                throw std::runtime_error(
                    "Uncovered shared edge between vertically adjacent cells at " +
                    std::to_string(p.x) + "," + std::to_string(p.y));
        }
    }
    checkAo();
    checkReliefRim();
    checkAmbientOcclusionFloor();
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
