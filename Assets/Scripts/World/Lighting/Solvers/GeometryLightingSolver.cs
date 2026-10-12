#nullable enable

using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.World.Lighting;

internal sealed class GeometryLightingSolver
{
    private static readonly ProfilerMarker s_aoRecordMarker = new("Kern.Lighting.AmbientOcclusion.Record.CPU");
    private readonly LightingResourceManager _resources;

    public GeometryLightingSolver(LightingResourceManager resources)
    {
        _resources = resources;
    }

    public void RecordMaterialField(
        CommandBuffer commandBuffer,
        Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor terrainGeometry,
        LightingGeometryRegistry geometryRegistry,
        Vector4 worldRect)
    {
        commandBuffer.BeginSample("Kern.Lighting.MaterialField");
        terrainGeometry.RenderMaterialGlowFields(
            commandBuffer,
            new Kern.Core.Interfaces.WorldLighting.LightingMaterialGlowContext(
                _resources.MaterialField!,
                _resources.StaticGlowField!,
                worldRect));
        if (geometryRegistry.HasContributors)
        {
            geometryRegistry.RenderMaterialGlowFields(
                commandBuffer,
                _resources.MaterialField!,
                _resources.StaticGlowField!,
                worldRect,
                clearFields: false);
        }

        commandBuffer.EndSample("Kern.Lighting.MaterialField");
        // End the raster attachments before the cache kernels sample them.
        // Persistent field contents must be stored before compute consumption.
        commandBuffer.SetRenderTarget(BuiltinRenderTextureType.None);
    }

    public void RecordAmbientOcclusionField(
        CommandBuffer commandBuffer,
        Kern.Core.Interfaces.WorldLighting.ILightingGeometryContributor terrainGeometry,
        LightingGeometryRegistry geometryRegistry,
        Vector4 worldRect,
        IReadOnlyList<RectInt>? rasterRects = null)
    {
        using var marker = s_aoRecordMarker.Auto();
        RenderTexture ambientOcclusionField = _resources.AmbientOcclusionField!;
        bool isPartial = rasterRects != null;
        if (isPartial && rasterRects!.Count == 0)
        {
            return;
        }

        RectInt fullRect = new(0, 0, ambientOcclusionField.width, ambientOcclusionField.height);
        if (isPartial)
        {
            for (int i = 0; i < rasterRects!.Count; i++)
            {
                ValidateRasterRect(rasterRects[i], ambientOcclusionField);
            }
        }

        commandBuffer.BeginSample("Kern.Lighting.AmbientOcclusionField");
        commandBuffer.BeginSample(isPartial
            ? "Kern.Lighting.AmbientOcclusionField.Partial"
            : "Kern.Lighting.AmbientOcclusionField.Full");
        commandBuffer.DisableScissorRect();
        commandBuffer.SetRenderTarget(new RenderTargetIdentifier(ambientOcclusionField),
            isPartial ? RenderBufferLoadAction.Load : RenderBufferLoadAction.DontCare,
            RenderBufferStoreAction.Store);
        if (isPartial)
        {
            for (int i = 0; i < rasterRects!.Count; i++)
            {
                RectInt rect = rasterRects[i];
                // Clear by rasterization: ClearRenderTarget ignores scissor on
                // Metal. The loaded attachment preserves pixels outside each rect.
                commandBuffer.SetViewport(new Rect(rect.x, rect.y, rect.width, rect.height));
                commandBuffer.DrawProcedural(Matrix4x4.identity, _resources.AmbientOcclusionClearMaterial,
                    0, MeshTopology.Triangles, 3);
            }
        }
        else
        {
            commandBuffer.ClearRenderTarget(false, true, Color.clear);
            // A full field precedes every regional update. Exercise the
            // regional clear on its real attachment here too: an older state
            // collection may not contain this new shader. Deferring its first
            // draw to digging builds a new graphics pipeline during that action.
            // This pixel is already transparent, so the field is unchanged.
            // Format, sample count, vertex inputs and render state match the
            // regional draw; there is no scratch target or synthetic probe pass.
            commandBuffer.BeginSample("Kern.Lighting.AmbientOcclusionField.PrimeClearPipeline");
            commandBuffer.SetViewport(new Rect(0f, 0f, 1f, 1f));
            commandBuffer.DrawProcedural(Matrix4x4.identity, _resources.AmbientOcclusionClearMaterial,
                0, MeshTopology.Triangles, 3);
            commandBuffer.EndSample("Kern.Lighting.AmbientOcclusionField.PrimeClearPipeline");
        }

        int drawCount = isPartial ? rasterRects!.Count : 1;
        long rasterizedPixels = 0;
        RectInt bounds = isPartial ? rasterRects![0] : fullRect;
        for (int i = 0; i < drawCount; i++)
        {
            RectInt rect = isPartial ? rasterRects![i] : fullRect;
            rasterizedPixels += (long)rect.width * rect.height;
            if (i > 0)
            {
                bounds = Union(bounds, rect);
            }

            commandBuffer.SetViewport(new Rect(0f, 0f, ambientOcclusionField.width, ambientOcclusionField.height));
            commandBuffer.EnableScissorRect(new Rect(rect.x, rect.y, rect.width, rect.height));
            RectInt? contextRect = isPartial ? rect : null;
            terrainGeometry.RenderAmbientOcclusionField(
                commandBuffer,
                new Kern.Core.Interfaces.WorldLighting.LightingAmbientOcclusionContext(
                    ambientOcclusionField,
                    worldRect)
                {
                    RasterRect = contextRect,
                });
            if (geometryRegistry.HasContributors)
            {
                geometryRegistry.RenderAmbientOcclusionField(
                    commandBuffer,
                    ambientOcclusionField,
                    worldRect,
                    clearField: false,
                    rasterRect: contextRect);
            }
        }

        // Visible terrain samples mip zero around the transformed receiver.
        // Keeping exact displaced occupancy avoids carrier-shaped mip halos.
        commandBuffer.DisableScissorRect();
        commandBuffer.SetViewport(new Rect(0f, 0f, ambientOcclusionField.width, ambientOcclusionField.height));
        commandBuffer.EndSample(isPartial
            ? "Kern.Lighting.AmbientOcclusionField.Partial"
            : "Kern.Lighting.AmbientOcclusionField.Full");
        commandBuffer.EndSample("Kern.Lighting.AmbientOcclusionField");
        string clearCost = isPartial
            ? drawCount == 1
                ? $"прямоугольник {bounds.width}×{bounds.height} пикселей"
                : $"{drawCount} прямоугольника, суммарно {rasterizedPixels} пикселей"
            : $"всё поле {(long)ambientOcclusionField.width * ambientOcclusionField.height} пикселей + 1 прогревочный пиксель";
        FrameEventLog.Record($"AO: {(isPartial ? "частично" : "целиком")} " +
            $"границы {bounds.width}×{bounds.height}, обработано {rasterizedPixels} пикселей, " +
            $"поле R8 {ambientOcclusionField.width}×{ambientOcclusionField.height}, " +
            $"draws={drawCount}, очистка: {clearCost}");
    }

