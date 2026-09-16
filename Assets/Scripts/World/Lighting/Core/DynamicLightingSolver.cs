#nullable enable

using System;
using Kern.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

internal sealed class DynamicLightingSolver
{
    // Polar tracing is required for every moved lamp, but one texel-wide ray
    // fan at the edge of a large field creates a quadratic-looking burst:
    // angles * emitter points * ray length. This is a frame-wide budget, not a
    // per-lamp budget: otherwise N visible lamps multiply the supposed cap by
    // N and walking past a busy area creates a burst.
    private const long MaximumPolarRayWorkUnits = 2_000_000;

    private readonly LightingResourceManager _resources;
    private readonly DynamicLightManager _lightManager;
    private readonly DynamicLightTileCache _tileCache;
    private RectInt[] _lampRects = new RectInt[1];
    private DynamicLightTileCache.TileInfo[] _lampTileInfos = new DynamicLightTileCache.TileInfo[1];
    private Vector2Int[] _lampRaySizes = new Vector2Int[1];
    private int[] _lampRequestedRayFans = new int[1];
    private int _previousLampCount;

    public DynamicLightingSolver(
        LightingResourceManager resources,
        DynamicLightManager lightManager,
        DynamicLightTileCache tileCache)
    {
        _resources = resources;
        _lightManager = lightManager;
        _tileCache = tileCache;
    }

    public void InvalidateTiles()
    {
        _tileCache.InvalidateAll();
    }

    public void Release()
    {
        _tileCache.Release();
    }

