#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Rendering;
using Fodinae.World.Terrain;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.World.Lighting
{
    [DisallowMultipleComponent]
    public class TerrariaLightingEngine : MonoBehaviour
    {
        public enum QualityPreset
        {
            Low,
            Medium,
            High,
            Ultra,
        }

        public enum DebugView
        {
            Composite,
            Occupancy,
            Albedo,
            Emission,
            Transmittance,
            Direct,
            Bounce,
            AmbientOcclusion,
        }

        private const string ComputeResourcePath = "Shaders/Lighting/WorldLighting";
        private const string QualityPreferenceKey = "WorldLightingQuality";
        public const string AmbientOcclusionPreferenceKey = "WorldLightingAO";
        public const string GlobalIlluminationPreferenceKey = "WorldLightingGI";
        public const string AmbientIntensityPreferenceKey = "WorldLightingAmbientIntensity";
        public const string EmissionScalePreferenceKey = "WorldLightingEmissionScale";
        public const string EmptyExtinctionPreferenceKey = "WorldLightingEmptyExtinction";
        public const string SolidExtinctionPreferenceKey = "WorldLightingSolidExtinction";
        public const string BounceStrengthPreferenceKey = "WorldLightingBounceStrength";
        public const string AmbientOcclusionRadiusPreferenceKey = "WorldLightingAORadius";
        public const string AmbientOcclusionStrengthPreferenceKey = "WorldLightingAOStrength";
        public const string EmitterRadiusPreferenceKey = "WorldLightingEmitterRadius";
        public const string MaximumMultiplierPreferenceKey = "WorldLightingMaximumMultiplier";
        public const string TransmittanceDistancePreferenceKey =
            "WorldLightingTransmittanceDistance";
        private const int LightingCacheAnchorCells = 8;
        private const int LightingRegionSizeQuantum = 32;
        private const int LightingRegionPaddingCells = 16;
        private const int MaximumCascadeDirections = 256;
        private const int DynamicLightStride = sizeof(float) * 8;
        private const int RadianceStride = sizeof(uint) * 3;
        private const int MaximumDispatchGroupsPerDimension = 65535;

        private static readonly int MaterialFieldId = Shader.PropertyToID("_MaterialField");
        private static readonly int EmissionFieldId = Shader.PropertyToID("_EmissionField");
        private static readonly int DynamicLightsId = Shader.PropertyToID("_DynamicLights");
        private static readonly int RadianceAtlasId = Shader.PropertyToID("_RadianceAtlas");
        private static readonly int DirectTextureId = Shader.PropertyToID("_DirectTexture");
        private static readonly int DirectInputId = Shader.PropertyToID("_DirectInput");
        private static readonly int BounceTextureId = Shader.PropertyToID("_BounceTexture");
        private static readonly int BounceInputId = Shader.PropertyToID("_BounceInput");
        private static readonly int ResultId = Shader.PropertyToID("_Result");
        private static readonly int FieldSizeId = Shader.PropertyToID("_FieldSize");
        private static readonly int BounceSizeId = Shader.PropertyToID("_BounceSize");
        private static readonly int WorldRectId = Shader.PropertyToID("_WorldRect");
        private static readonly int AmbientColorId = Shader.PropertyToID("_AmbientColor");
        private static readonly int EmptyExtinctionRgbId = Shader.PropertyToID("_EmptyExtinctionRgb");
        private static readonly int SolidExtinctionRgbId = Shader.PropertyToID("_SolidExtinctionRgb");
        private static readonly int MinimumTransmissionId = Shader.PropertyToID("_MinimumTransmission");
        private static readonly int BounceStrengthId = Shader.PropertyToID("_BounceStrength");
        private static readonly int EmissionScaleId = Shader.PropertyToID("_EmissionScale");
        private static readonly int MaximumLightMultiplierId =
            Shader.PropertyToID("_MaximumLightMultiplier");
        private static readonly int CellSizeId = Shader.PropertyToID("_CellSize");
        private static readonly int AmbientOcclusionRadiusCellsId =
            Shader.PropertyToID("_AmbientOcclusionRadiusCells");
        private static readonly int AmbientOcclusionStrengthId =
            Shader.PropertyToID("_AmbientOcclusionStrength");
        private static readonly int TransmittanceDebugDistanceCellsId =
            Shader.PropertyToID("_TransmittanceDebugDistanceCells");
        private static readonly int DebugViewId = Shader.PropertyToID("_DebugView");
        private static readonly int MaterialYFlipId = Shader.PropertyToID("_MaterialYFlip");
        private static readonly int MaximumIntervalStepsId =
            Shader.PropertyToID("_MaximumIntervalSteps");
        private static readonly int EnableAmbientOcclusionId =
            Shader.PropertyToID("_EnableAmbientOcclusion");
        private static readonly int EnableGlobalIlluminationId =
            Shader.PropertyToID("_EnableGlobalIllumination");
        private static readonly int CascadeOffsetId = Shader.PropertyToID("_CascadeOffset");
        private static readonly int CascadeProbeSizeId = Shader.PropertyToID("_CascadeProbeSize");
        private static readonly int CascadeProbeSpacingId = Shader.PropertyToID("_CascadeProbeSpacing");
        private static readonly int CascadeDirectionCountId = Shader.PropertyToID("_CascadeDirectionCount");
        private static readonly int CascadeIntervalId = Shader.PropertyToID("_CascadeInterval");
        private static readonly int FarCascadeOffsetId = Shader.PropertyToID("_FarCascadeOffset");
        private static readonly int FarCascadeProbeSizeId = Shader.PropertyToID("_FarCascadeProbeSize");
        private static readonly int FarCascadeProbeSpacingId = Shader.PropertyToID("_FarCascadeProbeSpacing");
        private static readonly int FarCascadeDirectionCountId = Shader.PropertyToID("_FarCascadeDirectionCount");
        private static readonly int HasFarCascadeId = Shader.PropertyToID("_HasFarCascade");
        private static readonly int CascadeEntryCountId = Shader.PropertyToID("_CascadeEntryCount");
        private static readonly int CascadeDispatchRowWidthId =
            Shader.PropertyToID("_CascadeDispatchRowWidth");
        private static readonly int WorldLightTextureId = Shader.PropertyToID("_WorldLightTexture");
        private static readonly int WorldLightRectId = Shader.PropertyToID("_WorldLightRect");
        private static readonly int WorldLightDebugViewId =
            Shader.PropertyToID("_WorldLightDebugView");
        private static readonly int WorldLightTextureSizeId =
            Shader.PropertyToID("_WorldLightTextureSize");

        private static readonly string[] RequiredKernels =
        [
            "SolveCascade",
            "ResolveDirect",
            "SolveDiffuseBounce",
            "CompositeLighting",
        ];

        private static TerrariaLightingEngine? _instance;

        [Header("Quality")]
        [SerializeField]
        private QualityPreset _quality = QualityPreset.Ultra;
        [SerializeField]
        private GraphicsQualityProfile? _graphicsProfile;

        [Header("Radiance Cascades")]
        [SerializeField]
        private Color _ambientColor = new(0.045f, 0.05f, 0.065f, 1f);
        [SerializeField]
        [Range(0.0001f, 0.1f)]
        private float _minimumTransmission = 0.008f;
        [SerializeField]
        [Min(0.1f)]
        private float _staticLightRadius = 8f;
        [SerializeField]
        [Min(0)]
        private int _lightSafeBorder = 2;

        [Header("Ambient Occlusion")]
        [SerializeField]
        [Range(0.5f, 8f)]
        private float _ambientOcclusionRadiusCells = 2f;
        [SerializeField]
        [Range(0.1f, 8f)]
        private float _ambientOcclusionStrength = 1.5f;

        [Header("Runtime Lighting Calibration")]
        [SerializeField]
        [Range(0f, 2f)]
        private float _ambientIntensity = 0.9f;
        [SerializeField]
        [Range(0.1f, 16f)]
        private float _emissionScale = 8f;
        [SerializeField]
        [Range(0f, 4f)]
        private float _emptyExtinctionMultiplier = 1f;
        [SerializeField]
        [Range(0.25f, 4f)]
        private float _solidExtinctionMultiplier = 1f;
        [SerializeField]
        [Range(0f, 2f)]
        private float _bounceStrength = 0.3f;
        [SerializeField]
        [Range(0.1f, 2f)]
        private float _dynamicEmitterRadiusCells = 1f;
        [SerializeField]
        [Range(0.5f, 4f)]
        private float _maximumLightMultiplier = 1f;
        [SerializeField]
        [Range(2f, 32f)]
        private float _transmittanceDebugDistanceCells = 10f;

        [Header("Diagnostics")]
        [SerializeField]
        private DebugView _debugView;

        private readonly List<CascadeLayout> _cascades = new();
        private readonly Dictionary<int, DynamicLightSource> _externalLights = new();
        private GraphicsQualitySettings _qualitySettings;
        private ComputeShader? _lightingCompute;
        private Material? _dynamicEmissionMaterial;
        private ComputeBuffer? _dynamicLightBuffer;
        private ComputeBuffer? _radianceAtlas;
        private CommandBuffer? _lightingCommandBuffer;
        private RenderTexture? _materialField;
        private RenderTexture? _staticEmissionField;
        private RenderTexture? _emissionField;
        private RenderTexture? _directTexture;
        private RenderTexture? _bounceTexture;
        private RenderTexture? _lightmapTexture;
        private int _solveCascadeKernel;
        private int _resolveDirectKernel;
        private int _solveDiffuseBounceKernel;
        private int _compositeLightingKernel;
        private int _fieldWidth;
        private int _fieldHeight;
        private int _bounceWidth;
        private int _bounceHeight;
        private int _atlasCapacity;
        private int _atlasEntryCount;
        private bool _fieldDirty = true;
        private ulong _solveCount;
        private ulong _lastTerrainGeometryRevision;
        private Vector4 _lastVisibleRegion = new(float.NaN, float.NaN, float.NaN, float.NaN);

        private Vector2 _playerAuraPosition;
        private float _playerAuraRadius = 12f;
        private float _playerAuraIntensity = 1f;
        private bool _playerAuraEnabled = true;
        private bool _hasRenderedAuraState;
        private bool _lastPlayerAuraEnabled;
        private Vector2 _lastPlayerAuraPosition;
        private float _lastPlayerAuraRadius;
        private float _lastPlayerAuraIntensity;
        private bool _externalLightsDirty;
        private bool _ambientOcclusionEnabled = true;
        private bool _globalIlluminationEnabled = true;
        private DynamicLight[] _dynamicLights = new DynamicLight[1];

        private readonly record struct CascadeLayout(
            int Offset,
            int EntryCount,
            int ProbeWidth,
            int ProbeHeight,
            int ProbeSpacing,
            int DirectionCount,
            float IntervalStart,
            float IntervalEnd);

        private readonly struct DynamicLight
        {
            public readonly Vector4 PositionRadius;
            public readonly Vector4 ColorIntensity;

            public DynamicLight(
                Vector2 position,
                float emitterRadius,
                Color color,
                float intensity)
            {
                PositionRadius = new Vector4(position.x, position.y, emitterRadius, 0f);
                ColorIntensity = new Vector4(color.r, color.g, color.b, intensity);
            }
        }

        private readonly record struct DynamicLightSource(
            Vector2 Position,
            float Radius,
            Color Color,
            float Intensity);

        public static TerrariaLightingEngine? Instance => _instance;

        public QualityPreset Quality => _quality;

        public DebugView ActiveDebugView => _debugView;

        public bool AmbientOcclusionEnabled => _ambientOcclusionEnabled;

        public bool GlobalIlluminationEnabled => _globalIlluminationEnabled;

        public float AmbientIntensity => _ambientIntensity;

        public float EmissionScale => _emissionScale;

        public float EmptyExtinctionMultiplier => _emptyExtinctionMultiplier;

        public float SolidExtinctionMultiplier => _solidExtinctionMultiplier;

        public float BounceStrength => _bounceStrength;

        public float AmbientOcclusionRadiusCells => _ambientOcclusionRadiusCells;

        public float AmbientOcclusionStrength => _ambientOcclusionStrength;

        public float DynamicEmitterRadiusCells => _dynamicEmitterRadiusCells;

        public float MaximumLightMultiplier => _maximumLightMultiplier;

        public float TransmittanceDebugDistanceCells => _transmittanceDebugDistanceCells;

        public ulong SolveCount => _solveCount;

        public int FieldWidth => _fieldWidth;

        public int FieldHeight => _fieldHeight;

        public int AtlasEntryCount => _atlasEntryCount;

        public int StableRegionPaddingCells => LightingRegionPaddingCells;

        public int RequiredTerrainPadding
        {
            get
            {
                float maximumRadius = Mathf.Max(_staticLightRadius, _playerAuraRadius);
                foreach (DynamicLightSource source in _externalLights.Values)
                {
                    maximumRadius = Mathf.Max(maximumRadius, source.Radius);
                }

                int radiusInCells = Mathf.CeilToInt(
                    maximumRadius / GameConstants.World.CELLSIZE);
                return Mathf.Max(1, radiusInCells + _lightSafeBorder);
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                DestroyLightingObject(this);
                return;
            }

            _instance = this;
            _graphicsProfile ??= Resources.Load<GraphicsQualityProfile>("GraphicsQualityProfile");
            _ambientOcclusionEnabled =
                PlayerPrefs.GetInt(AmbientOcclusionPreferenceKey, 1) == 1;
            _globalIlluminationEnabled =
                PlayerPrefs.GetInt(GlobalIlluminationPreferenceKey, 1) == 1;
            LoadRuntimeSettings();
            int savedQuality = PlayerPrefs.GetInt(QualityPreferenceKey, (int)QualityPreset.Ultra);
            ApplyQualityPreset(
                (QualityPreset)Mathf.Clamp(savedQuality, 0, (int)QualityPreset.Ultra),
                save: false);
            LoadComputeShaderOrThrow();
            ValidateGpuRequirements();
            ValidateMaterialFieldPass();
            CreateDynamicEmissionMaterial();
            _lightingCommandBuffer = new CommandBuffer
            {
                name = "Fodinae Radiance Cascades",
            };
        }

        private void OnValidate()
        {
            ApplyQualityPreset(_quality, save: false);
            _fieldDirty = true;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            ReleaseResources();
            _lightingCommandBuffer?.Release();
            _lightingCommandBuffer = null;
            if (_dynamicEmissionMaterial != null)
            {
                DestroyLightingObject(_dynamicEmissionMaterial);
                _dynamicEmissionMaterial = null;
            }
        }

        public void SetPlayerAura(Vector2 position, float radius, float intensity, float height = 2.5f)
        {
            _ = height;
            _playerAuraPosition = position;
            _playerAuraRadius = Mathf.Max(0.1f, radius);
            _playerAuraIntensity = Mathf.Max(0f, intensity);
            _playerAuraEnabled = true;
        }

        public void DisablePlayerAura()
        {
            _playerAuraEnabled = false;
        }

        public void SetDynamicLight(
            int id,
            Vector2 position,
            float radius,
            Color color,
            float intensity)
        {
            var source = new DynamicLightSource(
                position,
                Mathf.Max(0.1f, radius),
                color,
                Mathf.Max(0f, intensity));
            if (_externalLights.TryGetValue(id, out DynamicLightSource previous) &&
                previous == source)
            {
                return;
            }

            _externalLights[id] = source;
            _externalLightsDirty = true;
        }

        public void RemoveDynamicLight(int id)
        {
            if (_externalLights.Remove(id))
            {
                _externalLightsDirty = true;
            }
        }

        public void ClearDynamicLights()
        {
            if (_externalLights.Count == 0)
            {
                return;
            }

            _externalLights.Clear();
            _externalLightsDirty = true;
        }

        public void InvalidateStaticCache()
        {
            _fieldDirty = true;
        }

        public void InvalidateCell(int worldX, int worldY)
        {
            if (float.IsNaN(_lastVisibleRegion.x) ||
                (worldX >= _lastVisibleRegion.x - 1f &&
                worldX <= _lastVisibleRegion.x + _lastVisibleRegion.z + 1f &&
                worldY >= _lastVisibleRegion.y - 1f &&
                worldY <= _lastVisibleRegion.y + _lastVisibleRegion.w + 1f))
            {
                _fieldDirty = true;
            }
        }

        public void SetQuality(QualityPreset quality)
        {
            ApplyQualityPreset(
                (QualityPreset)Mathf.Clamp((int)quality, 0, (int)QualityPreset.Ultra),
                save: true);
        }

        public void SetDebugView(DebugView debugView)
        {
            if (_debugView == debugView)
            {
                return;
            }

            _debugView = debugView;
            _hasRenderedAuraState = false;
        }

        public void SetAmbientOcclusionEnabled(bool enabled)
        {
            if (_ambientOcclusionEnabled == enabled)
            {
                return;
            }

            _ambientOcclusionEnabled = enabled;
            PlayerPrefs.SetInt(AmbientOcclusionPreferenceKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
            _hasRenderedAuraState = false;
        }

        public void SetGlobalIlluminationEnabled(bool enabled)
        {
            if (_globalIlluminationEnabled == enabled)
            {
                return;
            }

            _globalIlluminationEnabled = enabled;
            PlayerPrefs.SetInt(GlobalIlluminationPreferenceKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
            _hasRenderedAuraState = false;
        }

        public void SetAmbientIntensity(float value)
        {
            SetRuntimeSetting(
                ref _ambientIntensity,
                value,
                AmbientIntensityPreferenceKey,
                0f,
                2f);
        }

        public void SetEmissionScale(float value)
        {
            SetRuntimeSetting(
                ref _emissionScale,
                value,
                EmissionScalePreferenceKey,
                0.1f,
                16f);
        }

        public void SetEmptyExtinctionMultiplier(float value)
        {
            SetRuntimeSetting(
                ref _emptyExtinctionMultiplier,
                value,
                EmptyExtinctionPreferenceKey,
                0f,
                4f);
        }

        public void SetSolidExtinctionMultiplier(float value)
        {
            SetRuntimeSetting(
                ref _solidExtinctionMultiplier,
                value,
                SolidExtinctionPreferenceKey,
                0.25f,
                4f);
        }

        public void SetBounceStrength(float value)
        {
            SetRuntimeSetting(
                ref _bounceStrength,
                value,
                BounceStrengthPreferenceKey,
                0f,
                2f);
        }

        public void SetAmbientOcclusionRadius(float value)
        {
            SetRuntimeSetting(
                ref _ambientOcclusionRadiusCells,
                value,
                AmbientOcclusionRadiusPreferenceKey,
                0.5f,
                8f);
        }

        public void SetAmbientOcclusionStrength(float value)
        {
            SetRuntimeSetting(
                ref _ambientOcclusionStrength,
                value,
                AmbientOcclusionStrengthPreferenceKey,
                0.1f,
                8f);
        }

        public void SetDynamicEmitterRadius(float value)
        {
            SetRuntimeSetting(
                ref _dynamicEmitterRadiusCells,
                value,
                EmitterRadiusPreferenceKey,
                0.1f,
                2f);
        }

        public void SetMaximumLightMultiplier(float value)
        {
            SetRuntimeSetting(
                ref _maximumLightMultiplier,
                value,
                MaximumMultiplierPreferenceKey,
                0.5f,
                4f);
        }

        public void SetTransmittanceDebugDistance(float value)
        {
            SetRuntimeSetting(
                ref _transmittanceDebugDistanceCells,
                value,
                TransmittanceDistancePreferenceKey,
                2f,
                32f);
        }

        public void UpdateLighting(
            int visibleMinX,
            int visibleMinY,
            int visibleWidth,
            int visibleHeight,
            IWorldDataStorage? storage,
            MapManager? mapManager)
        {
            if (visibleWidth <= 0 || visibleHeight <= 0 || storage == null || mapManager == null)
            {
                return;
            }

            TerrainRenderer terrainRenderer = TerrainRenderer.Instance ??
                throw new InvalidOperationException("Radiance Cascades requires an active TerrainRenderer.");
            Vector4 lightingRegion = GetStableLightingRegion(
                visibleMinX,
                visibleMinY,
                visibleWidth,
                visibleHeight);
            bool regionChanged = lightingRegion != _lastVisibleRegion;
            _lastVisibleRegion = lightingRegion;

            int gridWidth = Mathf.RoundToInt(lightingRegion.z);
            int gridHeight = Mathf.RoundToInt(lightingRegion.w);
            EnsureResources(gridWidth, gridHeight);

            bool geometryChanged =
                _lastTerrainGeometryRevision != terrainRenderer.LightingGeometryRevision;
            bool auraChanged = HasAuraStateChanged();
            if (!_fieldDirty && !regionChanged && !geometryChanged && !auraChanged)
            {
                PublishLightingGlobals();
                return;
            }

            const float cellSize = GameConstants.World.CELLSIZE;
            Vector4 worldRect = new(
                lightingRegion.x * cellSize,
                lightingRegion.y * cellSize,
                lightingRegion.z * cellSize,
                lightingRegion.w * cellSize);
            CommandBuffer commandBuffer = _lightingCommandBuffer ??
                throw new InvalidOperationException("Radiance Cascades command buffer is not initialized.");
            commandBuffer.Clear();
            try
            {
                commandBuffer.BeginSample("Fodinae.RadianceCascades");
                bool rebuildFields = _fieldDirty || regionChanged || geometryChanged;
                if (rebuildFields)
                {
                    terrainRenderer.RenderLightingMaterialFields(
                        commandBuffer,
                        _materialField!,
                        _staticEmissionField!,
                        worldRect);
                }

                commandBuffer.CopyTexture(
                    _staticEmissionField!,
                    0,
                    0,
                    _emissionField!,
                    0,
                    0);
                int dynamicLightCount = UploadDynamicLights(commandBuffer, worldRect);
                DrawDynamicEmission(commandBuffer, worldRect, dynamicLightCount);
                commandBuffer.GenerateMips(_emissionField!);
                ConfigureSharedComputeParameters(commandBuffer, worldRect, cellSize);
                if (_debugView is DebugView.Occupancy or DebugView.Albedo or DebugView.Emission)
                {
                    DispatchComposite(commandBuffer);
                }
                else if (_debugView == DebugView.Transmittance)
                {
                    DispatchResolveAndBounce(commandBuffer, solveBounce: false);
                }
                else
                {
                    DispatchRadianceCascades(commandBuffer);
                    DispatchResolveAndBounce(commandBuffer, solveBounce: true);
                }

                commandBuffer.EndSample("Fodinae.RadianceCascades");
                Graphics.ExecuteCommandBuffer(commandBuffer);
                PublishLightingGlobals();
                _solveCount++;

                _fieldDirty = false;
                _lastTerrainGeometryRevision = terrainRenderer.LightingGeometryRevision;
                RememberAuraState();
            }
            finally
            {
                commandBuffer.Clear();
            }
        }

        private void PublishLightingGlobals()
        {
            if (_lightmapTexture == null || float.IsNaN(_lastVisibleRegion.x))
            {
                return;
            }

            const float cellSize = GameConstants.World.CELLSIZE;
            Shader.SetGlobalTexture(WorldLightTextureId, _lightmapTexture);
            Shader.SetGlobalInteger(WorldLightDebugViewId, (int)_debugView);
            Shader.SetGlobalVector(
                WorldLightTextureSizeId,
                new Vector4(
                    _lightmapTexture.width,
                    _lightmapTexture.height,
                    1f / _lightmapTexture.width,
                    1f / _lightmapTexture.height));
            Shader.SetGlobalVector(
                WorldLightRectId,
                new Vector4(
                    _lastVisibleRegion.x * cellSize,
                    _lastVisibleRegion.y * cellSize,
                    _lastVisibleRegion.z * cellSize,
                    _lastVisibleRegion.w * cellSize));
        }

        private void ConfigureSharedComputeParameters(
            CommandBuffer commandBuffer,
            Vector4 worldRect,
            float cellSize)
        {
            ComputeShader compute = _lightingCompute!;
            commandBuffer.SetComputeIntParams(compute, FieldSizeId, _fieldWidth, _fieldHeight);
            commandBuffer.SetComputeIntParams(compute, BounceSizeId, _bounceWidth, _bounceHeight);
            commandBuffer.SetComputeVectorParam(compute, WorldRectId, worldRect);
            commandBuffer.SetComputeVectorParam(
                compute,
                AmbientColorId,
                _ambientColor * _ambientIntensity);
            commandBuffer.SetComputeVectorParam(
                compute,
                EmptyExtinctionRgbId,
                _qualitySettings.EmptyExtinctionRgb * _emptyExtinctionMultiplier);
            commandBuffer.SetComputeVectorParam(
                compute,
                SolidExtinctionRgbId,
                _qualitySettings.SolidExtinctionRgb * _solidExtinctionMultiplier);
            commandBuffer.SetComputeFloatParam(compute, MinimumTransmissionId, _minimumTransmission);
            commandBuffer.SetComputeFloatParam(
                compute,
                BounceStrengthId,
                _bounceStrength);
            commandBuffer.SetComputeFloatParam(compute, EmissionScaleId, _emissionScale);
            commandBuffer.SetComputeFloatParam(
                compute,
                MaximumLightMultiplierId,
                _maximumLightMultiplier);
            commandBuffer.SetComputeFloatParam(compute, CellSizeId, cellSize);
            commandBuffer.SetComputeFloatParam(
                compute,
                AmbientOcclusionRadiusCellsId,
                _ambientOcclusionRadiusCells);
            commandBuffer.SetComputeFloatParam(
                compute,
                AmbientOcclusionStrengthId,
                _ambientOcclusionStrength);
            commandBuffer.SetComputeFloatParam(
                compute,
                TransmittanceDebugDistanceCellsId,
                _transmittanceDebugDistanceCells);
            commandBuffer.SetComputeIntParam(compute, DebugViewId, (int)_debugView);
            commandBuffer.SetComputeIntParam(
                compute,
                MaterialYFlipId,
                SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
            commandBuffer.SetComputeIntParam(
                compute,
                MaximumIntervalStepsId,
                Mathf.Clamp(_qualitySettings.LightingMaximumRaySteps, 1, 64));
            commandBuffer.SetComputeIntParam(
                compute,
                EnableAmbientOcclusionId,
                _ambientOcclusionEnabled ? 1 : 0);
            commandBuffer.SetComputeIntParam(
                compute,
                EnableGlobalIlluminationId,
                _globalIlluminationEnabled ? 1 : 0);
            BindFieldTextures(commandBuffer, _solveCascadeKernel);
            BindFieldTextures(commandBuffer, _resolveDirectKernel);
            BindFieldTextures(commandBuffer, _solveDiffuseBounceKernel);
            BindFieldTextures(commandBuffer, _compositeLightingKernel);
        }

        private void BindFieldTextures(CommandBuffer commandBuffer, int kernel)
        {
            commandBuffer.SetComputeTextureParam(
                _lightingCompute!,
                kernel,
                MaterialFieldId,
                _materialField!);
            commandBuffer.SetComputeTextureParam(
                _lightingCompute!,
                kernel,
                EmissionFieldId,
                _emissionField!);
        }

        private int UploadDynamicLights(CommandBuffer commandBuffer, Vector4 worldRect)
        {
            int maximumLightCount = _dynamicLights.Length;
            int dynamicLightCount = 0;
            float emitterRadius =
                _dynamicEmitterRadiusCells * GameConstants.World.CELLSIZE;
            if (_playerAuraEnabled && _playerAuraIntensity > 0f &&
                IntersectsWorldRect(_playerAuraPosition, emitterRadius, worldRect) &&
                dynamicLightCount < maximumLightCount)
            {
                _dynamicLights[dynamicLightCount++] = new DynamicLight(
                    _playerAuraPosition,
                    emitterRadius,
                    Color.white,
                    _playerAuraIntensity);
            }

            foreach (DynamicLightSource source in _externalLights.Values)
            {
                if (dynamicLightCount >= maximumLightCount)
                {
                    break;
                }

                if (source.Intensity <= 0f)
                {
                    continue;
                }

                if (!IntersectsWorldRect(source.Position, emitterRadius, worldRect))
                {
                    continue;
                }

                _dynamicLights[dynamicLightCount++] = new DynamicLight(
                    source.Position,
                    emitterRadius,
                    source.Color,
                    source.Intensity);
            }

            if (dynamicLightCount > 0)
            {
                commandBuffer.SetBufferData(
                    _dynamicLightBuffer!,
                    _dynamicLights,
                    0,
                    0,
                    dynamicLightCount);
            }

            return dynamicLightCount;
        }

        private static bool IntersectsWorldRect(
            Vector2 position,
            float radius,
            Vector4 worldRect)
        {
            return position.x + radius >= worldRect.x &&
                position.x - radius <= worldRect.x + worldRect.z &&
                position.y + radius >= worldRect.y &&
                position.y - radius <= worldRect.y + worldRect.w;
        }

        private void DrawDynamicEmission(
            CommandBuffer commandBuffer,
            Vector4 worldRect,
            int dynamicLightCount)
        {
            if (dynamicLightCount == 0)
            {
                return;
            }

            _dynamicEmissionMaterial!.SetBuffer(DynamicLightsId, _dynamicLightBuffer!);
            Matrix4x4 projection = Matrix4x4.Ortho(
                worldRect.x,
                worldRect.x + worldRect.z,
                worldRect.y,
                worldRect.y + worldRect.w,
                -100f,
                100f);
            commandBuffer.SetViewProjectionMatrices(
                Matrix4x4.identity,
                GL.GetGPUProjectionMatrix(projection, renderIntoTexture: true));
            commandBuffer.SetRenderTarget(_emissionField!);
            commandBuffer.DrawProcedural(
                Matrix4x4.identity,
                _dynamicEmissionMaterial,
                shaderPass: 0,
                MeshTopology.Triangles,
                vertexCount: 6,
                instanceCount: dynamicLightCount);
        }

        private void DispatchRadianceCascades(CommandBuffer commandBuffer)
        {
            ComputeShader compute = _lightingCompute!;
            commandBuffer.SetComputeBufferParam(
                compute,
                _solveCascadeKernel,
                RadianceAtlasId,
                _radianceAtlas!);
            for (int cascadeIndex = _cascades.Count - 1; cascadeIndex >= 0; cascadeIndex--)
            {
                CascadeLayout cascade = _cascades[cascadeIndex];
                bool hasFarCascade = cascadeIndex + 1 < _cascades.Count;
                CascadeLayout farCascade = hasFarCascade
                    ? _cascades[cascadeIndex + 1]
                    : cascade;
                commandBuffer.SetComputeIntParam(compute, CascadeOffsetId, cascade.Offset);
                commandBuffer.SetComputeIntParams(
                    compute,
                    CascadeProbeSizeId,
                    cascade.ProbeWidth,
                    cascade.ProbeHeight);
                commandBuffer.SetComputeIntParam(
                    compute,
                    CascadeProbeSpacingId,
                    cascade.ProbeSpacing);
                commandBuffer.SetComputeIntParam(
                    compute,
                    CascadeDirectionCountId,
                    cascade.DirectionCount);
                commandBuffer.SetComputeVectorParam(
                    compute,
                    CascadeIntervalId,
                    new Vector4(cascade.IntervalStart, cascade.IntervalEnd, 0f, 0f));
                commandBuffer.SetComputeIntParam(compute, FarCascadeOffsetId, farCascade.Offset);
                commandBuffer.SetComputeIntParams(
                    compute,
                    FarCascadeProbeSizeId,
                    farCascade.ProbeWidth,
                    farCascade.ProbeHeight);
                commandBuffer.SetComputeIntParam(
                    compute,
                    FarCascadeProbeSpacingId,
                    farCascade.ProbeSpacing);
                commandBuffer.SetComputeIntParam(
                    compute,
                    FarCascadeDirectionCountId,
                    farCascade.DirectionCount);
                commandBuffer.SetComputeIntParam(compute, HasFarCascadeId, hasFarCascade ? 1 : 0);
                commandBuffer.SetComputeIntParam(
                    compute,
                    CascadeEntryCountId,
                    cascade.EntryCount);
                int totalGroupCount = Mathf.CeilToInt(cascade.EntryCount / 64f);
                int groupCountX = Mathf.Min(
                    MaximumDispatchGroupsPerDimension,
                    totalGroupCount);
                int groupCountY = Mathf.CeilToInt(totalGroupCount / (float)groupCountX);
                commandBuffer.SetComputeIntParam(
                    compute,
                    CascadeDispatchRowWidthId,
                    groupCountX * 64);
                commandBuffer.DispatchCompute(
                    compute,
                    _solveCascadeKernel,
                    groupCountX,
                    groupCountY,
                    1);
            }
        }

        private void DispatchResolveAndBounce(
            CommandBuffer commandBuffer,
            bool solveBounce)
        {
            ComputeShader compute = _lightingCompute!;
            CascadeLayout baseCascade = _cascades[0];
            commandBuffer.SetComputeIntParam(compute, CascadeOffsetId, baseCascade.Offset);
            commandBuffer.SetComputeBufferParam(
                compute,
                _resolveDirectKernel,
                RadianceAtlasId,
                _radianceAtlas!);
            commandBuffer.SetComputeTextureParam(
                compute,
                _resolveDirectKernel,
                DirectTextureId,
                _directTexture!);
            commandBuffer.DispatchCompute(
                compute,
                _resolveDirectKernel,
                Mathf.CeilToInt(_fieldWidth / 8f),
                Mathf.CeilToInt(_fieldHeight / 8f),
                1);

            if (solveBounce && _globalIlluminationEnabled)
            {
                commandBuffer.SetComputeTextureParam(
                    compute,
                    _solveDiffuseBounceKernel,
                    DirectInputId,
                    _directTexture!);
                commandBuffer.SetComputeTextureParam(
                    compute,
                    _solveDiffuseBounceKernel,
                    BounceTextureId,
                    _bounceTexture!);
                commandBuffer.DispatchCompute(
                    compute,
                    _solveDiffuseBounceKernel,
                    Mathf.CeilToInt(_bounceWidth / 8f),
                    Mathf.CeilToInt(_bounceHeight / 8f),
                    1);
            }

            DispatchComposite(commandBuffer);
        }

        private void DispatchComposite(CommandBuffer commandBuffer)
        {
            ComputeShader compute = _lightingCompute!;
            commandBuffer.SetComputeTextureParam(
                compute,
                _compositeLightingKernel,
                DirectInputId,
                _directTexture!);
            commandBuffer.SetComputeTextureParam(
                compute,
                _compositeLightingKernel,
                BounceInputId,
                _bounceTexture!);
            commandBuffer.SetComputeTextureParam(
                compute,
                _compositeLightingKernel,
                ResultId,
                _lightmapTexture!);
            commandBuffer.DispatchCompute(
                compute,
                _compositeLightingKernel,
                Mathf.CeilToInt(_fieldWidth / 8f),
                Mathf.CeilToInt(_fieldHeight / 8f),
                1);
        }

        private bool HasAuraStateChanged()
        {
            if (!_hasRenderedAuraState || _externalLightsDirty)
            {
                return true;
            }

            bool playerAuraActive = _playerAuraEnabled && _playerAuraIntensity > 0f;
            if (playerAuraActive != _lastPlayerAuraEnabled)
            {
                return true;
            }

            return playerAuraActive &&
                (_lastPlayerAuraPosition != _playerAuraPosition ||
                !Mathf.Approximately(_lastPlayerAuraRadius, _playerAuraRadius) ||
                !Mathf.Approximately(_lastPlayerAuraIntensity, _playerAuraIntensity));
        }

        private void LoadRuntimeSettings()
        {
            _ambientIntensity = PlayerPrefs.GetFloat(
                AmbientIntensityPreferenceKey,
                _ambientIntensity);
            _emissionScale = PlayerPrefs.GetFloat(
                EmissionScalePreferenceKey,
                _emissionScale);
            _emptyExtinctionMultiplier = PlayerPrefs.GetFloat(
                EmptyExtinctionPreferenceKey,
                _emptyExtinctionMultiplier);
            _solidExtinctionMultiplier = PlayerPrefs.GetFloat(
                SolidExtinctionPreferenceKey,
                _solidExtinctionMultiplier);
            _bounceStrength = PlayerPrefs.GetFloat(
                BounceStrengthPreferenceKey,
                _bounceStrength);
            _ambientOcclusionRadiusCells = PlayerPrefs.GetFloat(
                AmbientOcclusionRadiusPreferenceKey,
                _ambientOcclusionRadiusCells);
            _ambientOcclusionStrength = PlayerPrefs.GetFloat(
                AmbientOcclusionStrengthPreferenceKey,
                _ambientOcclusionStrength);
            _dynamicEmitterRadiusCells = PlayerPrefs.GetFloat(
                EmitterRadiusPreferenceKey,
                _dynamicEmitterRadiusCells);
            _maximumLightMultiplier = PlayerPrefs.GetFloat(
                MaximumMultiplierPreferenceKey,
                _maximumLightMultiplier);
            _transmittanceDebugDistanceCells = PlayerPrefs.GetFloat(
                TransmittanceDistancePreferenceKey,
                _transmittanceDebugDistanceCells);
        }

        private void SetRuntimeSetting(
            ref float field,
            float value,
            string preferenceKey,
            float minimum,
            float maximum)
        {
            float clampedValue = Mathf.Clamp(value, minimum, maximum);
            if (Mathf.Approximately(field, clampedValue))
            {
                return;
            }

            field = clampedValue;
            PlayerPrefs.SetFloat(preferenceKey, clampedValue);
            PlayerPrefs.Save();
            _hasRenderedAuraState = false;
        }

        private void RememberAuraState()
        {
            _hasRenderedAuraState = true;
            _lastPlayerAuraEnabled = _playerAuraEnabled && _playerAuraIntensity > 0f;
            _lastPlayerAuraPosition = _playerAuraPosition;
            _lastPlayerAuraRadius = _playerAuraRadius;
            _lastPlayerAuraIntensity = _playerAuraIntensity;
            _externalLightsDirty = false;
        }

        private Vector4 GetStableLightingRegion(
            int visibleMinX,
            int visibleMinY,
            int visibleWidth,
            int visibleHeight)
        {
            int visibleMaxX = visibleMinX + visibleWidth;
            int visibleMaxY = visibleMinY + visibleHeight;
            if (!float.IsNaN(_lastVisibleRegion.x))
            {
                int currentMinX = Mathf.RoundToInt(_lastVisibleRegion.x);
                int currentMinY = Mathf.RoundToInt(_lastVisibleRegion.y);
                int currentMaxX = currentMinX + Mathf.RoundToInt(_lastVisibleRegion.z);
                int currentMaxY = currentMinY + Mathf.RoundToInt(_lastVisibleRegion.w);
                int regionWidth = Mathf.RoundToInt(_lastVisibleRegion.z);
                int regionHeight = Mathf.RoundToInt(_lastVisibleRegion.w);
                int quarterRegionSize = Mathf.Min(regionWidth, regionHeight) / 4;
                int safeMargin = Mathf.Min(
                    LightingRegionPaddingCells,
                    Mathf.Max(2, quarterRegionSize));
                if (visibleMinX >= currentMinX + safeMargin &&
                    visibleMaxX <= currentMaxX - safeMargin &&
                    visibleMinY >= currentMinY + safeMargin &&
                    visibleMaxY <= currentMaxY - safeMargin)
                {
                    return _lastVisibleRegion;
                }
            }

            int paddedMinX = SnapLightingRegion(visibleMinX - LightingRegionPaddingCells);
            int paddedMinY = SnapLightingRegion(visibleMinY - LightingRegionPaddingCells);
            int requiredWidth = visibleMaxX + LightingRegionPaddingCells - paddedMinX;
            int requiredHeight = visibleMaxY + LightingRegionPaddingCells - paddedMinY;
            int paddedWidth = Mathf.CeilToInt(
                requiredWidth / (float)LightingRegionSizeQuantum) *
                LightingRegionSizeQuantum;
            int paddedHeight = Mathf.CeilToInt(
                requiredHeight / (float)LightingRegionSizeQuantum) *
                LightingRegionSizeQuantum;
            return new Vector4(
                paddedMinX,
                paddedMinY,
                Mathf.Max(2, paddedWidth),
                Mathf.Max(2, paddedHeight));
        }

        private static int SnapLightingRegion(int coordinate)
        {
            return Mathf.FloorToInt(coordinate / (float)LightingCacheAnchorCells) *
                LightingCacheAnchorCells;
        }

        private void EnsureResources(int gridWidth, int gridHeight)
        {
            float scale = Mathf.Min(
                _qualitySettings.LightingPixelsPerCell,
                Mathf.Min(
                    _qualitySettings.LightingCascadeAtlasLimit / (float)gridWidth,
                    _qualitySettings.LightingCascadeAtlasLimit / (float)gridHeight));
            int fieldWidth = Mathf.Max(1, Mathf.CeilToInt(gridWidth * scale));
            int fieldHeight = Mathf.Max(1, Mathf.CeilToInt(gridHeight * scale));
            FitFieldDimensionsToAtlasBudget(ref fieldWidth, ref fieldHeight);
            if (_fieldWidth >= fieldWidth && _fieldHeight >= fieldHeight &&
                _materialField != null && _radianceAtlas != null)
            {
                return;
            }

            ReleaseFieldTextures();
            _fieldWidth = fieldWidth;
            _fieldHeight = fieldHeight;
            _bounceWidth = Mathf.Max(1, Mathf.CeilToInt(fieldWidth * 0.5f));
            _bounceHeight = Mathf.Max(1, Mathf.CeilToInt(fieldHeight * 0.5f));
            _materialField = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGB32,
                randomWrite: false,
                FilterMode.Bilinear,
                "_LightingMaterialField",
                useMipMap: true);
            _staticEmissionField = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: false,
                FilterMode.Bilinear,
                "_StaticEmissionField",
                useMipMap: false);
            _emissionField = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: false,
                FilterMode.Bilinear,
                "_EmissionField",
                useMipMap: true);
            _directTexture = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_RadianceDirect");
            _bounceTexture = CreateTexture(
                _bounceWidth,
                _bounceHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_RadianceBounce");
            _lightmapTexture = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_WorldLightTexture");

            BuildCascadeLayouts(fieldWidth, fieldHeight);
            _atlasEntryCount = _cascades[^1].Offset + _cascades[^1].EntryCount;
            EnsurePersistentBuffers();
            _fieldDirty = true;
            _hasRenderedAuraState = false;
        }

        private void EnsurePersistentBuffers()
        {
            long atlasDimension = _qualitySettings.LightingCascadeAtlasLimit;
            long maximumCapacity = atlasDimension * atlasDimension * 4;
            if (maximumCapacity <= 0 || maximumCapacity > int.MaxValue)
            {
                throw new InvalidOperationException(
                    "Radiance cascade atlas capacity exceeds the supported structured-buffer size.");
            }

            if (_atlasEntryCount > maximumCapacity)
            {
                throw new InvalidOperationException(
                    "Radiance cascade layout exceeds the configured atlas capacity.");
            }

            int requiredCapacity = Mathf.Max(1, _atlasEntryCount);
            if (_radianceAtlas == null || _atlasCapacity < requiredCapacity)
            {
                _radianceAtlas?.Release();
                _radianceAtlas = new ComputeBuffer(
                    requiredCapacity,
                    RadianceStride,
                    ComputeBufferType.Structured);
                _atlasCapacity = requiredCapacity;
            }

            int maximumLightCount = Mathf.Max(
                1,
                _qualitySettings.LightingMaximumLightCount);
            if (_dynamicLightBuffer == null || _dynamicLightBuffer.count != maximumLightCount)
            {
                _dynamicLightBuffer?.Release();
                _dynamicLightBuffer = new ComputeBuffer(
                    maximumLightCount,
                    DynamicLightStride,
                    ComputeBufferType.Structured);
            }

            if (_dynamicLights.Length != maximumLightCount)
            {
                _dynamicLights = new DynamicLight[maximumLightCount];
            }
        }

        private void FitFieldDimensionsToAtlasBudget(ref int width, ref int height)
        {
            long atlasDimension = _qualitySettings.LightingCascadeAtlasLimit;
            long maximumEntryCount = atlasDimension * atlasDimension * 4;
            for (int iteration = 0; iteration < 8; iteration++)
            {
                long requiredEntryCount = CalculateCascadeEntryCount(width, height);
                if (requiredEntryCount <= maximumEntryCount)
                {
                    return;
                }

                float reduction = Mathf.Sqrt(maximumEntryCount / (float)requiredEntryCount) * 0.98f;
                width = Mathf.Max(1, Mathf.FloorToInt(width * reduction));
                height = Mathf.Max(1, Mathf.FloorToInt(height * reduction));
            }

            throw new InvalidOperationException(
                "Radiance cascade atlas could not be fitted into the configured GPU memory budget.");
        }

        private static long CalculateCascadeEntryCount(int width, int height)
        {
            float requiredDistance = Mathf.Sqrt((width * width) + (height * height));
            long entryCount = 0;
            int spacing = 1;
            int directions = 4;
            float intervalEnd = 1f;
            while (true)
            {
                int probeWidth = Mathf.CeilToInt(width / (float)spacing);
                int probeHeight = Mathf.CeilToInt(height / (float)spacing);
                entryCount += (long)probeWidth * probeHeight * directions;
                if (intervalEnd >= requiredDistance)
                {
                    return entryCount;
                }

                spacing *= 2;
                directions = Mathf.Min(MaximumCascadeDirections, directions * 4);
                intervalEnd *= 4f;
            }
        }

        private void BuildCascadeLayouts(int width, int height)
        {
            _cascades.Clear();
            float requiredDistance = Mathf.Sqrt((width * width) + (height * height));
            int offset = 0;
            int spacing = 1;
            int directions = 4;
            float intervalStart = 0f;
            float intervalEnd = 1f;
            while (true)
            {
                int probeWidth = Mathf.CeilToInt(width / (float)spacing);
                int probeHeight = Mathf.CeilToInt(height / (float)spacing);
                long entryCountLong = (long)probeWidth * probeHeight * directions;
                if (entryCountLong > int.MaxValue - offset)
                {
                    throw new InvalidOperationException("Radiance cascade atlas exceeds the supported buffer size.");
                }

                int entryCount = (int)entryCountLong;
                _cascades.Add(new CascadeLayout(
                    offset,
                    entryCount,
                    probeWidth,
                    probeHeight,
                    spacing,
                    directions,
                    intervalStart,
                    intervalEnd));
                offset += entryCount;
                if (intervalEnd >= requiredDistance)
                {
                    break;
                }

                spacing *= 2;
                directions = Mathf.Min(MaximumCascadeDirections, directions * 4);
                intervalStart = intervalEnd;
                intervalEnd *= 4f;
            }
        }

        private static RenderTexture CreateTexture(
            int width,
            int height,
            RenderTextureFormat format,
            bool randomWrite,
            FilterMode filterMode,
            string name,
            bool useMipMap = false)
        {
            var texture = new RenderTexture(
                width,
                height,
                0,
                format,
                RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = randomWrite,
                useMipMap = useMipMap,
                autoGenerateMips = false,
                filterMode = filterMode,
                wrapMode = TextureWrapMode.Clamp,
                name = name,
            };
            if (!texture.Create())
            {
                DestroyLightingObject(texture);
                throw new InvalidOperationException($"Failed to create required lighting target '{name}'.");
            }

            return texture;
        }

        private void LoadComputeShaderOrThrow()
        {
            if (!SystemInfo.supportsComputeShaders)
            {
                throw new NotSupportedException("Radiance Cascades requires compute shader support.");
            }

            _lightingCompute = Resources.Load<ComputeShader>(ComputeResourcePath) ??
                throw new InvalidOperationException(
                    $"Required compute shader Resources/{ComputeResourcePath} is missing.");
            foreach (string kernelName in RequiredKernels)
            {
                if (!_lightingCompute.HasKernel(kernelName))
                {
                    throw new InvalidOperationException(
                        $"Radiance Cascades compute shader is missing kernel '{kernelName}'.");
                }
            }

            _solveCascadeKernel = _lightingCompute.FindKernel("SolveCascade");
            _resolveDirectKernel = _lightingCompute.FindKernel("ResolveDirect");
            _solveDiffuseBounceKernel = _lightingCompute.FindKernel("SolveDiffuseBounce");
            _compositeLightingKernel = _lightingCompute.FindKernel("CompositeLighting");
            ValidateKernelSupportOrThrow("SolveCascade", _solveCascadeKernel);
            ValidateKernelSupportOrThrow("ResolveDirect", _resolveDirectKernel);
            ValidateKernelSupportOrThrow("SolveDiffuseBounce", _solveDiffuseBounceKernel);
            ValidateKernelSupportOrThrow("CompositeLighting", _compositeLightingKernel);
        }

        private void ValidateKernelSupportOrThrow(string kernelName, int kernelIndex)
        {
            if (_lightingCompute?.IsSupported(kernelIndex) != true)
            {
                throw new InvalidOperationException(
                    $"Radiance Cascades kernel '{kernelName}' failed to compile for {SystemInfo.graphicsDeviceType}.");
            }
        }

        private static void ValidateGpuRequirements()
        {
            if (SystemInfo.supportedRenderTargetCount < 2 ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32) ||
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ||
                !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {
                throw new NotSupportedException(
                    "Radiance Cascades requires two MRTs, RGBA8 material, and random-write RGBA16F lighting targets.");
            }
        }

        private static void ValidateMaterialFieldPass()
        {
            Shader terrainShader = Shader.Find("Universal Render Pipeline/Custom/Terrain") ??
                throw new InvalidOperationException("The terrain shader required by lighting is missing.");
            var validationMaterial = new Material(terrainShader);
            try
            {
                if (validationMaterial.FindPass("LightingMaterialField") < 0)
                {
                    throw new InvalidOperationException(
                        "The terrain shader is missing the LightingMaterialField pass.");
                }
            }
            finally
            {
                DestroyLightingObject(validationMaterial);
            }
        }

        private void CreateDynamicEmissionMaterial()
        {
            Shader dynamicEmissionShader = Shader.Find("Hidden/Fodinae/DynamicEmission") ??
                throw new InvalidOperationException("The dynamic emission shader is missing.");
            _dynamicEmissionMaterial = new Material(dynamicEmissionShader)
            {
                name = "Dynamic Emission Material",
            };
            if (_dynamicEmissionMaterial.FindPass("DynamicEmission") < 0)
            {
                DestroyLightingObject(_dynamicEmissionMaterial);
                _dynamicEmissionMaterial = null;
                throw new InvalidOperationException(
                    "The dynamic emission shader is missing the DynamicEmission pass.");
            }
        }

        private void ApplyQualityPreset(QualityPreset quality, bool save)
        {
            if (_materialField != null)
            {
                ReleaseResources();
            }

            _quality = quality;
            ApplyUnityQualityLevel(quality);
            _qualitySettings = _graphicsProfile != null
                ? _graphicsProfile.Get((GraphicsQualityTier)quality)
                : CreateDefaultQualitySettings(quality);
            ApplyUnityRenderingSettings(_qualitySettings);
            _lastVisibleRegion = new Vector4(float.NaN, float.NaN, float.NaN, float.NaN);
            _fieldDirty = true;
            _hasRenderedAuraState = false;

            if (save)
            {
                PlayerPrefs.SetInt(QualityPreferenceKey, (int)_quality);
                PlayerPrefs.Save();
            }
        }

        private static GraphicsQualitySettings CreateDefaultQualitySettings(QualityPreset quality)
        {
            return quality switch
            {
                QualityPreset.Low => new GraphicsQualitySettings(1, 512, 128, 20, 60f, lightingCascadeAtlasLimit: 512),
                QualityPreset.Medium => new GraphicsQualitySettings(2, 768, 256, 28, 60f, lightingCascadeAtlasLimit: 768),
                QualityPreset.High => new GraphicsQualitySettings(2, 1024, 512, 40, 60f, lightingCascadeAtlasLimit: 1024),
                _ => new GraphicsQualitySettings(4, 1536, 1024, 64, 60f, lightingCascadeAtlasLimit: 1536),
            };
        }

        private static void ApplyUnityQualityLevel(QualityPreset quality)
        {
            string targetName = quality.ToString();
            string[] qualityNames = UnityEngine.QualitySettings.names;
            int qualityIndex = Array.IndexOf(qualityNames, targetName);
            if (qualityIndex >= 0 && UnityEngine.QualitySettings.GetQualityLevel() != qualityIndex)
            {
                UnityEngine.QualitySettings.SetQualityLevel(qualityIndex, applyExpensiveChanges: true);
            }
        }

        private static void ApplyUnityRenderingSettings(GraphicsQualitySettings settings)
        {
            UnityEngine.QualitySettings.vSyncCount = Mathf.Clamp(settings.VSyncCount, 0, 4);
            UnityEngine.QualitySettings.antiAliasing = Mathf.Clamp(settings.AntiAliasing, 0, 8);
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = Mathf.Clamp(settings.RenderScale, 0.5f, 1f);
            }
        }

        private void ReleaseResources()
        {
            _dynamicLightBuffer?.Release();
            _dynamicLightBuffer = null;
            _radianceAtlas?.Release();
            _radianceAtlas = null;
            _atlasCapacity = 0;
            _atlasEntryCount = 0;
            ReleaseFieldTextures();
        }

        private void ReleaseFieldTextures()
        {
            ReleaseTexture(ref _materialField);
            ReleaseTexture(ref _staticEmissionField);
            ReleaseTexture(ref _emissionField);
            ReleaseTexture(ref _directTexture);
            ReleaseTexture(ref _bounceTexture);
            ReleaseTexture(ref _lightmapTexture);
            _fieldWidth = 0;
            _fieldHeight = 0;
            _bounceWidth = 0;
            _bounceHeight = 0;
            _cascades.Clear();
        }

        private static void ReleaseTexture(ref RenderTexture? texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.Release();
            DestroyLightingObject(texture);
            texture = null;
        }

        private static void DestroyLightingObject(UnityEngine.Object target)
        {
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
