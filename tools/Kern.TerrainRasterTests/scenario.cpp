#include <filesystem>
#include <fstream>
// Клетка кодируется здесь по таблице битов из TerrainCellData.cs, а не
// рабочим C#: так проверяется, что шейдер читает ту же раскладку. Клетки —
// ushort, по две в слове, как в TerrainCellBuffers.
static const int RingSize = 4;
static void setCell(int gridX, int unityY, uint bits)
{
    int x = ((gridX % RingSize) + RingSize) % RingSize;
    int y = ((unityY % RingSize) + RingSize) % RingSize;
    uint index = (uint)(y * RingSize + x);
    uint& word = _TerrainCells.data.at(index >> 1);
    uint shift = (index & 1u) * 16u;
    word = (word & ~(0xFFFFu << shift)) | ((bits & 0xFFFFu) << shift);
}
static const uint TypeCause = 1u << 20;
static void resetCells()
{
    _TerrainCells.reset(RingSize * RingSize / 2);
    _TerrainTypes.reset(256);
    _TerrainCellGridSize = {RingSize, RingSize, 1, 0};
    _TerrainCellOrigin = {0, 0, 1, 1};
    _TerrainCellViewOffset = {0, 0, 0, 0};
    _TerrainDistortionMode = 0;
    _TerrainTileDescriptors.reset(64);
    // Передний план — тип 1 (слот атласа 0, искажает), фон — тип 2: разные
    // типы, поэтому фон рисуется под каждой клеткой.
    for (int y = 0; y < RingSize; ++y) for (int x = 0; x < RingSize; ++x) setCell(x, y, 1u | (2u << 8));
    _TerrainTypes.data[1].b.z = TypeCause;
}

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
float oracleSignedDistance(float2 p, float4 xs, float4 ys)
{
    float nearestSquared = 1.0e20f;
    for (int side = 0; side < 4; ++side)
    {
        int next = (side + 1) & 3;
        float2 start{xs[side], ys[side]};
        float2 edge{xs[next] - xs[side], ys[next] - ys[side]};
        float t = std::clamp(
            ((p.x - start.x) * edge.x + (p.y - start.y) * edge.y) /
                (edge.x * edge.x + edge.y * edge.y),
            0.0f, 1.0f);
        float2 delta = p - (start + edge * t);
        nearestSquared = std::min(nearestSquared, delta.x * delta.x + delta.y * delta.y);
    }
    float distance = std::sqrt(nearestSquared);
    return oracle(p, xs, ys) ? distance : -distance;
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
        return oracle(sample,xs,ys);
    }
    return false;
}

// Затенение не имеет права гасить поверхность в ноль: полная занятость
// вокруг обязана оставить ровно пол, иначе тень читается дырой. Число
// совпадает с оригиналом (1 - z² при z = 0.7).
static void checkAmbientOcclusionFloor()
{
    _KernFieldRowsTopDown=0;
    _TerrainAmbientOcclusionStrength=1;
    _TerrainAmbientOcclusionFloor=0.51f;
    Texture solid;
    solid.reset(64,64);
    for(int i=0;i<64*64;++i) solid.data[i].a=1;
    _WorldAmbientOcclusionTexture.generate(std::move(solid));
    float darkest=KernTerrainAmbientOcclusionMultiplier(
        0, float2{4.0f,4.0f}, float4{0,0,8,8});
    if(std::fabs(darkest-0.51f)>1e-3f)
        throw std::runtime_error(
            "Contact occlusion does not stop at the floor: " + std::to_string(darkest));

    Texture empty;
    empty.reset(64,64);
    for(int i=0;i<64*64;++i) empty.data[i].a=0;
    _WorldAmbientOcclusionTexture.generate(std::move(empty));
    float brightest=KernTerrainAmbientOcclusionMultiplier(
        0, float2{4.0f,4.0f}, float4{0,0,8,8});
    if(std::fabs(brightest-1.0f)>1e-3f)
        throw std::runtime_error(
            "Contact occlusion darkens an empty neighbourhood");
}

// Упаковка слова контура — та же арифметика, что в TerrainLightingData.Pack:
// бит 0 — флаг контура, 1-4 — диагональные соседи, 5+ — код рельефа. Хендмейдный
// reliefCode*32 проверял только шейдер и молчал о том, переживает ли код
// соседство с занятыми младшими битами.
static float packContourFull(int reliefCode, int contourFlags, int solidDiagonal)
{
    return float(contourFlags + solidDiagonal * 2 + reliefCode * 32);
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
    TerrainSurfaceInputs surface{};
    surface.cellSample=p;
    surface.cornersX=float4{0,1,1,0};
    surface.cornersY=float4{0,0,1,1};
    surface.packedContour=packed;
    return TerrainReliefRim(surface);
}

