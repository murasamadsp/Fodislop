#ifndef KERN_DISTANCE_FIELD_HLSL
#define KERN_DISTANCE_FIELD_HLSL

// DistanceField: jump-flooded nearest-solid seeds for geometry-true AO.
//
// READS: _MaterialField (mip0 occupancy), _DistanceSeedInput (ping-pong)
// WRITES: _DistanceSeed
// MUST NOT: знать о каскадах, источниках, bounce, DDA
//
// occupancy точна на тексель поля (контур скругления, альфа дырок), и флуд
// превращает её в истинное расстояние до геометрии: круги остаются кругами,
// дырки пробиваются, радиус в клетках одинаков на всех тирах. Это замена
// мип-блюру, который мазал любую форму квадратным ядром и терял мелочь.
// Строится только при перестройке поля, не каждый кадр; террейн читает
// готовое значение из альфы лайтмапы и отдельной выборки не делает.

static const float DistanceSeedFar = 60000.0;

// Порог — общий IsSolidOccupancy из GeometryField.hlsl.

// _JumpStep оголошено в WorldLighting.compute поруч з іншими shared params:
// дубль тут давав redefinition на кожен кернел.

[numthreads(8, 8, 1)]
void SeedDistanceField(uint3 dispatchId : SV_DispatchThreadID)
{
    if (any(dispatchId.xy >= (uint2)_FieldSize))
    {
        return;
    }

    int2 pixel = int2(dispatchId.xy);
    int2 materialPixel = MaterialPixel(pixel);

    float occupancy = _MaterialField.Load(int3(materialPixel, 0)).a;
    // Сиды — в пространстве пикселей compute (без флипа): композит читает
    // тем же пикселем и меряет дистанцию в нём же.
    float2 seed = IsSolidOccupancy(occupancy)
        ? float2(pixel) + 0.5
        : float2(DistanceSeedFar, DistanceSeedFar);
    _DistanceSeed[pixel] = float4(seed, 0.0, 0.0);
}

[numthreads(8, 8, 1)]
void JumpFloodStep(uint3 dispatchId : SV_DispatchThreadID)
{
    if (any(dispatchId.xy >= (uint2)_FieldSize))
    {
        return;
    }

    int2 pixel = int2(dispatchId.xy);
    float2 best = float2(DistanceSeedFar, DistanceSeedFar);
    float bestDistSq = DistanceSeedFar * DistanceSeedFar;
    int step = _JumpStep;
    for (int dy = -1; dy <= 1; dy++)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            int2 samplePixel = pixel + int2(dx, dy) * step;
            // Кламп вместо скипа: семпл за краем дублирует крайний тексель.
            // Стандартный JFA вариант без ветвлений на чтение.
            samplePixel = clamp(samplePixel, int2(0, 0), _FieldSize - 1);
            float2 candidate = _DistanceSeedInput.Load(int3(samplePixel, 0)).rg;
            float2 delta = candidate - (float2(pixel) + 0.5);
            float distSq = dot(delta, delta);
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = candidate;
            }
        }
    }

    _DistanceSeed[pixel] = float4(best, 0.0, 0.0);
}

#endif // KERN_DISTANCE_FIELD_HLSL