    public void Record(
        CommandBuffer commandBuffer,
        int lightCount,
        Vector4 worldRect,
        float cellSize,
        bool invalidateLampTiles,
        LightingEngine.DebugView debugView,
        IFrameTelemetry telemetry)
    {
        commandBuffer.BeginSample("Kern.Lighting.DynamicRadiance");

        bool hasPreviousRectUnion = TryGetRectUnion(
            _lampRects,
            _previousLampCount,
            out RectInt previousRectUnion);

        // The transmission debug view reads the static component only.
        if (debugView == LightingEngine.DebugView.Transmission)
        {
            ClearDynamicDirect(commandBuffer);
            _tileCache.InvalidateAll();
            commandBuffer.EndSample("Kern.Lighting.DynamicRadiance");
            return;
        }

        System.ReadOnlySpan<DynamicLightGpuData> lights = _lightManager.UploadedLights;
        System.ReadOnlySpan<int> lightIds = _lightManager.UploadedLightIds;
        int count = Mathf.Min(lightCount, lights.Length);
        if (_lampRects.Length < count)
        {
            Array.Resize(ref _lampRects, count);
            Array.Resize(ref _lampTileInfos, count);
            Array.Resize(ref _lampRaySizes, count);
            Array.Resize(ref _lampRequestedRayFans, count);
        }

        int longestRay = 1;
        long dynamicDispatchPixels = 0;
        long polarRayWorkUnits = 0;

        float minimumExtinction = LightingComputeBinder.ResolveMinimumExtinction();
        float texelsPerWorldX = _resources.FieldWidth / worldRect.z;
        float texelsPerWorldY = _resources.FieldHeight / worldRect.w;
        int widestRect = 1;
        int tallestRect = 1;
        int composeMinX = int.MaxValue;
        int composeMinY = int.MaxValue;
        int composeMaxX = int.MinValue;
        int composeMaxY = int.MinValue;
        for (int lightIndex = 0; lightIndex < count; lightIndex++)
        {
            DynamicLightGpuData light = lights[lightIndex];
            float brightest = Mathf.Max(
                0f,
                Mathf.Max(light.ColorIntensity.x, Mathf.Max(light.ColorIntensity.y, light.ColorIntensity.z)) *
                    light.ColorIntensity.w) * LightingConfigHolder.EmissionScale;
            RectInt rect = default;
            if (brightest > 0f)
            {
                int minX = 0;
                int minY = 0;
                int maxX = _resources.FieldWidth;
                int maxY = _resources.FieldHeight;
                if (minimumExtinction > 0f)
                {
                    float reachCells = Mathf.Max(
                        0f,
                        Mathf.Log(brightest * 1.5f / LightingComputeBinder.InvisibleLampRadiance) /
                            minimumExtinction);
                    float halfExtent = (0.5f + reachCells) * cellSize;
                    // One texel of margin against rounding of the rectangle edge.
                    minX = Mathf.Max(0, Mathf.FloorToInt((light.PositionRadius.x - halfExtent - worldRect.x) * texelsPerWorldX) - 1);
                    minY = Mathf.Max(0, Mathf.FloorToInt((light.PositionRadius.y - halfExtent - worldRect.y) * texelsPerWorldY) - 1);
                    maxX = Mathf.Min(_resources.FieldWidth, Mathf.CeilToInt((light.PositionRadius.x + halfExtent - worldRect.x) * texelsPerWorldX) + 1);
                    maxY = Mathf.Min(_resources.FieldHeight, Mathf.CeilToInt((light.PositionRadius.y + halfExtent - worldRect.y) * texelsPerWorldY) + 1);
                }

                if (maxX > minX && maxY > minY)
                {
                    rect = new RectInt(minX, minY, maxX - minX, maxY - minY);
                    widestRect = Mathf.Max(widestRect, rect.width);
                    tallestRect = Mathf.Max(tallestRect, rect.height);
                    composeMinX = Mathf.Min(composeMinX, rect.xMin);
                    composeMinY = Mathf.Min(composeMinY, rect.yMin);
                    composeMaxX = Mathf.Max(composeMaxX, rect.xMax);
                    composeMaxY = Mathf.Max(composeMaxY, rect.yMax);
                }
            }

            _lampRects[lightIndex] = rect;

            // Lamp-centred rays long enough to reach every corner of the
            // rectangle. Angular density is bounded by the complete polar
            // work budget below; the receiver interpolates between rays.
            Vector2 rayCenter = new(
                (light.PositionRadius.x - worldRect.x) * texelsPerWorldX,
                (light.PositionRadius.y - worldRect.y) * texelsPerWorldY);
            float farthest = 0f;
            if (rect.width > 0)
            {
                farthest = Mathf.Max(
                    Vector2.Distance(rayCenter, new Vector2(rect.xMin, rect.yMin)),
                    Mathf.Max(
                        Vector2.Distance(rayCenter, new Vector2(rect.xMax, rect.yMin)),
                        Mathf.Max(
                            Vector2.Distance(rayCenter, new Vector2(rect.xMin, rect.yMax)),
                            Vector2.Distance(rayCenter, new Vector2(rect.xMax, rect.yMax)))));
            }

            // Fans start at emitter points anywhere in the lamp cell.
            int rayLength = Mathf.CeilToInt(farthest + (texelsPerWorldX + texelsPerWorldY) * cellSize) + 2;
            int requestedRayFan = Mathf.Max(
                1,
                Mathf.CeilToInt(2f * Mathf.PI * rayLength));
            _lampRequestedRayFans[lightIndex] = requestedRayFan;
            _lampRaySizes[lightIndex] = new Vector2Int(1, rayLength);
            if (rect.width > 0)
            {
                longestRay = Mathf.Max(longestRay, rayLength);
            }
        }

        int widestRayFan = AllocatePolarRayFans(count);

        _tileCache.EnsureLayout(widestRect, tallestRect, count);
        if (invalidateLampTiles)
        {
            _tileCache.InvalidateAll();
        }

        if (invalidateLampTiles)
        {
            ClearDynamicDirect(commandBuffer);
        }
        else
        {
            bool hasCurrentRectUnion = composeMaxX > composeMinX && composeMaxY > composeMinY;
            RectInt clearRect = hasPreviousRectUnion
                ? previousRectUnion
                : default;
            if (hasCurrentRectUnion)
            {
                RectInt currentRectUnion = new(
                    composeMinX,
                    composeMinY,
                    composeMaxX - composeMinX,
                    composeMaxY - composeMinY);
                clearRect = hasPreviousRectUnion
                    ? Union(clearRect, currentRectUnion)
                    : currentRectUnion;
            }

            if (clearRect.width > 0 && clearRect.height > 0)
            {
                ClearDynamicDirect(commandBuffer, clearRect);
            }
        }

        _tileCache.AssignSlots(lightIds.Slice(0, count));

        _tileCache.EnsurePolar(widestRayFan, longestRay * LightingComputeBinder.LampEmitterPointCount);

        ComputeShader compute = _resources.LightingCompute!;
        RenderTexture tiles = _tileCache.Tiles!;
        RenderTexture lampRays = _tileCache.Polar!;
        int traceKernel = _resources.SolveDynamicLightingKernel;
        BindFieldTextures(commandBuffer, traceKernel, _resources.StaticEmissionField!);
        commandBuffer.SetComputeBufferParam(compute, traceKernel, LightingComputeBinder.DynamicLightsID, _resources.DynamicLightBuffer!);
        commandBuffer.SetComputeTextureParam(compute, traceKernel, LightingComputeBinder.LampTilesID, tiles);
        commandBuffer.SetComputeTextureParam(compute, traceKernel, LightingComputeBinder.LampPolarInputID, lampRays);
        commandBuffer.SetComputeTextureParam(
            compute,
            traceKernel,
            LightingComputeBinder.DirectTextureID,
            _resources.DirectTexture!);
        int rayKernel = _resources.TraceLampPolarKernel;
        BindFieldTextures(commandBuffer, rayKernel, _resources.StaticEmissionField!);
        commandBuffer.SetComputeTextureParam(compute, rayKernel, LightingComputeBinder.LampPolarID, lampRays);
        commandBuffer.SetComputeBufferParam(compute, rayKernel, LightingComputeBinder.DynamicLightsID, _resources.DynamicLightBuffer!);

        bool singleLightDirectWritten = false;
        for (int lightIndex = 0; lightIndex < count; lightIndex++)
        {
            DynamicLightGpuData light = lights[lightIndex];
            RectInt rect = _lampRects[lightIndex];
            int slot = _tileCache.SlotOf(lightIds[lightIndex]);
            Vector2Int tileOffset = _tileCache.TileOffset(slot);
            _lampTileInfos[lightIndex] = new DynamicLightTileCache.TileInfo(rect, tileOffset);
            if (rect.width <= 0 ||
                !_tileCache.NeedsTrace(slot, light.PositionRadius, light.ColorIntensity, rect))
            {
                continue;
            }

            Vector2Int raySize = _lampRaySizes[lightIndex];
            dynamicDispatchPixels += (long)rect.width * rect.height;
            polarRayWorkUnits += (long)raySize.x *
                LightingComputeBinder.LampEmitterPointCount * raySize.y;
            bool writeDynamicDirect = count == 1;
            commandBuffer.SetComputeIntParam(
                compute,
                LightingComputeBinder.WriteDynamicDirectID,
                writeDynamicDirect ? 1 : 0);
            singleLightDirectWritten |= writeDynamicDirect;
            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.LampPolarSizeID, raySize.x, raySize.y);
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.DynamicLightIndexID, lightIndex);
            for (int point = 0; point < LightingComputeBinder.LampEmitterPointCount; point++)
            {
                commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.LampPolarPointID, point);
                commandBuffer.DispatchCompute(compute, rayKernel, Mathf.CeilToInt(raySize.x / 64f), 1, 1);
            }

            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.DynamicDispatchOriginID, rect.x, rect.y);
            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.DynamicDispatchSizeID, rect.width, rect.height);
            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.LampTileOffsetID, tileOffset.x, tileOffset.y);
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.DynamicLightIndexID, lightIndex);
            commandBuffer.DispatchCompute(
                compute,
                traceKernel,
                LightingComputeBinder.DispatchGroups(rect.width),
                LightingComputeBinder.DispatchGroups(rect.height),
                1);
            _tileCache.MarkTraced(slot, light.PositionRadius, light.ColorIntensity, rect);
            telemetry.LightingLampTraceCount++;
        }

        if (!singleLightDirectWritten && composeMaxX > composeMinX && composeMaxY > composeMinY)
        {
            int composeKernel = _resources.ComposeDynamicLightingKernel;
            ComputeBuffer tileInfos = _tileCache.TileInfos!;
            commandBuffer.SetBufferData(tileInfos, _lampTileInfos, 0, 0, count);
            commandBuffer.SetComputeBufferParam(compute, composeKernel, LightingComputeBinder.LampTileInfosID, tileInfos);
            commandBuffer.SetComputeTextureParam(compute, composeKernel, LightingComputeBinder.LampTilesInputID, tiles);
            commandBuffer.SetComputeTextureParam(compute, composeKernel, LightingComputeBinder.DirectTextureID, _resources.DirectTexture!);
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.LampTileCountID, count);
            int composeWidth = composeMaxX - composeMinX;
            int composeHeight = composeMaxY - composeMinY;
            telemetry.LightingDynamicComposePixels += (long)composeWidth * composeHeight;
            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.ComposeOriginID, composeMinX, composeMinY);
            commandBuffer.SetComputeIntParams(compute, LightingComputeBinder.ComposeSizeID, composeWidth, composeHeight);
            commandBuffer.DispatchCompute(
                compute,
                composeKernel,
                LightingComputeBinder.DispatchGroups(composeWidth),
                LightingComputeBinder.DispatchGroups(composeHeight),
                1);
        }

        telemetry.LightingDynamicDispatchPixels += dynamicDispatchPixels;
        telemetry.LightingPolarRayWorkUnits += polarRayWorkUnits;

        _previousLampCount = count;

        commandBuffer.EndSample("Kern.Lighting.DynamicRadiance");
    }

    private void ClearDynamicDirect(CommandBuffer commandBuffer)
    {
        commandBuffer.SetRenderTarget(_resources.DirectTexture!);
        commandBuffer.ClearRenderTarget(
            clearDepth: false,
            clearColor: true,
            backgroundColor: Color.clear);
    }

    private void ClearDynamicDirect(CommandBuffer commandBuffer, RectInt rect)
    {
        ComputeShader compute = _resources.LightingCompute!;
        int kernel = _resources.ClearDynamicDirectKernel;
        commandBuffer.SetComputeTextureParam(
            compute,
            kernel,
            LightingComputeBinder.DirectTextureID,
            _resources.DirectTexture!);
        commandBuffer.SetComputeIntParams(
            compute,
            LightingComputeBinder.DynamicDispatchOriginID,
            rect.x,
            rect.y);
        commandBuffer.SetComputeIntParams(
            compute,
            LightingComputeBinder.DynamicDispatchSizeID,
            rect.width,
            rect.height);
        commandBuffer.DispatchCompute(
            compute,
            kernel,
            LightingComputeBinder.DispatchGroups(rect.width),
            LightingComputeBinder.DispatchGroups(rect.height),
            1);
    }

    private static bool TryGetRectUnion(
        RectInt[] rects,
        int count,
        out RectInt union)
    {
        union = default;
        bool found = false;
        int boundedCount = Mathf.Min(count, rects.Length);
        for (int index = 0; index < boundedCount; index++)
        {
            RectInt rect = rects[index];
            if (rect.width <= 0 || rect.height <= 0)
            {
                continue;
            }

            union = found ? Union(union, rect) : rect;
            found = true;
        }

        return found;
    }

    private static RectInt Union(RectInt left, RectInt right)
    {
        int minX = Mathf.Min(left.xMin, right.xMin);
        int minY = Mathf.Min(left.yMin, right.yMin);
        int maxX = Mathf.Max(left.xMax, right.xMax);
        int maxY = Mathf.Max(left.yMax, right.yMax);
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    private int AllocatePolarRayFans(int count)
    {
        long requestedWork = 0;
        for (int lightIndex = 0; lightIndex < count; lightIndex++)
        {
            Vector2Int raySize = _lampRaySizes[lightIndex];
            requestedWork += (long)_lampRequestedRayFans[lightIndex] *
                LightingComputeBinder.LampEmitterPointCount *
                Mathf.Max(1, raySize.y);
        }

        long budget = MaximumPolarRayWorkUnits;
        int widestRayFan = 1;
        for (int lightIndex = 0; lightIndex < count; lightIndex++)
        {
            int requested = Mathf.Max(1, _lampRequestedRayFans[lightIndex]);
            int rayLength = Mathf.Max(1, _lampRaySizes[lightIndex].y);
            int rayFan = requested;
            if (requestedWork > budget)
            {
                long weightedBudget = budget * requested;
                long weightedWork = requestedWork;
                rayFan = Mathf.Clamp(
                    (int)(weightedBudget / Mathf.Max(1L, weightedWork)),
                    1,
                    requested);
            }

            _lampRaySizes[lightIndex] = new Vector2Int(rayFan, rayLength);
            widestRayFan = Mathf.Max(widestRayFan, rayFan);
        }

        return widestRayFan;
    }

    private void BindFieldTextures(
        CommandBuffer commandBuffer,
        int kernel,
        RenderTexture emissionField)
    {
        LightingComputeBinder.BindFieldTextures(
            commandBuffer,
            _resources.LightingCompute!,
            kernel,
            _resources.MaterialField!,
            emissionField,
            _resources.LightingCounters);
    }
}