// Смещённая клетка: падение обязано растянуться на её реальные границы, а
// не упереться в дно на всём выступе.
static float displacedRim(float2 p, float4 xs, float4 ys, float packed)
{
    TerrainSurfaceInputs surface{};
    surface.cellSample=p;
    surface.cornersX=xs;
    surface.cornersY=ys;
    surface.packedContour=packed;
    return TerrainReliefRim(surface);
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
        // The vertices are snapped once. Fragment positions remain continuous,
        // so the expected distances are exactly a quarter of each side span.
        auto sideFalloff=[](float distance)
        {
            float s=std::clamp(1.0f-2.0f*distance,0.0f,1.0f);
            float fall=1.0f-0.5f*s*s;
            return fall*fall*fall;
        };
        if(std::fabs(flat - sideFalloff(.25f)) > 1e-4f ||
            std::fabs(displaced - sideFalloff(span.y*.25f)) > 1e-4f)
        {
            throw std::runtime_error(
                "Relief rim does not follow the displaced edge distance: flat=" +
                std::to_string(flat) + " displaced=" + std::to_string(displaced));
        }

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
            // Compare tile-pixel centers: exact UV 0/1 lie beyond the center
            // of the first/last texel and are correctly outside the segment.
            float along = (i + 0.5f) / 21.0f;
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
    // прогоняются все 16 масок против всех 32 комбинаций младших битов.
    {
        const float2 sideProbes[4] = {nearTop, nearLeft, nearBottom, nearRight};
        for(int reliefMask = 0; reliefMask <= 0x0F; ++reliefMask)
        {
            float clean = packReliefMask(reliefMask, 0, 0);
            for(int contourFlags = 0; contourFlags <= 1; ++contourFlags)
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
    _KernFieldRowsTopDown=0;
    _TerrainAmbientOcclusionStrength=1;
    // Тот же пол, что в TerrainLook: множитель обязан останавливаться на нём.
    _TerrainAmbientOcclusionFloor=0.51f;
    for(int density : {8,16,32,64})
    {
        float samples[2];
        for(int shape=0;shape<2;++shape)
        {
            float4 xs={0,1,shape ? .5f : 1.f,0},ys={0,0,1,1};
            Texture field;
            field.reset(8*density,8*density);
            for(int y=0;y<field.height;++y) for(int x=0;x<field.width;++x)
            {
                float2 p={(x+.5f)/density,(y+.5f)/density};
                float signedDistance = TerrainGeometrySignedDistance(
                    p-float2{3,3},xs,ys,0,false);
                float contact = 1.0f-smoothstep(0.0f,0.5f,max(-signedDistance,0.0f));
                float expectedDistance = oracleSignedDistance(p-float2{3,3},xs,ys);
                float expectedContact = 1.0f-smoothstep(
                    0.0f,0.5f,max(-expectedDistance,0.0f));
                if (std::fabs(contact - expectedContact) > 1e-4f)
                    throw std::runtime_error("AO contact field differs from independent polygon-distance oracle");
                field.data[y*field.width+x].a = contact;
            }
            _WorldAmbientOcclusionTexture.generate(std::move(field));
            samples[shape]=KernSampleTerrainAmbientOcclusion(float2{4.0625f,3.875f},float4{0,0,8,8});
            float far=KernSampleTerrainAmbientOcclusion(float2{6,6},float4{0,0,8,8});
            if(far!=0) throw std::runtime_error("Isolated block AO leaks beyond the contact neighbourhood");
            float mass=KernTerrainAmbientOcclusionMultiplier(32,float2{4.0625f,3.875f},float4{0,0,8,8});
            if(mass!=1) throw std::runtime_error("Physical foreground self-darkens");
        }
        float difference=samples[0]-samples[1];
        if(difference<.025f)
            throw std::runtime_error("AO lost the sloped silhouette: square="+std::to_string(samples[0])+" slope="+std::to_string(samples[1]));
    }
    std::cout << "AO field sampling passed: shape sensitivity, density 8/16/32/64, empty distance and foreground receiver.\n";
}

void checkContinuousFieldSilhouette()
{
    auto checkEdge=[](float4 xs,float4 ys,float2 midpoint,float2 outwardNormal,
                      float sampleRange,const char* label)
    {
        float previous=1.0f;
        int fractionalSamples=0;
        for(int i=0;i<=16;++i)
        {
            float signedOffset=sampleRange*(1.0f-i/8.0f);
            float2 p=midpoint-outwardNormal*signedOffset;
            float actualDistance=TerrainGeometrySignedDistance(p,xs,ys,0.0f,false);
            float expectedDistance=oracleSignedDistance(p,xs,ys);
            if(std::fabs(actualDistance-expectedDistance)>1e-4f)
                throw std::runtime_error(
                    std::string("AO ")+label+" distance disagrees with independent polygon oracle");

            float actualExterior=std::max(-actualDistance,0.0f);
            float expectedExterior=std::max(-expectedDistance,0.0f);
            float actual=1.0f-smoothstep(0.0f,_TerrainAmbientOcclusionDistance,actualExterior);
            float expected=1.0f-smoothstep(0.0f,_TerrainAmbientOcclusionDistance,expectedExterior);
            if(std::fabs(actual-expected)>1e-4f)
                throw std::runtime_error(
                    std::string("AO ")+label+" contact disagrees with independent distance oracle");
            if(actual>previous+1e-5f)
                throw std::runtime_error(std::string("AO ")+label+" contact edge is not monotonic");
            if(actual>0.0f && actual<1.0f)
                ++fractionalSamples;
            previous=actual;
        }
        if(fractionalSamples<2)
            throw std::runtime_error(std::string("AO ")+label+" contact falloff is not continuous");
    };

    float4 squareX={0,1,1,0};
    float4 squareY={0,0,1,1};
    checkEdge(squareX,squareY,float2{1,.5f},float2{1,0},
        _TerrainAmbientOcclusionDistance*2.0f,"axis-aligned");

    // A diagonal uses Euclidean signed distance, so its falloff remains
    // isotropic instead of being stretched by the larger coordinate derivative.
    float4 diamondX={.5f,1,.5f,0};
    float4 diamondY={0,.5f,1,.5f};
    checkEdge(diamondX,diamondY,float2{.75f,.75f},
        float2{.70710678f,.70710678f},_TerrainAmbientOcclusionDistance*2.0f,
        "diagonal");
}

void checkOrganicVerticesUseGeometryGrid()
{
    float4 xs={0,1,1,0};
    float4 ys={0,0,1,1};
    float4 bends={.03125f,-.0625f,.0625f,-.03125f};
    for(int index=0;index<8;++index)
    {
        float2 point=TerrainOrganicGeometryPoint(xs,ys,bends,index);
        if(std::fabs(point.x*32.0f-std::round(point.x*32.0f))>1e-5f ||
            std::fabs(point.y*32.0f-std::round(point.y*32.0f))>1e-5f)
            throw std::runtime_error("Organic bend vertex was not quantized before rasterization");
    }
}

static bool organicCoverageOracle(float2 sample)
{
    const float2 vertices[8] = {
        {0,0}, {.34375f,.0625f}, {1,0}, {.96875f,.65625f},
        {1,1}, {.34375f,1.0625f}, {0,1}, {-.0625f,.65625f}
    };
    bool inside=false;
    for(int side=0;side<8;++side)
    {
        float2 start=vertices[side];
        float2 end=vertices[(side+1)&7];
        if((start.y>sample.y)!=(end.y>sample.y) &&
            sample.x<start.x+(sample.y-start.y)*(end.x-start.x)/(end.y-start.y))
            inside=!inside;
    }
    return inside;
}

void checkOrganicGeometryCoverage()
{
    float4 xs={0,1,1,0};
    float4 ys={0,0,1,1};
    for(int y=-16;y<80;++y)
    for(int x=-16;x<80;++x)
    {
        float2 sample={(x+.37f)/64.0f-.25f,(y+.19f)/64.0f-.25f};
        bool expected=organicCoverageOracle(sample);
        bool actual=TerrainOrganicGeometryCoverage(sample,xs,ys,110.0f)>0.5f;
        if(actual!=expected)
            throw std::runtime_error(
                "Organic raster coverage differs from independent octagon at "+
                std::to_string(sample.x)+","+std::to_string(sample.y));
    }
}

void checkOrganicSignedDistance()
{
    // Independent eight-vertex contour for packed bends (+2,-1,+2,-2).
    // Midpoints are snapped once to the 1/32-cell geometry grid.
    float2 vertices[8] = {
        {0,0}, {.34375f,.0625f}, {1,0}, {.96875f,.65625f},
        {1,1}, {.34375f,1.0625f}, {0,1}, {-.0625f,.65625f}
    };
    float4 xs={0,1,1,0};
    float4 ys={0,0,1,1};
    for (int y=0; y<96; ++y)
    for (int x=0; x<96; ++x)
    {
        float2 sample{(x+.37f)/64.0f-.25f,(y+.19f)/64.0f-.25f};
        bool inside=false;
        float nearestSquared=1.0e20f;
        float2 expectedNearest=sample;
        for (int side=0; side<8; ++side)
        {
            float2 start=vertices[side];
            float2 end=vertices[(side+1)&7];
            float2 edge=end-start;
            if ((start.y>sample.y)!=(end.y>sample.y) &&
                sample.x<start.x+(sample.y-start.y)*edge.x/edge.y)
                inside=!inside;
            float projection=std::clamp(
                ((sample.x-start.x)*edge.x+(sample.y-start.y)*edge.y)/
                    (edge.x*edge.x+edge.y*edge.y),0.0f,1.0f);
            float2 delta=sample-(start+edge*projection);
            float distanceSquared=delta.x*delta.x+delta.y*delta.y;
            if (distanceSquared<nearestSquared)
            {
                nearestSquared=distanceSquared;
                expectedNearest=start+edge*projection;
            }
        }
        float expected=std::sqrt(nearestSquared)*(inside?1.0f:-1.0f);
        float2 actualNearest;
        float actual=TerrainGeometrySignedDistance(
            sample,xs,ys,110.0f,true,actualNearest);
        if (std::fabs(actual-expected)>1e-4f ||
            length(actualNearest-expectedNearest)>1e-4f)
            throw std::runtime_error("Organic AO distance differs from independent contour");
    }
}

void checkFlatCellDistance()
{
    float4 xs={0,1,1,0};
    float4 ys={0,0,1,1};
    for (int y=-32; y<=96; ++y)
    for (int x=-32; x<=96; ++x)
    {
        float2 sample{(x+.37f)/64.0f,(y+.19f)/64.0f};
        float polygonDistance=TerrainGeometrySignedDistance(sample,xs,ys,0.0f,false);
        float boxDistance=-TerrainSignedDistanceToBox(
            sample,float2{.5f,.5f},float2{.5f,.5f});
        if (std::fabs(polygonDistance-boxDistance)>1e-4f)
            throw std::runtime_error("Flat-cell AO shortcut differs from polygon distance");
    }
}

static void checkDistortedAutotileUvSeam()
{
    // The right edge of the left cell and left edge of the right cell share
    // the same displaced endpoints. Their tile-local UVs must stay on the
    // matching authored edges instead of wrapping into each cell's interior.
    float4 leftX{0.0f, 1.125f, 1.0625f, 0.0625f};
    float4 leftY{0.0f, 0.0f, 1.125f, 1.0f};
    float4 rightX{leftX.y, leftX.y + 1.0f, leftX.z + 1.0f, leftX.z};
    float4 rightY{leftY.y, leftY.y, leftY.z, leftY.z};
    const float edgeT = 0.375f;
    float2 sharedPoint{
        leftX.y + (leftX.z - leftX.y) * edgeT,
        leftY.y + (leftY.z - leftY.y) * edgeT};

    // PackCornerUvs identity mapping: corners (0,0), (1,0), (1,1), (0,1).
    float2 leftUv = TerrainResolveGeometryTileUV(
        float2{0.5f, edgeT}, sharedPoint, leftX, leftY, 180.0f, 1.0f);
    float2 rightUv = TerrainResolveGeometryTileUV(
        float2{0.5f, edgeT}, sharedPoint, rightX, rightY, 180.0f, 1.0f);
    if (std::fabs(leftUv.x - 1.0f) > 1e-4f ||
        std::fabs(rightUv.x) > 1e-4f ||
        std::fabs(leftUv.y - edgeT) > 1e-4f ||
        std::fabs(rightUv.y - edgeT) > 1e-4f)
    {
        throw std::runtime_error(
            "Distorted adjacent autotiles do not meet at their authored UV edges");
    }
}

static void checkAffineGeometryUv()
{
    // Independent forward construction from known UVs; non-axis-aligned,
    // displaced parallelogram corners lie exactly on the production 1/32 grid.
    float2 origin{-0.125f, 0.0625f};
    float2 u{1.0f, 0.125f};
    float2 v{0.0625f, 1.0f};
    float4 xs{origin.x, origin.x+u.x, origin.x+u.x+v.x, origin.x+v.x};
    float4 ys{origin.y, origin.y+u.y, origin.y+u.y+v.y, origin.y+v.y};
    for (int y=-2; y<=34; ++y)
    for (int x=-2; x<=34; ++x)
    {
        float2 known{float(x)/32.0f, float(y)/32.0f};
        float2 point=origin+u*known.x+v*known.y;
        float2 actual=TerrainResolveGeometryLocalUv(point,xs,ys);
        float expectedX=std::clamp(known.x,0.0f,1.0f);
        float expectedY=std::clamp(known.y,0.0f,1.0f);
        if(std::fabs(actual.x-expectedX)>1e-5f || std::fabs(actual.y-expectedY)>1e-5f)
            throw std::runtime_error("Affine terrain UV inversion changed authored coordinates");
    }
}

// Независимая копия TerrainVertex.H: обрезка float до half, 11 значащих
// бит, всё меньше 2^-14 — ноль.
static float truncatedHalf(float value)
{
    if (value == 0.0f) return 0.0f;
    int exponent;
    float mantissa = std::frexp(value, &exponent);
    if (exponent - 1 < -14) return 0.0f;
    return std::ldexp(std::floor(mantissa * 2048.0f) / 2048.0f, exponent);
}

// Фаза k/1000 считается на GPU целыми: обязана дать ровно H(k / 1000f) для
// всего диапазона хэша (k < 6283).
static void checkThousandthsPhase()
{
    for (uint k = 0; k < 6283u; ++k)
    {
        float expected = truncatedHalf((float)k / 1000.0f);
        float actual = TerrainThousandthsAsHalf(k);
        if (actual != expected)
            throw std::runtime_error("phase k/1000 differs at k=" + std::to_string(k) +
                ": " + std::to_string(actual) + " != " + std::to_string(expected));
    }
    for (uint k = 0; k < 65536u; ++k)
    {
        float value = (float)k * (1.0f / 65536.0f);
        if (TerrainTruncateToHalf(value) != truncatedHalf(value))
            throw std::runtime_error("faceted phase truncation differs at " + std::to_string(k));
    }
}

// Миры стенда (TerrainCellEquivalenceHarnessTests.ExportWorldsForHlslShim):
// пёстрые типы, автотайл, стены пака, рельеф, органика, перекрытие фона.
// Настоящий LoadTerrainCellVertex на каждом кваде обязан дать ровно
// атрибуты вершин TerrainQuadBuilder.FillQuad — эталона прежнего вида.
template <typename T> static T readValue(std::ifstream& in)
{
    T value{};
    in.read(reinterpret_cast<char*>(&value), sizeof(T));
    if (!in) throw std::runtime_error("truncated shim world fixture");
    return value;
}
static void expectAttribute(float actual, float expected, const std::string& what)
{
    if (actual != expected)
        throw std::runtime_error(what + ": " + std::to_string(actual) + " != " + std::to_string(expected));
}
struct WorldCell { int x, y; };
static std::vector<WorldCell> shapes;
static void checkWorldShapes(const std::string& path);
static long checkWorld(const std::string& path)
{
    shapes.clear();
    std::ifstream in(path, std::ios::binary);
    if (!in) throw std::runtime_error("cannot open " + path);
    int worldWidth = readValue<int>(in);
    int worldHeight = readValue<int>(in);
    _TerrainDistortionMode = readValue<int>(in);
    for (float4& vector : _TerrainDistortion)
        vector = float4{readValue<float>(in), readValue<float>(in), readValue<float>(in), readValue<float>(in)};
    _TerrainOrganicHorizontalSeed = (int)readValue<uint>(in);
    _TerrainOrganicVerticalSeed = (int)readValue<uint>(in);
    _TerrainGroundDecalRule = (int)readValue<uint>(in);
    _TerrainStoneDecalRule = (int)readValue<uint>(in);
    int originX = readValue<int>(in);
    int originY = readValue<int>(in);
    int ringWidth = readValue<int>(in);
    int ringHeight = readValue<int>(in);
    _TerrainTileDescriptors.reset(64);
    for (uint& word : _TerrainTileDescriptors.data) word = readValue<uint>(in);
    _TerrainTypes.reset(256);
    for (TerrainTypeRow& row : _TerrainTypes.data)
    {
        row.a = uint4{readValue<uint>(in), readValue<uint>(in), readValue<uint>(in), readValue<uint>(in)};
        row.b = uint4{readValue<uint>(in), readValue<uint>(in), readValue<uint>(in), readValue<uint>(in)};
    }
    int cells = ringWidth * ringHeight;
    _TerrainCells.reset((cells + 1) / 2);
    for (int i = 0; i < cells; ++i)
        _TerrainCells.data[i >> 1] |= (uint)readValue<uint16_t>(in) << ((i & 1) * 16);
    _TerrainCellGridSize = {(float)ringWidth, (float)ringHeight, 1, 0};
    _TerrainCellOrigin = {(float)originX, (float)originY, (float)worldHeight, (float)worldWidth};
    _TerrainCellViewOffset = {0, 0, 0, 0};

    float2 corners[] = {{0,0},{1,0},{1,1},{0,1}};
    int quads = readValue<int>(in);
    long compared = 0;
    for (int q = 0; q < quads; ++q)
    {
        int x = readValue<int>(in), y = readValue<int>(in), layer = readValue<int>(in), atlas = readValue<int>(in);
        float expected[4][26];
        for (auto& vertex : expected) for (float& value : vertex) value = readValue<float>(in);
        std::string where = path + " (" + std::to_string(x) + "," + std::to_string(y) + ") layer " + std::to_string(layer);
        for (int corner = 0; corner < 4; ++corner)
        {
            TerrainCellVertex v = LoadTerrainCellVertex(float3{(float)x, (float)y, (float)layer}, corners[corner]);
            expectAttribute(v.atlasIndex, (float)atlas, where + ": atlas slot");
            if (atlas < 0) break;
            const float* e = expected[corner];
            std::string at = where + " corner " + std::to_string(corner);
            const float actual[26] = {
                v.uv.x, v.uv.y,
                v.subAtlasRect.x, v.subAtlasRect.y, v.subAtlasRect.z, v.subAtlasRect.w,
                v.tileSizeUV.x, v.tileSizeUV.y, v.tileSizeUV.z, v.tileSizeUV.w,
                v.worldPos.x, v.worldPos.y, v.worldPos.z, v.worldPos.w,
                v.animData.x, v.animData.y, v.animData.z, v.animData.w,
                v.packedData.x, v.geometryCornersX[corner], v.geometryCornersY[corner], v.packedData.w,
                v.glowData.x, v.glowData.y, v.glowData.z, v.glowData.w,
            };
            static const char* names[26] = {
                "u", "v", "rect x", "rect y", "rect z", "rect w", "tile x", "tile y", "frames", "frame height",
                "world x", "server y", "column", "autotile", "animation", "speed", "phase", "profile",
                "anchored", "corner x", "corner y", "organic", "light colour", "light flags", "contour", "decal",
            };
            for (int i = 0; i < 26; ++i) expectAttribute(actual[i], e[i], at + ": " + names[i]);
            ++compared;
            if (layer == 0 &&
                (v.positionOS.x != x + corners[corner].x || v.positionOS.y != y + corners[corner].y))
                throw std::runtime_error(at + ": background was distorted");

            // Тот же передний план видимым проходом со смещением окна и
            // накладкой дверей (слой 2, адрес в сетке) — та же вершина.
            if (layer == 1)
            {
                _TerrainCellViewOffset = {3, 2, 0, 0};
                TerrainCellVertex visible = LoadTerrainCellVertex(
                    float3{(float)(x - 3), (float)(y - 2), 1.0f}, corners[corner]);
                TerrainCellVertex overlay = LoadTerrainCellVertex(
                    float3{(float)x, (float)y, 2.0f}, corners[corner]);
                _TerrainCellViewOffset = {0, 0, 0, 0};
                for (const TerrainCellVertex* other : {&visible, &overlay})
                {
                    if (other->atlasIndex != v.atlasIndex ||
                        other->positionOS.x != v.positionOS.x || other->positionOS.y != v.positionOS.y ||
                        other->positionOS.z != v.positionOS.z ||
                        other->glowData.z != v.glowData.z || other->uvBits != v.uvBits)
                        throw std::runtime_error(at + ": view offset or door overlay address differs");
                }
            }
        }
        if (layer == 1 && atlas >= 0) shapes.push_back({x, y});
    }
    checkWorldShapes(path);
    return compared;
}
// Растеризация настоящих смещённых клеток мира (классика: прямые рёбра)
// против независимого полуплоскостного оракула, швы с соседями по общим
// узлам и носитель AO. Узлы — из шейдерного правила, не из фикстуры.
static long shapeSamples = 0;
static long seamSamples = 0;
static bool loadQuad(WorldCell cell, TerrainCellVertex* vertices)
{
    static const float2 corners[] = {{0,0},{1,0},{1,1},{0,1}};
    for (int i = 0; i < 4; ++i)
        vertices[i] = LoadTerrainCellVertex(float3{(float)cell.x, (float)cell.y, 1.0f}, corners[i]);
    return vertices[0].atlasIndex >= 0 && vertices[0].packedData.x == 1 && vertices[0].packedData.w == 0;
}
static bool drawn(int x, int y)
{
    for (const WorldCell& cell : shapes) if (cell.x == x && cell.y == y) return true;
    return false;
}
static void checkWorldShapes(const std::string& path)
{
    int rasterised = 0;
    bool paddingChecked = false;
    bool canonicalChecked = false;
    for (const WorldCell& cell : shapes)
    {
        TerrainCellVertex v[4];
        bool straight = loadQuad(cell, v);
        if (!canonicalChecked && v[0].atlasIndex >= 0 && v[0].packedData.x == 0)
        {
            // Несмещённая клетка: расширенный носитель AO вокруг квадрата,
            // силуэт за квадрат не выходит.
            _TerrainCellGridSize.z = 2;
            _TerrainGeometryCarrierPaddingWorld = {.125f, .25f};
            TerrainCellVertex lower = LoadTerrainCellVertex(float3{(float)cell.x, (float)cell.y, 1.0f}, float2{0,0});
            TerrainCellVertex upper = LoadTerrainCellVertex(float3{(float)cell.x, (float)cell.y, 1.0f}, float2{1,1});
            _TerrainGeometryCarrierPaddingWorld = {0, 0};
            _TerrainCellGridSize.z = 1;
            if (std::fabs(lower.packedData.y + .0625f) > 1e-6f || std::fabs(lower.packedData.z + .125f) > 1e-6f ||
                std::fabs(upper.packedData.y - 1.0625f) > 1e-6f || std::fabs(upper.packedData.z - 1.125f) > 1e-6f ||
                oracle(float2{-.01f, .5f}, lower.geometryCornersX, lower.geometryCornersY))
                throw std::runtime_error(path + ": expanded unanchored AO carrier mismatch");
            canonicalChecked = true;
        }
        if (!straight) continue;
        float4 xs = v[0].geometryCornersX, ys = v[0].geometryCornersY;
        // Предпосылка oracle(): четырёхугольник выпуклый.
        for (int i = 0; i < 4; ++i)
        {
            int j = (i + 1) % 4, k = (i + 2) % 4;
            if (cross2(float2{xs[j]-xs[i], ys[j]-ys[i]}, float2{xs[k]-xs[j], ys[k]-ys[j]}) <= 0)
                throw std::runtime_error(path + ": displaced cell is not convex");
        }
        if (!paddingChecked)
        {
            // Носитель AO: на полтексела поля вокруг крайних углов силуэта.
            _TerrainCellGridSize.z = 2;
            _TerrainGeometryCarrierPaddingWorld = {.125f, .25f};
            TerrainCellVertex lower = LoadTerrainCellVertex(float3{(float)cell.x, (float)cell.y, 1.0f}, float2{0,0});
            TerrainCellVertex upper = LoadTerrainCellVertex(float3{(float)cell.x, (float)cell.y, 1.0f}, float2{1,1});
            _TerrainGeometryCarrierPaddingWorld = {0, 0};
            _TerrainCellGridSize.z = 1;
            float minX = std::min({xs.x, xs.y, xs.z, xs.w}), maxX = std::max({xs.x, xs.y, xs.z, xs.w});
            float minY = std::min({ys.x, ys.y, ys.z, ys.w}), maxY = std::max({ys.x, ys.y, ys.z, ys.w});
            if (std::fabs(lower.packedData.y - (minX - .0625f)) > 1e-6f ||
                std::fabs(lower.packedData.z - (minY - .125f)) > 1e-6f ||
                std::fabs(upper.packedData.y - (maxX + .0625f)) > 1e-6f ||
                std::fabs(upper.packedData.z - (maxY + .125f)) > 1e-6f)
                throw std::runtime_error(path + ": AO carrier does not enclose the silhouette with padding");
            paddingChecked = true;
        }
        if (rasterised++ >= 96) continue;
        // Поклеточная растеризация: по обе стороны каждого центра пикселя.
        for (int y = -8; y < 40; ++y) for (int x = -8; x < 40; ++x)
            for (int sy = 0; sy < 4; ++sy) for (int sx = 0; sx < 4; ++sx)
            {
                float2 p = {(float)cell.x + (x + (sx + .5f) / 4) / 32, (float)cell.y + (y + (sy + .5f) / 4) / 32};
                float2 local = p - float2{(float)cell.x, (float)cell.y};
                TerrainCellVertex shifted[4];
                for (int i = 0; i < 4; ++i)
                {
                    shifted[i] = v[i];
                    shifted[i].positionOS.x -= cell.x;
                    shifted[i].positionOS.y -= cell.y;
                }
                if (rendered(local, shifted, xs, ys) != expectedRendered(local, shifted, xs, ys))
                    throw std::runtime_error(path + ": raster coverage differs from polygon oracle at cell " +
                        std::to_string(cell.x) + "," + std::to_string(cell.y));
                ++shapeSamples;
            }
        // Швы с правым и верхним соседом: общие узлы совпадают, и точка у
        // ребра внутри любого из двух силуэтов покрыта хотя бы одной клеткой.
        for (int side = 0; side < 2; ++side)
        {
            WorldCell other = side == 0 ? WorldCell{cell.x + 1, cell.y} : WorldCell{cell.x, cell.y + 1};
            TerrainCellVertex n[4];
            if (!drawn(other.x, other.y) || !loadQuad(other, n)) continue;
            float2 offset = side == 0 ? float2{1, 0} : float2{0, 1};
            float4 nx = n[0].geometryCornersX, ny = n[0].geometryCornersY;
            int ownA = side == 0 ? 1 : 3, ownB = 2;
            int theirA = 0, theirB = side == 0 ? 3 : 1;
            float2 a = float2{xs[ownA], ys[ownA]}, b = float2{xs[ownB], ys[ownB]};
            if (a.x != nx[theirA] + offset.x || a.y != ny[theirA] + offset.y ||
                b.x != nx[theirB] + offset.x || b.y != ny[theirB] + offset.y)
                throw std::runtime_error(path + ": neighbour does not share the edge nodes");
            for (int t = 0; t < 64; ++t)
            {
                float2 edge = a + (b - a) * ((t + .5f) / 64);
                for (int o = -2; o <= 2; ++o)
                {
                    float2 p = edge + offset * (o / 512.0f);
                    float2 q = p - offset;
                    bool inside = oracle(p, xs, ys) || oracle(q, nx, ny);
                    bool covered = TerrainGeometryCoverage(p, xs, ys, 1) > .5f ||
                        TerrainGeometryCoverage(q, nx, ny, 1) > .5f;
                    if (inside && !covered)
                        throw std::runtime_error(path + ": uncovered shared edge");
                    ++seamSamples;
                }
            }
        }
    }
    if (rasterised == 0 && path.find("Classic") != std::string::npos)
        throw std::runtime_error(path + ": classic world has no displaced cells to rasterise");
}

static void checkHarnessWorlds(const char* directory)
{
    if (directory == nullptr) throw std::runtime_error("shim world fixture directory was not passed");
    long compared = 0;
    int worlds = 0;
    for (const auto& entry : std::filesystem::directory_iterator(directory))
    {
        compared += checkWorld(entry.path().string());
        ++worlds;
    }
    if (worlds != 3) throw std::runtime_error("expected 3 shim worlds, found " + std::to_string(worlds));
    resetCells();
    std::cout << "HLSL cell decode matches FillQuad vertices: " << worlds << " worlds, " << compared
        << " vertices; displaced raster " << shapeSamples << " subpixels, seams " << seamSamples << " samples.\n";
}

// Номер угла из меша идентификаторов (TerrainCellIdVertex) обязан дать тот
// же обход квада, что и углы, которыми шим зовёт LoadTerrainCellVertex.
static void checkCornerBase()
{
    const float2 expected[4] = {{0,0},{1,0},{1,1},{0,1}};
    for (int corner = 0; corner < 4; corner++)
    {
        float2 base = TerrainCornerBase((float)corner);
        if (base.x != expected[corner].x || base.y != expected[corner].y)
            throw std::runtime_error("TerrainCornerBase walks the quad in the wrong order");
    }
}

// Чтения на вершину: клетка, восемь соседей, их признаки по разу и строка
// типа (фон — ещё признаки фоновых типов соседей). Число
// печатается, чтобы рост цены вершины был виден, и ограничено сверху.
static void checkBufferReads()
{
    resetCells();
    _TerrainCellOrigin = {5, 5, 100, 100};
    _TerrainTypes.data[1].b.z = 0u;
    bufferReads = 0;
    LoadTerrainCellVertex(float3{0,0,1},float2{0,0});
    long flat = bufferReads;
    _TerrainTypes.data[1].b.z = TypeCause;
    _TerrainDistortionMode = 2;
    bufferReads = 0;
    LoadTerrainCellVertex(float3{0,0,1},float2{0,0});
    long organic = bufferReads;
    bufferReads = 0;
    LoadTerrainCellVertex(float3{0,0,0},float2{0,0});
    long background = bufferReads;
    std::cout << "Terrain cell reads per vertex: flat foreground=" << flat
        << " organic foreground=" << organic << " background=" << background << '\n';
    if (flat > 28 || organic > 28 || background > 28)
        throw std::runtime_error("Terrain cell vertex reads exceed 28");
    resetCells();
}


int runChecks(const char* worldDirectory)
{
    checkAffineGeometryUv();
    checkDistortedAutotileUvSeam();
    checkThousandthsPhase();
    checkHarnessWorlds(worldDirectory);
    checkBufferReads();
    checkCornerBase();
    checkAo();
    checkContinuousFieldSilhouette();
    checkOrganicVerticesUseGeometryGrid();
    checkOrganicGeometryCoverage();
    checkOrganicSignedDistance();
    checkFlatCellDistance();
    checkReliefRim();
    checkAmbientOcclusionFloor();
    std::cout << "HLSL shim displaced autotile UV continuity, geometry quantization, phase and AO geometry edge passed.\n";
    return 0;
}

int main(int argc, char** argv)
{
    try
    {
        return runChecks(argc > 1 ? argv[1] : nullptr);
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
