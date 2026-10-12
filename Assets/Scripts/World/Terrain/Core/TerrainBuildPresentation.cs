#nullable enable

using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Interfaces.WorldLighting;
using Kern.Core.Lifecycle;
using MinesServer.Data;
using UnityEngine;

namespace Kern.World.Terrain;

/// <summary>Owns Terrain material bindings and the derived door-overlay presentation.</summary>
internal sealed class TerrainBuildPresentation : IDisposable
{
    private readonly TerrainMaterialManager _materials = new();
    private readonly TerrainDoorOverlayBuilder _doorOverlay = new();

    private Transform? _parent;
    private ISceneObjectFactory? _sceneObjects;
    private string _sortingLayerName = "Default";
    private int _doorOverlaySortingOrder;
    private float _cellSize = 1f;

    public Material[] CellMaterials => _materials.CellMaterials;

    public bool HasMaterials => _materials.CellMaterials.Length > 0;

    public float CellSize => _cellSize;

    public void SetTerrainShader(Shader? shader) => _materials.TerrainShader = shader;

    public void InitializeShader() => _materials.InitializeShader();

    public void ApplyClientConfig(ClientConfig config) => _materials.ApplyClientConfig(config);

    public void BindAtlasTextures(
        IReadOnlyList<IAtlasDescriptor> atlases,
        ITextureService textureService) =>
        _materials.BindAtlasTextures(atlases, textureService);

    public void ValidateLightingBinding(in LightingOutputSnapshot output) =>
        _materials.ValidateLightingBinding(output);

    public void Attach(
        Transform parent,
        ISceneObjectFactory sceneObjects,
        string sortingLayerName,
        int doorOverlaySortingOrder,
        float cellSize)
    {
        _parent = parent;
        _sceneObjects = sceneObjects;
        _sortingLayerName = sortingLayerName;
        _doorOverlaySortingOrder = doorOverlaySortingOrder;
        _cellSize = cellSize;
    }

    private bool EnsureMaterials(
        IReadOnlyList<IAtlasDescriptor> atlases,
        in TerrainBuildContext context,
        IClientConfigManager clientConfigManager,
        TerrainCellCache cellCache)
    {
        bool changed = _materials.EnsureMaterials(
            atlases,
            context.MeshWidth,
            context.MeshHeight,
            clientConfigManager,
            cellCache);
        return changed;
    }

    public bool TryBeginBuild(
        in TerrainBuildContext context,
        IClientConfigManager clientConfigManager,
        TerrainCellCache cellCache,
        out IReadOnlyList<IAtlasDescriptor> atlases,
        out bool materialsChanged)
    {
        atlases = context.TextureService.GetAllAtlases();
        materialsChanged = false;
        if (atlases == null || atlases.Count == 0)
        {
            return false;
        }

        materialsChanged = EnsureMaterials(atlases, context, clientConfigManager, cellCache);
        FlushAtlases(context);
        if (materialsChanged)
        {
            _materials.BindAtlasTextures(atlases, context.TextureService);
        }

        return true;
    }

    public bool TryContinueBuild(
        in TerrainBuildContext context,
        out IReadOnlyList<IAtlasDescriptor> atlases)
    {
        atlases = context.TextureService.GetAllAtlases();
        return atlases != null && CanContinueBuild(atlases);
    }

    public bool CanContinueBuild(IReadOnlyList<IAtlasDescriptor> atlases) =>
        atlases.Count > 0 && _materials.CellMaterials.Length > 0;

    public void Publish(
        IReadOnlyList<IAtlasDescriptor> atlases,
        in TerrainBuildContext context,
        TerrainCPUBuildRequest request,
        TerrainCPUBuildResult result,
        TerrainCellBuilder cellBuilder,
        bool lastBuildScrolled)
    {
        FlushAtlases(context);
        _materials.BindAtlasTextures(atlases, context.TextureService);
        // Адреса накладки заданы в сетке окна: после сдвига окна они
        // указывают на другие клетки и пересобираются вместе с составом.
        if (result.DoorsTouched || lastBuildScrolled)
        {
            RebuildDoorOverlay(cellBuilder, request);
        }
    }

    public void HideDoorOverlay() => _doorOverlay.Hide();

    public void Dispose()
    {
        _doorOverlay.Dispose();
        _materials.CleanupMaterials();
    }

    private void RebuildDoorOverlay(
        TerrainCellBuilder cellBuilder,
        TerrainCPUBuildRequest request)
    {
        if (_parent == null || _sceneObjects == null)
        {
            throw new InvalidOperationException(
                "Terrain door overlay cannot be built before presentation is attached to the scene.");
        }

        _doorOverlay.Rebuild(
            cellBuilder,
            _parent,
            _sceneObjects,
            _materials.CellMaterials,
            _sortingLayerName,
            _doorOverlaySortingOrder,
            request.Size.x,
            request.Size.y,
            _cellSize);
    }

    private static void FlushAtlases(in TerrainBuildContext context)
    {
        long atlasUploadStart = System.Diagnostics.Stopwatch.GetTimestamp();
        context.TextureService.FlushDirtyAtlases();
        context.Telemetry.TerrainAtlasUploadTimeMs += (float)(
            (System.Diagnostics.Stopwatch.GetTimestamp() - atlasUploadStart) *
            1000.0 / System.Diagnostics.Stopwatch.Frequency);
    }
}