    private static void ValidateRasterRect(RectInt rect, RenderTexture field)
    {
        if (rect.x < 0 || rect.y < 0 || rect.width <= 0 || rect.height <= 0 ||
            rect.xMax > field.width || rect.yMax > field.height)
        {
            throw new System.ArgumentOutOfRangeException(
                nameof(rect),
                "AO raster rectangle is outside its target.");
        }
    }

    private static RectInt Union(RectInt left, RectInt right)
    {
        int minX = Mathf.Min(left.xMin, right.xMin);
        int minY = Mathf.Min(left.yMin, right.yMin);
        int maxX = Mathf.Max(left.xMax, right.xMax);
        int maxY = Mathf.Max(left.yMax, right.yMax);
        return new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    public void PrepareCaches(CommandBuffer commandBuffer, bool materialFieldRebuilt)
    {
        if (materialFieldRebuilt)
        {
            _resources.DynamicDistanceFieldValid = false;
        }

        ComputeShader compute = _resources.LightingCompute!;
        RenderTexture cellSolidMask = _resources.CellSolidMask!;
        int buildMaskKernel = _resources.BuildCellSolidMaskKernel;

        commandBuffer.SetComputeIntParams(
            compute,
            LightingComputeBinder.CellGridSizeId,
            _resources.CellGridWidth,
            _resources.CellGridHeight);
        commandBuffer.SetComputeTextureParam(
            compute,
            _resources.SolveCascadeKernel,
            LightingComputeBinder.CellSolidMaskId,
            cellSolidMask);
        commandBuffer.SetComputeBufferParam(
            compute,
            _resources.SolveCascadeKernel,
            LightingComputeBinder.CleanCellPrefixId,
            _resources.CleanCellPrefix!);
        commandBuffer.SetComputeBufferParam(
            compute,
            _resources.SolveDynamicLightingBatchKernel,
            LightingComputeBinder.CleanCellPrefixId,
            _resources.CleanCellPrefix!);
        commandBuffer.SetComputeTextureParam(
            compute,
            _resources.SolveDynamicLightingKernel,
            LightingComputeBinder.CellSolidMaskId,
            cellSolidMask);
        commandBuffer.SetComputeBufferParam(
            compute,
            _resources.SolveDynamicLightingKernel,
            LightingComputeBinder.CleanCellPrefixId,
            _resources.CleanCellPrefix!);
        commandBuffer.SetComputeTextureParam(
            compute,
            _resources.TraceDynamicPolarKernel,
            LightingComputeBinder.CellSolidMaskId,
            cellSolidMask);
        commandBuffer.SetComputeTextureParam(
            compute,
            _resources.ResolveTransmissionDebugKernel,
            LightingComputeBinder.CellSolidMaskId,
            cellSolidMask);

        if (!materialFieldRebuilt && _resources.GeometryCachesValid)
        {
            return;
        }

        commandBuffer.BeginSample("Kern.Lighting.GeometryCaches");
        BindFieldTextures(commandBuffer, compute, buildMaskKernel);
        commandBuffer.SetComputeTextureParam(
            compute,
            buildMaskKernel,
            LightingComputeBinder.CellSolidMaskOutputId,
            cellSolidMask);

        commandBuffer.DispatchCompute(
            compute,
            buildMaskKernel,
            LightingComputeBinder.DispatchGroups(_resources.CellGridWidth),
            LightingComputeBinder.DispatchGroups(_resources.CellGridHeight),
            1);

        // Clean-medium summed-area tables: let the cascade merge prove a
        // probe's child paths are pure air or pure stone with four loads
        // (CascadeTrace.hlsl). The stone proof reads the cell's occupancy.
        int rowsKernel = _resources.BuildCleanCellRowsKernel;
        int columnsKernel = _resources.BuildCleanCellColumnsKernel;
        BindFieldTextures(commandBuffer, compute, rowsKernel);
        commandBuffer.SetComputeTextureParam(compute, rowsKernel, LightingComputeBinder.CellSolidMaskId, cellSolidMask);
        commandBuffer.SetComputeBufferParam(compute, rowsKernel, LightingComputeBinder.CleanCellRowsOutputId,
            _resources.CleanCellRows!);
        commandBuffer.DispatchCompute(compute, rowsKernel, (_resources.CellGridHeight + 63) / 64, 1, 1);
        commandBuffer.SetComputeBufferParam(compute, columnsKernel, LightingComputeBinder.CleanCellRowsId,
            _resources.CleanCellRows!);
        commandBuffer.SetComputeBufferParam(compute, columnsKernel, LightingComputeBinder.CleanCellPrefixOutputId,
            _resources.CleanCellPrefix!);
        commandBuffer.DispatchCompute(compute, columnsKernel, (_resources.CellGridWidth + 63) / 64, 1, 1);

        int buildSurfaceAirKernel = _resources.BuildSurfaceAirCacheKernel;
        BindFieldTextures(commandBuffer, compute, buildSurfaceAirKernel);
        commandBuffer.SetComputeTextureParam(
            compute,
            buildSurfaceAirKernel,
            LightingComputeBinder.CellSolidMaskId,
            cellSolidMask);
        commandBuffer.SetComputeTextureParam(
            compute,
            buildSurfaceAirKernel,
            LightingComputeBinder.SurfaceAirCacheOutputId,
            _resources.SurfaceAirCache!);
        commandBuffer.DispatchCompute(
            compute,
            buildSurfaceAirKernel,
            LightingComputeBinder.DispatchGroups(_resources.LightWidth),
            LightingComputeBinder.DispatchGroups(_resources.LightHeight),
            1);

        commandBuffer.EndSample("Kern.Lighting.GeometryCaches");
        _resources.GeometryCachesValid = true;
    }

    public bool PrepareDynamicDistanceField(CommandBuffer commandBuffer)
    {
        if (LightingQualityTuningController.DynamicTransportMode !=
            DynamicLightingTransportMode.JumpFloodSdfSphereTracing)
        {
            return false;
        }

        _resources.EnsureDynamicDistanceField();
        if (_resources.DynamicDistanceFieldValid)
        {
            return false;
        }

        ComputeShader compute = _resources.LightingCompute!;
        int width = _resources.FieldWidth;
        int height = _resources.FieldHeight;
        int seedKernel = _resources.SeedDynamicDistanceFieldKernel;
        int jumpKernel = _resources.JumpFloodDynamicDistanceFieldKernel;
        int resolveKernel = _resources.ResolveDynamicDistanceFieldKernel;
        commandBuffer.BeginSample("Kern.Lighting.DynamicSdf.JumpFloodBuild");
        commandBuffer.BeginSample("Kern.Lighting.DynamicSdf.Seed");
        BindFieldTextures(commandBuffer, compute, seedKernel);
        commandBuffer.SetComputeBufferParam(
            compute, seedKernel, LightingComputeBinder.DynamicSdfSeedOutputId, _resources.DynamicDistanceSeedsA!);
        commandBuffer.DispatchCompute(compute, seedKernel,
            LightingComputeBinder.DispatchGroups(width), LightingComputeBinder.DispatchGroups(height), 1);
        commandBuffer.EndSample("Kern.Lighting.DynamicSdf.Seed");

        int jumpStep = 1;
        int largestSide = width > height ? width : height;
        while (jumpStep < largestSide)
        {
            jumpStep <<= 1;
        }
        jumpStep >>= 1;
        ComputeBuffer source = _resources.DynamicDistanceSeedsA!;
        ComputeBuffer destination = _resources.DynamicDistanceSeedsB!;
        while (jumpStep >= 1)
        {
            commandBuffer.BeginSample("Kern.Lighting.DynamicSdf.JumpFloodPass");
            commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.DynamicSdfJumpStepId, jumpStep);
            commandBuffer.SetComputeBufferParam(
                compute, jumpKernel, LightingComputeBinder.DynamicSdfSeedInputId, source);
            commandBuffer.SetComputeBufferParam(
                compute, jumpKernel, LightingComputeBinder.DynamicSdfSeedOutputId, destination);
            commandBuffer.DispatchCompute(compute, jumpKernel,
                LightingComputeBinder.DispatchGroups(width), LightingComputeBinder.DispatchGroups(height), 1);
            commandBuffer.EndSample("Kern.Lighting.DynamicSdf.JumpFloodPass");
            (source, destination) = (destination, source);
            jumpStep >>= 1;
        }

        // JFA+1 reduces nearest-seed errors at diagonal and narrow features.
        commandBuffer.BeginSample("Kern.Lighting.DynamicSdf.JumpFloodPass");
        commandBuffer.SetComputeIntParam(compute, LightingComputeBinder.DynamicSdfJumpStepId, 1);
        commandBuffer.SetComputeBufferParam(
            compute, jumpKernel, LightingComputeBinder.DynamicSdfSeedInputId, source);
        commandBuffer.SetComputeBufferParam(
            compute, jumpKernel, LightingComputeBinder.DynamicSdfSeedOutputId, destination);
        commandBuffer.DispatchCompute(compute, jumpKernel,
            LightingComputeBinder.DispatchGroups(width), LightingComputeBinder.DispatchGroups(height), 1);
        commandBuffer.EndSample("Kern.Lighting.DynamicSdf.JumpFloodPass");
        source = destination;

        commandBuffer.BeginSample("Kern.Lighting.DynamicSdf.Resolve");
        commandBuffer.SetComputeBufferParam(
            compute, resolveKernel, LightingComputeBinder.DynamicSdfSeedInputId, source);
        commandBuffer.SetComputeTextureParam(
            compute, resolveKernel, LightingComputeBinder.MaterialFieldId, _resources.MaterialField!);
        commandBuffer.SetComputeTextureParam(
            compute, resolveKernel, LightingComputeBinder.DynamicSdfOutputId, _resources.DynamicDistanceField!);
        commandBuffer.DispatchCompute(compute, resolveKernel,
            LightingComputeBinder.DispatchGroups(width), LightingComputeBinder.DispatchGroups(height), 1);
        commandBuffer.EndSample("Kern.Lighting.DynamicSdf.Resolve");
        commandBuffer.EndSample("Kern.Lighting.DynamicSdf.JumpFloodBuild");
        _resources.DynamicDistanceFieldValid = true;
        return true;
    }

    private void BindFieldTextures(
        CommandBuffer commandBuffer,
        ComputeShader compute,
        int kernel)
    {
        LightingComputeBinder.BindFieldTextures(
            commandBuffer,
            compute,
            kernel,
            _resources.MaterialField!,
            _resources.StaticGlowField!,
            _resources.LightingCounters);
    }
}
