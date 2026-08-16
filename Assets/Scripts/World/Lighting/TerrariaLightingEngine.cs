#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using Fodinae.Rendering;
using Fodinae.World.Terrain;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VContainer;

namespace Fodinae.World.Lighting
{
    [DisallowMultipleComponent]
    public class TerrariaLightingEngine : MonoBehaviour
    {
        public enum DebugView
        {
            FinalLighting,
            Occupancy,
            Albedo,
            Emission,
            Transmission,
            DirectRadiance,
            DiffuseBounce,
            ContactOcclusion,
        }

        private const int LightingCacheAnchorCells = 8;
        private const int LightingRegionSizeQuantum = 32;
        private const int LightingRegionPaddingCells = 16;
        private const int MaximumCascadeDirections = 256;
        private const int DynamicLightStride = sizeof(float) * 8;
        private const float DynamicLightPositionEpsilon = 0.00390625f;
        private const int RadianceStride = sizeof(uint) * 3;
        private const int MaximumDispatchGroupsPerDimension = 65535;

        private static readonly int MaterialFieldId = Shader.PropertyToID("_MaterialField");
        private static readonly int EmissionFieldId = Shader.PropertyToID("_EmissionField");
        private static readonly int AutomaticNormalFieldId =
            Shader.PropertyToID("_AutomaticNormalField");
        private static readonly int AutomaticNormalInputId =
            Shader.PropertyToID("_AutomaticNormalInput");
        private static readonly int DynamicLightsId = Shader.PropertyToID("_DynamicLights");
        private static readonly int DynamicEmissionWorldRectId =
            Shader.PropertyToID("_WorldRect");
        private static readonly int DynamicEmissionFieldSizeId =
            Shader.PropertyToID("_FieldSize");
        private static readonly int RadianceAtlasId = Shader.PropertyToID("_RadianceAtlas");
        private static readonly int DirectTextureId = Shader.PropertyToID("_DirectTexture");
        private static readonly int DirectInputId = Shader.PropertyToID("_DirectInput");
        private static readonly int ContactOcclusionTextureId = Shader.PropertyToID("_ContactOcclusionTexture");
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
        private static readonly int EnableFinalLightingClampId =
            Shader.PropertyToID("_EnableFinalLightingClamp");
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
        private static readonly int EnableContactOcclusionId =
            Shader.PropertyToID("_EnableContactOcclusion");
        private static readonly int EnableDiffuseBounceId =
            Shader.PropertyToID("_EnableDiffuseBounce");
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
        private static readonly int WorldEmissionScaleId =
            Shader.PropertyToID("_WorldEmissionScale");
        private static readonly ProfilerMarker LightingUpdateMarker =
            new("Fodinae.Lighting.UpdateLighting.CPU");
        private static readonly ProfilerMarker DynamicUploadMarker =
            new("Fodinae.Lighting.DynamicLights.Upload.CPU");
        private static readonly ProfilerMarker EmissionMarker =
            new("Fodinae.Lighting.Emission.Record.CPU");
        private static readonly ProfilerMarker CascadeMarker =
            new("Fodinae.Lighting.Cascades.Record.CPU");
        private static readonly ProfilerMarker ResolveMarker =
            new("Fodinae.Lighting.Resolve.Record.CPU");
        private static readonly ProfilerMarker CompositeMarker =
            new("Fodinae.Lighting.Composite.Record.CPU");

        private static readonly string[] RequiredKernels =
        [
            "SolveCascade",
            "SolveAutomaticNormals",
            "SolveContactOcclusion",
            "ResolveDirect",
            "SolveDiffuseBounce",
            "CompositeLighting",
        ];

        private static TerrariaLightingEngine? _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            _instance = null;
        }

        [Header("Quality")]

        // Quality is selected by ClientConfig.GraphicsPreset at runtime.
        private GraphicsPreset _graphicsPreset;

        [Header("Radiance Cascades")]

        // Runtime values are loaded from ProjectDefaults and the persisted
        // lighting runtime config. They are intentionally not scene defaults.
        private Color _ambientColor;
        private Color _emptyExtinctionRgb;
        private Color _solidExtinctionRgb;
        private float _minimumTransmission;
        private int _lightSafeBorder;

        [Header("Ambient Occlusion")]
        private bool _ambientOcclusionEnabled;
        private float _ambientOcclusionRadiusCells;
        private float _ambientOcclusionStrength;

        [Header("Runtime Lighting Calibration")]
        private float _ambientIntensity;
        private float _emissionScale;
        private float _emptyExtinctionMultiplier;
        private float _solidExtinctionMultiplier;
        private float _bounceStrength;
        private float _maximumLightMultiplier;
        private bool _enableFinalLightingClamp;
        private float _transmittanceDebugDistanceCells;

        [Header("Diagnostics")]
        [SerializeField]
        [Tooltip("Debug view для проверки отдельных lighting-слоёв без скрытого AO/exposure влияния.")]
        private DebugView _debugView;

        private readonly List<CascadeLayout> _cascades = new();
        private readonly SortedDictionary<int, DynamicLightSource> _externalLights = new();
        private GraphicsQualitySettings _qualitySettings;
        private ComputeShader? _lightingCompute;
        private Material? _dynamicEmissionMaterial;
        private ComputeBuffer? _dynamicLightBuffer;
        private ComputeBuffer? _radianceAtlas;
        private CommandBuffer? _lightingCommandBuffer;
        private RenderTexture? _materialField;
        private RenderTexture? _staticEmissionField;
        private RenderTexture? _emissionField;
        private RenderTexture? _automaticNormalField;
        private RenderTexture? _directTexture;
        private RenderTexture? _ambientOcclusionTexture;
        private RenderTexture? _bounceTexture;
        private RenderTexture? _lightmapTexture;
        private int _solveCascadeKernel;
        private int _solveAutomaticNormalsKernel;
        private int _solveContactOcclusionKernel;
        private int _resolveDirectKernel;
        private int _solveDiffuseBounceKernel;
        private int _compositeLightingKernel;
        private int _fieldWidth;
        private int _fieldHeight;
        private float _requestedPixelsPerCell;
        private float _effectivePixelsPerCell;
        private bool _textureDimensionLimited;
        private bool _cascadeBudgetLimited;
        private int _bounceWidth;
        private int _bounceHeight;
        private int _atlasCapacity;
        private int _atlasEntryCount;
        private bool _fieldDirty = true;
        private bool _ambientOcclusionDirty = true;
        private bool _compositeDirty = true;
        private bool _bounceDirty = true;
        private bool _runtimeConfigSavePending;
        private float _runtimeConfigSaveTime;
        private LightingRuntimeConfig _runtimeConfig = null!;
        private float _nextLightingUpdateTime;
        private float _nextDynamicLightingUpdateTime;
        private ulong _solveCount;
        private ulong _contactOcclusionSolveCount;
        private ulong _lastTerrainGeometryRevision;
        private ulong _lastContributorGeometryRevision;
        [Inject]
        private LightingGeometryRegistry _lightingGeometryRegistry = null!;
        [Inject]
        private IProjectDefaults _projectDefaults = null!;
        [Inject]
        private IClientConfigManager _clientConfig = null!;
        private Vector4 _lastVisibleRegion = new(float.NaN, float.NaN, float.NaN, float.NaN);

        private bool _hasRenderedLightState;
        private bool _externalLightsDirty;
        private uint _externalLightsRevision;
        private bool _initialized;
        private bool _hasStaticRadianceState;
        private uint _dynamicLightGeneration;
        private bool _dynamicSolveInProgress;
        private int _dynamicSolveCascadeIndex;
        private uint _dynamicSolveSourceRevision;
        [Header("Diffuse Bounce")]
        private bool _diffuseBounceEnabled;
        private float _dynamicLightUpdatesPerSecond;
        private DynamicLight[] _dynamicLights = new DynamicLight[1];
        private readonly List<int> _lastDroppedDynamicLightIds = new();
        private int _lastDynamicLightCount;
        private int _lastDroppedDynamicLightCount;
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
                Color color,
                float intensity)
            {
                PositionRadius = new Vector4(position.x, position.y, 0f, 0f);
                ColorIntensity = new Vector4(color.r, color.g, color.b, intensity);
            }
        }

        private readonly record struct DynamicLightSource(
            Vector2 Position,
            Color Color,
            float Intensity);

        public static TerrariaLightingEngine? Instance => _instance;

        public GraphicsPreset ActiveGraphicsPreset => _graphicsPreset;

        public DebugView ActiveDebugView => _debugView;

        public bool AmbientOcclusionEnabled => _ambientOcclusionEnabled;

        public bool DiffuseBounceEnabled => _diffuseBounceEnabled;

        public float AmbientIntensity => _ambientIntensity;

        public Color AmbientColor => _ambientColor;

        public float EmissionScale => _emissionScale;

        public Color EmptyExtinctionRgb => _emptyExtinctionRgb;

        public Color SolidExtinctionRgb => _solidExtinctionRgb;

        public float EmptyExtinctionMultiplier => _emptyExtinctionMultiplier;

        public float SolidExtinctionMultiplier => _solidExtinctionMultiplier;

        public float BounceStrength => _bounceStrength;

        public float AmbientOcclusionRadiusCells => _ambientOcclusionRadiusCells;

        public float AmbientOcclusionStrength => _ambientOcclusionStrength;

        public float MaximumLightMultiplier => _maximumLightMultiplier;

        public float TransmittanceDebugDistanceCells => _transmittanceDebugDistanceCells;

        public float MinimumTransmission => _minimumTransmission;

        public bool EnableFinalLightingClamp => _enableFinalLightingClamp;

        public float DynamicLightIntensity => _runtimeConfig.DynamicLightIntensity;

        public Color DynamicLightColor => _runtimeConfig.DynamicLightColor;

        public float DynamicLightUpdatesPerSecond => _runtimeConfig.DynamicLightUpdatesPerSecond;

        public bool IsRuntimeConfigReady => _runtimeConfig != null;

        public string RuntimeConfigFilePath => _clientConfig.ConfigFilePath;

        public int LightSafeBorder => _lightSafeBorder;

        public int DynamicLightCount => _externalLights.Count;

        public uint DynamicLightGeneration => _dynamicLightGeneration;

        public int UploadedDynamicLightCount => _lastDynamicLightCount;

        public int DroppedDynamicLightCount => _lastDroppedDynamicLightCount;

        public IReadOnlyList<int> DroppedDynamicLightIds => _lastDroppedDynamicLightIds;

        public ulong SolveCount => _solveCount;

        public ulong ContactOcclusionSolveCount => _contactOcclusionSolveCount;

        public int FieldWidth => _fieldWidth;

        public int FieldHeight => _fieldHeight;

        public float RequestedPixelsPerCell => _requestedPixelsPerCell;

        public float EffectivePixelsPerCell => _effectivePixelsPerCell;

        public bool TextureDimensionLimited => _textureDimensionLimited;

        public bool CascadeBudgetLimited => _cascadeBudgetLimited;

        public int BounceWidth => _bounceWidth;

        public int BounceHeight => _bounceHeight;

        public int CascadeCount => _cascades.Count;

        public int MaximumIntervalSteps =>
            Mathf.Clamp(_qualitySettings.LightingMaximumRaySteps, 1, 64);

        public int MaterialYFlip => SystemInfo.graphicsUVStartsAtTop ? 1 : 0;

        public float CellSize => GameConstants.World.CellSize;

        public Vector4 WorldRect => new(
            _lastVisibleRegion.x * GameConstants.World.CellSize,
            _lastVisibleRegion.y * GameConstants.World.CellSize,
            _lastVisibleRegion.z * GameConstants.World.CellSize,
            _lastVisibleRegion.w * GameConstants.World.CellSize);

        public IReadOnlyList<string> GetCascadeUniformSummaries()
        {
            var summaries = new List<string>(_cascades.Count);
            for (int index = 0; index < _cascades.Count; index++)
            {
                CascadeLayout cascade = _cascades[index];
                summaries.Add(
                    $"Cascade {index}: offset={cascade.Offset}, entries={cascade.EntryCount}, " +
                    $"probe={cascade.ProbeWidth}x{cascade.ProbeHeight}, spacing={cascade.ProbeSpacing}, " +
                    $"directions={cascade.DirectionCount}, interval={cascade.IntervalStart:F2}..{cascade.IntervalEnd:F2}");
            }

            return summaries;
        }

        public int AtlasEntryCount => _atlasEntryCount;

        public Color ComputeAmbientColor => _ambientColor * _ambientIntensity;

        public Color ComputeEmptyExtinction =>
            _emptyExtinctionRgb * _emptyExtinctionMultiplier;

        public Color ComputeSolidExtinction =>
            _solidExtinctionRgb * _solidExtinctionMultiplier;

        public int StableRegionPaddingCells => LightingRegionPaddingCells;

        public int RequiredTerrainPadding
        {
            get
            {
                // Dynamic sources are rasterized as one-cell emitters. Their
                // propagation distance is solved by the same extinction and
                // cascade intervals as terrain emission, not by a source halo.
                return Mathf.Max(1, 1 + _lightSafeBorder);
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
        }

        private void Start()
        {
            TryInitialize();
        }

        public void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            if (_projectDefaults == null || _clientConfig == null || _lightingGeometryRegistry == null)
            {
                throw new InvalidOperationException(
                    "TerrariaLightingEngine requires all DI dependencies before initialization.");
            }

            ApplyProjectDefaults(_projectDefaults.Lighting);
            LoadRuntimeConfig();
            ApplyQualitySettings(
                _clientConfig.Config.GraphicsPreset,
                _clientConfig.Config.GraphicsQualitySettings);

            LoadComputeShaderOrThrow();
            ValidateGpuRequirements();
            ValidateMaterialFieldPass();
            CreateDynamicEmissionMaterial();
            _lightingCommandBuffer = new CommandBuffer
            {
                name = "Fodinae Radiance Cascades",
            };
            _initialized = true;
        }

        private void TryInitialize()
        {
            if (_initialized || !ServiceLocator.IsInitialized)
            {
                return;
            }

            EnsureInitialized();
        }

        private void OnDestroy()
        {
            FlushRuntimeConfig();
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

        private void Update()
        {
            if (!_initialized)
            {
                TryInitialize();
                return;
            }

            if (_runtimeConfigSavePending && Time.unscaledTime >= _runtimeConfigSaveTime)
            {
                FlushRuntimeConfig();
            }
        }

        private void OnApplicationQuit()
        {
            FlushRuntimeConfig();
        }

        public void SetDynamicLight(
            int id,
            Vector2 position,
            Color color,
            float intensity)
        {
            var source = new DynamicLightSource(
                position,
                color,
                Mathf.Max(0f, intensity));
            if (_externalLights.TryGetValue(id, out DynamicLightSource previous) &&
                DynamicLightSourceApproximatelyEquals(previous, source))
            {
                return;
            }

            _externalLights[id] = source;
            _externalLightsDirty = true;
            _externalLightsRevision++;
        }

        private static bool DynamicLightSourceApproximatelyEquals(
            DynamicLightSource left,
            DynamicLightSource right)
        {
            return (left.Position - right.Position).sqrMagnitude <=
                DynamicLightPositionEpsilon * DynamicLightPositionEpsilon &&
                left.Color == right.Color &&
                Mathf.Approximately(left.Intensity, right.Intensity);
        }

        public void RemoveDynamicLight(int id)
        {
            if (_externalLights.Remove(id))
            {
                _externalLightsDirty = true;
                _externalLightsRevision++;
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
            _externalLightsRevision++;
            _dynamicLightGeneration++;
        }

        public void InvalidateStaticCache()
        {
            _fieldDirty = true;
        }

        public void InvalidateRegion(int worldX, int worldY, int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            int regionMaxX = worldX + width - 1;
            int regionMaxY = worldY + height - 1;
            if (float.IsNaN(_lastVisibleRegion.x) ||
                (regionMaxX >= _lastVisibleRegion.x - 1f &&
                worldX <= _lastVisibleRegion.x + _lastVisibleRegion.z + 1f &&
                regionMaxY >= _lastVisibleRegion.y - 1f &&
                worldY <= _lastVisibleRegion.y + _lastVisibleRegion.w + 1f))
            {
                _fieldDirty = true;
            }
        }

        public void InvalidateCell(int worldX, int worldY)
        {
            InvalidateRegion(worldX, worldY, 1, 1);
        }

        public void ApplyClientConfig()
        {
            _runtimeConfig = CreateConfigFromClientConfig();
            ApplyRuntimeConfig(_runtimeConfig);
            ApplyQualitySettings(
                _clientConfig.Config.GraphicsPreset,
                _clientConfig.Config.GraphicsQualitySettings);
            _ambientOcclusionDirty = true;
            _bounceDirty = true;
            _compositeDirty = true;
            _dynamicLightGeneration++;
        }

        public void SetDebugView(DebugView debugView)
        {
            if (_debugView == debugView)
            {
                return;
            }

            _debugView = debugView;
            _hasRenderedLightState = false;
            _hasStaticRadianceState = false;
            _compositeDirty = true;
        }

        public void SetAmbientOcclusionEnabled(bool enabled)
        {
            if (_ambientOcclusionEnabled == enabled)
            {
                return;
            }

            _ambientOcclusionEnabled = enabled;
            QueueRuntimeConfigSave();
            if (enabled)
            {
                _ambientOcclusionDirty = true;
            }

            _compositeDirty = true;
        }

        public void SetDiffuseBounceEnabled(bool enabled)
        {
            if (_diffuseBounceEnabled == enabled)
            {
                return;
            }

            _diffuseBounceEnabled = enabled;
            QueueRuntimeConfigSave();
            _bounceDirty = true;
            _compositeDirty = true;
        }

        public void SetAmbientIntensity(float value)
        {
            SetRuntimeSetting(
                ref _ambientIntensity,
                value,
                0f,
                1f,
                radianceDirty: false);
        }

        public void SetAmbientColor(Color value)
        {
            SetRuntimeColor(ref _ambientColor, value, radianceDirty: false);
        }

        public void SetEmissionScale(float value)
        {
            SetRuntimeSetting(
                ref _emissionScale,
                value,
                0.1f,
                8f);
        }

        public void SetEmptyExtinctionColor(Color value)
        {
            SetRuntimeColor(ref _emptyExtinctionRgb, value, radianceDirty: true);
        }

        public void SetSolidExtinctionColor(Color value)
        {
            SetRuntimeColor(ref _solidExtinctionRgb, value, radianceDirty: true);
        }

        public void SetFinalLightingClampEnabled(bool enabled)
        {
            if (_enableFinalLightingClamp == enabled)
            {
                return;
            }

            _enableFinalLightingClamp = enabled;
            QueueRuntimeConfigSave();
            _compositeDirty = true;
        }

        public void SetEmptyExtinctionMultiplier(float value)
        {
            SetRuntimeSetting(
                ref _emptyExtinctionMultiplier,
                value,
                0f,
                2f);
        }

        public void SetSolidExtinctionMultiplier(float value)
        {
            SetRuntimeSetting(
                ref _solidExtinctionMultiplier,
                value,
                0.25f,
                2f);
        }

        public void SetBounceStrength(float value)
        {
            bool changed = SetRuntimeSetting(
                ref _bounceStrength,
                value,
                0f,
                1f,
                radianceDirty: false);
            if (changed)
            {
                _bounceDirty = true;
            }
        }

        public void SetAmbientOcclusionRadius(float value)
        {
            bool changed = SetRuntimeSetting(
                ref _ambientOcclusionRadiusCells,
                value,
                0.5f,
                8f,
                radianceDirty: false);
            if (changed)
            {
                _ambientOcclusionDirty = true;
            }
        }

        public void SetAmbientOcclusionStrength(float value)
        {
            bool changed = SetRuntimeSetting(
                ref _ambientOcclusionStrength,
                value,
                0.1f,
                8f,
                radianceDirty: false);
            if (changed)
            {
                _ambientOcclusionDirty = true;
            }
        }

        public void SetMaximumLightMultiplier(float value)
        {
            SetRuntimeSetting(
                ref _maximumLightMultiplier,
                value,
                0.25f,
                LightingConfigLimits.MaximumLightMultiplier,
                radianceDirty: false);
        }

        public void SetTransmittanceDebugDistance(float value)
        {
            SetRuntimeSetting(
                ref _transmittanceDebugDistanceCells,
                value,
                2f,
                32f,
                radianceDirty: true);
        }

        public void SetMinimumTransmission(float value)
        {
            SetRuntimeSetting(
                ref _minimumTransmission,
                value,
                0.0001f,
                0.1f);
        }

        public void SetLightSafeBorder(float value)
        {
            int border = Mathf.RoundToInt(Mathf.Clamp(value, 0f, 8f));
            if (_lightSafeBorder == border)
            {
                return;
            }

            _lightSafeBorder = border;
            QueueRuntimeConfigSave();
            _fieldDirty = true;
            _hasRenderedLightState = false;
        }

        public void ResetRuntimeLightingPreferences()
        {
            ApplyLightingDefaultsToClientConfig(_projectDefaults.Lighting);
            _runtimeConfig = CreateConfigFromClientConfig();
            ApplyRuntimeConfig(_runtimeConfig);
            SaveRuntimeConfig();
            ApplyQualitySettings(
                _clientConfig.Config.GraphicsPreset,
                _clientConfig.Config.GraphicsQualitySettings);
            _fieldDirty = true;
            _ambientOcclusionDirty = true;
            _compositeDirty = true;
            _bounceDirty = true;
            _hasRenderedLightState = false;
            _hasStaticRadianceState = false;
        }

        public void UpdateLighting(
            int visibleMinX,
            int visibleMinY,
            int visibleWidth,
            int visibleHeight,
            Camera camera,
            IWorldDataStorage? storage,
            MapManager? mapManager)
        {
            using var lightingUpdateMarker = LightingUpdateMarker.Auto();
            if (visibleWidth <= 0 || visibleHeight <= 0 || camera == null ||
                storage == null || mapManager == null)
            {
                return;
            }

            TerrainRenderer terrainRenderer = TerrainRenderer.Instance ??
                throw new InvalidOperationException(
                    "Radiance Cascades requires an active TerrainRenderer.");
            Vector4 lightingRegion = GetStableLightingRegion(
                visibleMinX,
                visibleMinY,
                visibleWidth,
                visibleHeight);
            bool regionChanged = lightingRegion != _lastVisibleRegion;
            _lastVisibleRegion = lightingRegion;

            int gridWidth = Mathf.RoundToInt(lightingRegion.z);
            int gridHeight = Mathf.RoundToInt(lightingRegion.w);
            EnsureResources(gridWidth, gridHeight, camera);

            bool dynamicLightsDirty = HasDynamicLightsChanged();
            ulong contributorGeometryRevision =
                _lightingGeometryRegistry.GeometryRevision;
            bool geometryChanged =
                _lastTerrainGeometryRevision != terrainRenderer.LightingGeometryRevision ||
                _lastContributorGeometryRevision != contributorGeometryRevision;
            bool ambientOcclusionChanged = _ambientOcclusionDirty;
            if (!_fieldDirty && !regionChanged && !dynamicLightsDirty && !geometryChanged &&
                !ambientOcclusionChanged && !_compositeDirty && !_bounceDirty)
            {
                PublishLightingGlobals();
                return;
            }

            bool geometryUpdateRequired = _fieldDirty || regionChanged || geometryChanged;
            bool dynamicOnlyUpdate = dynamicLightsDirty &&
                !geometryUpdateRequired &&
                !ambientOcclusionChanged &&
                !_bounceDirty &&
                !_compositeDirty;
            bool continueDynamicSolve = _dynamicSolveInProgress &&
                !geometryUpdateRequired &&
                !ambientOcclusionChanged &&
                !_bounceDirty &&
                !_compositeDirty;
            float nextAllowedUpdateTime = dynamicOnlyUpdate
                ? _nextDynamicLightingUpdateTime
                : _nextLightingUpdateTime;
            if (!continueDynamicSolve &&
                Time.unscaledTime < nextAllowedUpdateTime &&
                !geometryUpdateRequired &&
                !ambientOcclusionChanged &&
                !_compositeDirty &&
                !_bounceDirty &&
                _hasStaticRadianceState)
            {
                PublishLightingGlobals();
                return;
            }

            if (geometryUpdateRequired || ambientOcclusionChanged || _bounceDirty || _compositeDirty)
            {
                _dynamicSolveInProgress = false;
            }

            const float cellSize = GameConstants.World.CellSize;
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
                    commandBuffer.BeginSample("Fodinae.Lighting.MaterialField");
                    terrainRenderer.RenderLightingMaterialFields(
                        commandBuffer,
                        _materialField!,
                        _staticEmissionField!,
                        worldRect);
                    if (_lightingGeometryRegistry.HasContributors)
                    {
                        _lightingGeometryRegistry.RenderLightingFields(
                            commandBuffer,
                            _materialField!,
                            _staticEmissionField!,
                            worldRect,
                            clearFields: false);
                    }
                    else
                    {
                        commandBuffer.GenerateMips(_materialField!);
                    }

                    commandBuffer.EndSample("Fodinae.Lighting.MaterialField");
                }

                int dynamicLightCount;
                bool dynamicLightsChanged;
                if (_dynamicSolveInProgress)
                {
                    dynamicLightCount = _lastDynamicLightCount;
                    dynamicLightsChanged = false;
                }
                else
                {
                    dynamicLightCount = UploadDynamicLights(
                        commandBuffer,
                        worldRect,
                        cellSize,
                        out dynamicLightsChanged);
                }

                bool dynamicRadianceView = _debugView is
                    DebugView.FinalLighting or
                    DebugView.DirectRadiance or
                    DebugView.DiffuseBounce;
                bool phasedDynamicSolve = !rebuildFields &&
                    dynamicRadianceView &&
                    (continueDynamicSolve || dynamicOnlyUpdate);
                if (phasedDynamicSolve)
                {
                    if (!_dynamicSolveInProgress)
                    {
                        PrepareEmissionField(
                            commandBuffer,
                            worldRect,
                            dynamicLightCount,
                            dynamicLightsChanged);
                        _dynamicSolveInProgress = true;
                        _dynamicSolveCascadeIndex = _cascades.Count - 1;
                        _dynamicSolveSourceRevision = _externalLightsRevision;
                    }

                    var dynamicEmissionField = _lastDynamicLightCount > 0
                        ? _emissionField!
                        : _staticEmissionField!;
                    ConfigureSharedComputeParameters(
                        commandBuffer,
                        worldRect,
                        cellSize,
                        dynamicEmissionField);
                    DispatchRadianceCascade(
                        commandBuffer,
                        _dynamicSolveCascadeIndex);
                    _dynamicSolveCascadeIndex--;
                    if (_dynamicSolveCascadeIndex >= 0)
                    {
                        commandBuffer.EndSample("Fodinae.RadianceCascades");
                        Graphics.ExecuteCommandBuffer(commandBuffer);
                        return;
                    }

                    bool solveBounce = _debugView is
                        DebugView.FinalLighting or
                        DebugView.DiffuseBounce;
                    DispatchResolveAndBounce(
                        commandBuffer,
                        solveBounce: solveBounce,
                        composite: false);
                    DispatchComposite(commandBuffer);
                    _dynamicSolveInProgress = false;
                    commandBuffer.EndSample("Fodinae.RadianceCascades");
                    Graphics.ExecuteCommandBuffer(commandBuffer);
                    PublishLightingGlobals();
                    _solveCount++;
                    _fieldDirty = false;
                    _ambientOcclusionDirty = false;
                    _compositeDirty = false;
                    _bounceDirty = false;
                    _hasStaticRadianceState = true;
                    _nextLightingUpdateTime = Time.unscaledTime +
                        (1f / Mathf.Max(_qualitySettings.LightingUpdatesPerSecond, 1f));
                    _nextDynamicLightingUpdateTime = Time.unscaledTime +
                        (1f / Mathf.Max(_dynamicLightUpdatesPerSecond, 1f));
                    _lastTerrainGeometryRevision = terrainRenderer.LightingGeometryRevision;
                    _lastContributorGeometryRevision = contributorGeometryRevision;
                    if (_externalLightsRevision == _dynamicSolveSourceRevision)
                    {
                        RememberDynamicLightState();
                    }

                    return;
                }

                if (!rebuildFields && !dynamicLightsChanged &&
                    !ambientOcclusionChanged && !_compositeDirty && !_bounceDirty)
                {
                    commandBuffer.EndSample("Fodinae.RadianceCascades");
                    RememberDynamicLightState();
                    return;
                }

                RenderTexture emissionField = _lastDynamicLightCount > 0
                    ? _emissionField!
                    : _staticEmissionField!;
                if (rebuildFields || dynamicLightsChanged)
                {
                    PrepareEmissionField(
                        commandBuffer,
                        worldRect,
                        dynamicLightCount,
                        dynamicLightsChanged);
                    emissionField = dynamicLightCount > 0
                        ? _emissionField!
                        : _staticEmissionField!;
                }

                ConfigureSharedComputeParameters(
                    commandBuffer,
                    worldRect,
                    cellSize,
                    emissionField);
                if (rebuildFields)
                {
                    DispatchAutomaticNormals(commandBuffer);
                }

                if (ShouldDispatchContactOcclusion(
                    _ambientOcclusionEnabled,
                    rebuildFields,
                    _ambientOcclusionDirty))
                {
                    DispatchContactOcclusion(commandBuffer);
                }

                bool staticRadianceChanged = rebuildFields || dynamicLightsChanged ||
                    !_hasStaticRadianceState;
                if (_debugView is DebugView.Occupancy or DebugView.Albedo or DebugView.Emission)
                {
                    DispatchComposite(commandBuffer);
                }
                else
                {
                    if (staticRadianceChanged || _bounceDirty)
                    {
                        bool needsCascade = _debugView is
                            DebugView.FinalLighting or
                            DebugView.DirectRadiance or
                            DebugView.DiffuseBounce;
                        bool needsDirect = needsCascade ||
                            _debugView == DebugView.Transmission;
                        bool needsBounce = _debugView is
                            DebugView.FinalLighting or
                            DebugView.DiffuseBounce;
                        if (needsCascade && staticRadianceChanged)
                        {
                            DispatchRadianceCascades(commandBuffer);
                        }

                        if (needsDirect)
                        {
                            DispatchResolveAndBounce(
                                commandBuffer,
                                solveBounce: needsBounce,
                                composite: false);
                        }

                        _hasStaticRadianceState = true;
                    }

                    DispatchComposite(commandBuffer);
                }

                commandBuffer.EndSample("Fodinae.RadianceCascades");
                Graphics.ExecuteCommandBuffer(commandBuffer);
                PublishLightingGlobals();
                _solveCount++;

                _fieldDirty = false;
                _ambientOcclusionDirty = false;
                _compositeDirty = false;
                _bounceDirty = false;
                _nextLightingUpdateTime = Time.unscaledTime +
                    (1f / Mathf.Max(_qualitySettings.LightingUpdatesPerSecond, 1f));
                _nextDynamicLightingUpdateTime = Time.unscaledTime +
                    (1f / Mathf.Max(_dynamicLightUpdatesPerSecond, 1f));
                _lastTerrainGeometryRevision = terrainRenderer.LightingGeometryRevision;
                _lastContributorGeometryRevision = contributorGeometryRevision;
                RememberDynamicLightState();
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

            const float cellSize = GameConstants.World.CellSize;
            Shader.SetGlobalTexture(WorldLightTextureId, _lightmapTexture);
            Shader.SetGlobalInteger(WorldLightDebugViewId, (int)_debugView);
            Shader.SetGlobalFloat(WorldEmissionScaleId, _emissionScale);
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
            float cellSize,
            RenderTexture emissionField)
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
                _emptyExtinctionRgb * _emptyExtinctionMultiplier);
            commandBuffer.SetComputeVectorParam(
                compute,
                SolidExtinctionRgbId,
                _solidExtinctionRgb * _solidExtinctionMultiplier);
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
            commandBuffer.SetComputeIntParam(
                compute,
                EnableFinalLightingClampId,
                _enableFinalLightingClamp ? 1 : 0);
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
                EnableContactOcclusionId,
                _ambientOcclusionEnabled ? 1 : 0);
            commandBuffer.SetComputeIntParam(
                compute,
                EnableDiffuseBounceId,
                _diffuseBounceEnabled ? 1 : 0);
            BindFieldTextures(commandBuffer, _solveCascadeKernel, emissionField);
            commandBuffer.SetComputeTextureParam(
                _lightingCompute!,
                _solveAutomaticNormalsKernel,
                MaterialFieldId,
                _materialField!);
            BindFieldTextures(commandBuffer, _solveContactOcclusionKernel, emissionField);
            BindFieldTextures(commandBuffer, _resolveDirectKernel, emissionField);
            BindFieldTextures(commandBuffer, _solveDiffuseBounceKernel, emissionField);
            BindFieldTextures(commandBuffer, _compositeLightingKernel, emissionField);
            BindAutomaticNormalInput(commandBuffer, _resolveDirectKernel);
            BindAutomaticNormalInput(commandBuffer, _solveDiffuseBounceKernel);
            commandBuffer.SetComputeTextureParam(
                _lightingCompute!,
                _compositeLightingKernel,
                ContactOcclusionTextureId,
                _ambientOcclusionTexture!);
        }

        private void BindFieldTextures(
            CommandBuffer commandBuffer,
            int kernel,
            RenderTexture emissionField)
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
                emissionField);
        }

        private void BindAutomaticNormalInput(CommandBuffer commandBuffer, int kernel)
        {
            commandBuffer.SetComputeTextureParam(
                _lightingCompute!,
                kernel,
                AutomaticNormalInputId,
                _automaticNormalField!);
        }

        private void DispatchAutomaticNormals(CommandBuffer commandBuffer)
        {
            commandBuffer.SetComputeTextureParam(
                _lightingCompute!,
                _solveAutomaticNormalsKernel,
                AutomaticNormalFieldId,
                _automaticNormalField!);
            commandBuffer.DispatchCompute(
                _lightingCompute!,
                _solveAutomaticNormalsKernel,
                Mathf.CeilToInt(_fieldWidth / 8f),
                Mathf.CeilToInt(_fieldHeight / 8f),
                1);
        }

        private void DispatchContactOcclusion(CommandBuffer commandBuffer)
        {
            commandBuffer.BeginSample("Fodinae.Lighting.ContactOcclusion");
            commandBuffer.SetComputeTextureParam(
                _lightingCompute!,
                _solveContactOcclusionKernel,
                ContactOcclusionTextureId,
                _ambientOcclusionTexture!);
            commandBuffer.DispatchCompute(
                _lightingCompute!,
                _solveContactOcclusionKernel,
                Mathf.CeilToInt(_fieldWidth / 8f),
                Mathf.CeilToInt(_fieldHeight / 8f),
                1);
            commandBuffer.EndSample("Fodinae.Lighting.ContactOcclusion");
            _contactOcclusionSolveCount++;
        }

        internal static bool ShouldDispatchContactOcclusion(
            bool ambientOcclusionEnabled,
            bool geometryOrRegionChanged,
            bool ambientOcclusionSettingsChanged)
        {
            return ambientOcclusionEnabled &&
                (geometryOrRegionChanged || ambientOcclusionSettingsChanged);
        }

        private int UploadDynamicLights(
            CommandBuffer commandBuffer,
            Vector4 worldRect,
            float cellSize,
            out bool uploadedLightsChanged)
        {
            using var dynamicUploadMarker = DynamicUploadMarker.Auto();
            int maximumLightCount = _dynamicLights.Length;
            int dynamicLightCount = 0;
            int previousDynamicLightCount = _lastDynamicLightCount;
            uploadedLightsChanged = false;
            _lastDroppedDynamicLightIds.Clear();
            foreach (KeyValuePair<int, DynamicLightSource> pair in _externalLights)
            {
                DynamicLightSource source = pair.Value;
                if (dynamicLightCount >= maximumLightCount)
                {
                    _lastDroppedDynamicLightIds.Add(pair.Key);
                    continue;
                }

                if (source.Intensity <= 0f)
                {
                    _lastDroppedDynamicLightIds.Add(pair.Key);
                    continue;
                }

                if (!IntersectsWorldRect(source.Position, 1f, worldRect, cellSize))
                {
                    _lastDroppedDynamicLightIds.Add(pair.Key);
                    continue;
                }

                DynamicLight dynamicLight = new(
                    source.Position * cellSize,
                    source.Color,
                    source.Intensity);
                if (dynamicLightCount >= previousDynamicLightCount ||
                    !DynamicLightEquals(_dynamicLights[dynamicLightCount], dynamicLight))
                {
                    uploadedLightsChanged = true;
                }

                _dynamicLights[dynamicLightCount++] = dynamicLight;
            }

            if (dynamicLightCount != previousDynamicLightCount)
            {
                uploadedLightsChanged = true;
            }

            _lastDynamicLightCount = dynamicLightCount;
            _lastDroppedDynamicLightCount = _lastDroppedDynamicLightIds.Count;

            if (uploadedLightsChanged && dynamicLightCount > 0)
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

        private static bool DynamicLightEquals(
            DynamicLight left,
            DynamicLight right)
        {
            return left.PositionRadius == right.PositionRadius &&
                left.ColorIntensity == right.ColorIntensity;
        }

        private static bool IntersectsWorldRect(
            Vector2 position,
            float radius,
            Vector4 worldRect,
            float cellSize)
        {
            float worldRadius = radius * cellSize;
            float worldPositionX = position.x * cellSize;
            float worldPositionY = position.y * cellSize;
            return worldPositionX + worldRadius >= worldRect.x &&
                worldPositionX - worldRadius <= worldRect.x + worldRect.z &&
                worldPositionY + worldRadius >= worldRect.y &&
                worldPositionY - worldRadius <= worldRect.y + worldRect.w;
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
            _dynamicEmissionMaterial.SetVector(
                DynamicEmissionWorldRectId,
                worldRect);
            _dynamicEmissionMaterial.SetVector(
                DynamicEmissionFieldSizeId,
                new Vector4(_fieldWidth, _fieldHeight, 0f, 0f));
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

        private void PrepareEmissionField(
            CommandBuffer commandBuffer,
            Vector4 worldRect,
            int dynamicLightCount,
            bool dynamicLightsChanged)
        {
            using var emissionMarker = EmissionMarker.Auto();
            if (!dynamicLightsChanged && dynamicLightCount == 0)
            {
                return;
            }

            if (dynamicLightCount == 0)
            {
                return;
            }

            commandBuffer.CopyTexture(
                _staticEmissionField!,
                0,
                0,
                _emissionField!,
                0,
                0);
            commandBuffer.BeginSample("Fodinae.Lighting.DynamicEmission");
            DrawDynamicEmission(
                commandBuffer,
                worldRect,
                dynamicLightCount);
            commandBuffer.GenerateMips(_emissionField!);
            commandBuffer.EndSample("Fodinae.Lighting.DynamicEmission");
        }

        private void DispatchRadianceCascades(CommandBuffer commandBuffer)
        {
            using var cascadeMarker = CascadeMarker.Auto();
            commandBuffer.BeginSample("Fodinae.Lighting.RadianceCascades");
            ComputeShader compute = _lightingCompute!;
            commandBuffer.SetComputeBufferParam(
                compute,
                _solveCascadeKernel,
                RadianceAtlasId,
                _radianceAtlas!);
            for (int cascadeIndex = _cascades.Count - 1; cascadeIndex >= 0; cascadeIndex--)
            {
                DispatchRadianceCascade(commandBuffer, cascadeIndex);
            }

            commandBuffer.EndSample("Fodinae.Lighting.RadianceCascades");
        }

        private void DispatchRadianceCascade(
            CommandBuffer commandBuffer,
            int cascadeIndex)
        {
            commandBuffer.BeginSample("Fodinae.Lighting.RadianceCascade");
            ComputeShader compute = _lightingCompute!;
            CascadeLayout cascade = _cascades[cascadeIndex];
            bool hasFarCascade = cascadeIndex + 1 < _cascades.Count;
            CascadeLayout farCascade = hasFarCascade
                ? _cascades[cascadeIndex + 1]
                : cascade;
            commandBuffer.SetComputeBufferParam(
                compute,
                _solveCascadeKernel,
                RadianceAtlasId,
                _radianceAtlas!);
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
            commandBuffer.EndSample("Fodinae.Lighting.RadianceCascade");
        }

        private void DispatchResolveAndBounce(
            CommandBuffer commandBuffer,
            bool solveBounce,
            bool composite = true)
        {
            using var resolveMarker = ResolveMarker.Auto();
            commandBuffer.BeginSample("Fodinae.Lighting.ResolveAndBounce");
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

            if (solveBounce && _diffuseBounceEnabled && _bounceStrength > 0f)
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

            if (composite)
            {
                DispatchComposite(commandBuffer);
            }

            commandBuffer.EndSample("Fodinae.Lighting.ResolveAndBounce");
        }

        private void DispatchComposite(CommandBuffer commandBuffer)
        {
            using var compositeMarker = CompositeMarker.Auto();
            commandBuffer.BeginSample("Fodinae.Lighting.Composite");
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
            commandBuffer.EndSample("Fodinae.Lighting.Composite");
        }

        private bool HasDynamicLightsChanged()
        {
            return !_hasRenderedLightState || _externalLightsDirty;
        }

        private void LoadRuntimeConfig()
        {
            _runtimeConfig = CreateConfigFromClientConfig();
            ApplyRuntimeConfig(_runtimeConfig);
        }

        private LightingRuntimeConfig CreateConfigFromClientConfig()
        {
            ClientConfig config = _clientConfig.Config ??
                throw new InvalidOperationException(
                    "TerrariaLightingEngine requires an initialized ClientConfig.");
            LightingRuntimeConfig runtimeConfig = new()
            {
                Schema = LightingRuntimeConfig.SchemaId,
                Version = LightingRuntimeConfig.CurrentVersion,
                AmbientOcclusionEnabled = config.AmbientOcclusionEnabled,
                DiffuseBounceEnabled = config.DiffuseBounceEnabled,
                AmbientIntensity = config.AmbientIntensity,
                EmissionScale = config.EmissionScale,
                AmbientColor = config.AmbientColor,
                EmptyExtinctionRgb = config.EmptyExtinctionRgb,
                SolidExtinctionRgb = config.SolidExtinctionRgb,
                EmptyExtinctionMultiplier = config.EmptyExtinctionMultiplier,
                SolidExtinctionMultiplier = config.SolidExtinctionMultiplier,
                BounceStrength = config.BounceStrength,
                AmbientOcclusionRadiusCells = config.AmbientOcclusionRadiusCells,
                AmbientOcclusionStrength = config.AmbientOcclusionStrength,
                MaximumLightMultiplier = config.MaximumLightMultiplier,
                EnableFinalLightingClamp = config.EnableFinalLightingClamp,
                TransmittanceDebugDistanceCells = config.TransmittanceDebugDistanceCells,
                MinimumTransmission = config.MinimumTransmission,
                LightSafeBorder = config.LightSafeBorder,
                DynamicLightIntensity = config.DynamicLightIntensity,
                DynamicLightColor = config.DynamicLightColor,
                DynamicLightUpdatesPerSecond = config.DynamicLightUpdatesPerSecond,
            };
            runtimeConfig.Validate();
            return runtimeConfig;
        }

        private void ApplyRuntimeConfig(LightingRuntimeConfig config)
        {
            _ambientOcclusionEnabled = config.AmbientOcclusionEnabled;
            _diffuseBounceEnabled = config.DiffuseBounceEnabled;
            _ambientIntensity = config.AmbientIntensity;
            _emissionScale = config.EmissionScale;
            _ambientColor = config.AmbientColor;
            _emptyExtinctionRgb = config.EmptyExtinctionRgb;
            _solidExtinctionRgb = config.SolidExtinctionRgb;
            _emptyExtinctionMultiplier = config.EmptyExtinctionMultiplier;
            _solidExtinctionMultiplier = config.SolidExtinctionMultiplier;
            _bounceStrength = config.BounceStrength;
            _ambientOcclusionRadiusCells = config.AmbientOcclusionRadiusCells;
            _ambientOcclusionStrength = config.AmbientOcclusionStrength;
            _maximumLightMultiplier = config.MaximumLightMultiplier;
            _enableFinalLightingClamp = config.EnableFinalLightingClamp;
            _transmittanceDebugDistanceCells = config.TransmittanceDebugDistanceCells;
            _minimumTransmission = config.MinimumTransmission;
            _lightSafeBorder = config.LightSafeBorder;
            _dynamicLightUpdatesPerSecond = config.DynamicLightUpdatesPerSecond;
        }

        private void SyncRuntimeConfig()
        {
            _runtimeConfig.AmbientOcclusionEnabled = _ambientOcclusionEnabled;
            _runtimeConfig.DiffuseBounceEnabled = _diffuseBounceEnabled;
            _runtimeConfig.AmbientIntensity = _ambientIntensity;
            _runtimeConfig.EmissionScale = _emissionScale;
            _runtimeConfig.AmbientColor = _ambientColor;
            _runtimeConfig.EmptyExtinctionRgb = _emptyExtinctionRgb;
            _runtimeConfig.SolidExtinctionRgb = _solidExtinctionRgb;
            _runtimeConfig.EmptyExtinctionMultiplier = _emptyExtinctionMultiplier;
            _runtimeConfig.SolidExtinctionMultiplier = _solidExtinctionMultiplier;
            _runtimeConfig.BounceStrength = _bounceStrength;
            _runtimeConfig.AmbientOcclusionRadiusCells = _ambientOcclusionRadiusCells;
            _runtimeConfig.AmbientOcclusionStrength = _ambientOcclusionStrength;
            _runtimeConfig.MaximumLightMultiplier = _maximumLightMultiplier;
            _runtimeConfig.EnableFinalLightingClamp = _enableFinalLightingClamp;
            _runtimeConfig.TransmittanceDebugDistanceCells = _transmittanceDebugDistanceCells;
            _runtimeConfig.MinimumTransmission = _minimumTransmission;
            _runtimeConfig.LightSafeBorder = _lightSafeBorder;
            _runtimeConfig.DynamicLightIntensity = Mathf.Clamp(
                _runtimeConfig.DynamicLightIntensity,
                0f,
                4f);
            _runtimeConfig.DynamicLightColor = new Color(
                Mathf.Max(0f, _runtimeConfig.DynamicLightColor.r),
                Mathf.Max(0f, _runtimeConfig.DynamicLightColor.g),
                Mathf.Max(0f, _runtimeConfig.DynamicLightColor.b),
                1f);
            _runtimeConfig.DynamicLightUpdatesPerSecond = Mathf.Clamp(
                _dynamicLightUpdatesPerSecond,
                1f,
                LightingConfigLimits.DynamicLightUpdatesPerSecond);
            _runtimeConfig.Validate();
            ClientConfig config = _clientConfig.Config ??
                throw new InvalidOperationException(
                    "TerrariaLightingEngine requires an initialized ClientConfig.");
            config.AmbientOcclusionEnabled = _runtimeConfig.AmbientOcclusionEnabled;
            config.DiffuseBounceEnabled = _runtimeConfig.DiffuseBounceEnabled;
            config.AmbientIntensity = _runtimeConfig.AmbientIntensity;
            config.EmissionScale = _runtimeConfig.EmissionScale;
            config.AmbientColor = _runtimeConfig.AmbientColor;
            config.EmptyExtinctionRgb = _runtimeConfig.EmptyExtinctionRgb;
            config.SolidExtinctionRgb = _runtimeConfig.SolidExtinctionRgb;
            config.EmptyExtinctionMultiplier = _runtimeConfig.EmptyExtinctionMultiplier;
            config.SolidExtinctionMultiplier = _runtimeConfig.SolidExtinctionMultiplier;
            config.BounceStrength = _runtimeConfig.BounceStrength;
            config.AmbientOcclusionRadiusCells = _runtimeConfig.AmbientOcclusionRadiusCells;
            config.AmbientOcclusionStrength = _runtimeConfig.AmbientOcclusionStrength;
            config.MaximumLightMultiplier = _runtimeConfig.MaximumLightMultiplier;
            config.EnableFinalLightingClamp = _runtimeConfig.EnableFinalLightingClamp;
            config.TransmittanceDebugDistanceCells = _runtimeConfig.TransmittanceDebugDistanceCells;
            config.MinimumTransmission = _runtimeConfig.MinimumTransmission;
            config.LightSafeBorder = _runtimeConfig.LightSafeBorder;
            config.DynamicLightIntensity = _runtimeConfig.DynamicLightIntensity;
            config.DynamicLightColor = _runtimeConfig.DynamicLightColor;
            config.DynamicLightUpdatesPerSecond = _runtimeConfig.DynamicLightUpdatesPerSecond;
        }

        public void SetDynamicLightSettings(float intensity, Color color)
        {
            _runtimeConfig.DynamicLightIntensity = Mathf.Clamp(intensity, 0f, 4f);
            _runtimeConfig.DynamicLightColor = new Color(
                Mathf.Max(0f, color.r),
                Mathf.Max(0f, color.g),
                Mathf.Max(0f, color.b),
                1f);
            QueueRuntimeConfigSave();
        }

        public void SetDynamicLightUpdatesPerSecond(float value)
        {
            float clampedValue = Mathf.Clamp(
                value,
                1f,
                LightingConfigLimits.DynamicLightUpdatesPerSecond);
            if (Mathf.Approximately(_dynamicLightUpdatesPerSecond, clampedValue))
            {
                return;
            }

            _dynamicLightUpdatesPerSecond = clampedValue;
            _nextDynamicLightingUpdateTime = 0f;
            QueueRuntimeConfigSave();
        }

        private void QueueRuntimeConfigSave()
        {
            SyncRuntimeConfig();
            _runtimeConfigSavePending = true;
            _runtimeConfigSaveTime = Time.unscaledTime + 0.25f;
        }

        private void SaveRuntimeConfig()
        {
            SyncRuntimeConfig();
            _clientConfig.Save();
            _runtimeConfigSavePending = false;
        }

        private void FlushRuntimeConfig()
        {
            if (_runtimeConfigSavePending)
            {
                SaveRuntimeConfig();
            }
        }

        private void ApplyProjectDefaults(LightingDefaultsSnapshot defaults)
        {
            _ambientOcclusionEnabled = defaults.AmbientOcclusionEnabled;
            _diffuseBounceEnabled = defaults.DiffuseBounceEnabled;
            _ambientIntensity = defaults.AmbientIntensity;
            _emissionScale = defaults.EmissionScale;
            _ambientColor = defaults.AmbientColor;
            _emptyExtinctionRgb = defaults.EmptyExtinctionRgb;
            _solidExtinctionRgb = defaults.SolidExtinctionRgb;
            _emptyExtinctionMultiplier = defaults.EmptyExtinctionMultiplier;
            _solidExtinctionMultiplier = defaults.SolidExtinctionMultiplier;
            _bounceStrength = defaults.BounceStrength;
            _ambientOcclusionRadiusCells = defaults.AmbientOcclusionRadiusCells;
            _ambientOcclusionStrength = defaults.AmbientOcclusionStrength;
            _maximumLightMultiplier = defaults.MaximumLightMultiplier;
            _enableFinalLightingClamp = defaults.EnableFinalLightingClamp;
            _transmittanceDebugDistanceCells = defaults.TransmittanceDebugDistanceCells;
            _minimumTransmission = defaults.MinimumTransmission;
            _lightSafeBorder = defaults.LightSafeBorder;
            _dynamicLightUpdatesPerSecond = defaults.DynamicLightUpdatesPerSecond;
        }

        private void ApplyLightingDefaultsToClientConfig(LightingDefaultsSnapshot defaults)
        {
            ClientConfig config = _clientConfig.Config ??
                throw new InvalidOperationException(
                    "TerrariaLightingEngine requires an initialized ClientConfig.");
            config.AmbientOcclusionEnabled = defaults.AmbientOcclusionEnabled;
            config.DiffuseBounceEnabled = defaults.DiffuseBounceEnabled;
            config.AmbientIntensity = defaults.AmbientIntensity;
            config.EmissionScale = defaults.EmissionScale;
            config.AmbientColor = defaults.AmbientColor;
            config.EmptyExtinctionRgb = defaults.EmptyExtinctionRgb;
            config.SolidExtinctionRgb = defaults.SolidExtinctionRgb;
            config.EmptyExtinctionMultiplier = defaults.EmptyExtinctionMultiplier;
            config.SolidExtinctionMultiplier = defaults.SolidExtinctionMultiplier;
            config.BounceStrength = defaults.BounceStrength;
            config.AmbientOcclusionRadiusCells = defaults.AmbientOcclusionRadiusCells;
            config.AmbientOcclusionStrength = defaults.AmbientOcclusionStrength;
            config.MaximumLightMultiplier = defaults.MaximumLightMultiplier;
            config.EnableFinalLightingClamp = defaults.EnableFinalLightingClamp;
            config.TransmittanceDebugDistanceCells = defaults.TransmittanceDebugDistanceCells;
            config.MinimumTransmission = defaults.MinimumTransmission;
            config.LightSafeBorder = defaults.LightSafeBorder;
            config.DynamicLightIntensity = defaults.DynamicLightIntensity;
            config.DynamicLightColor = defaults.DynamicLightColor;
            config.DynamicLightUpdatesPerSecond = defaults.DynamicLightUpdatesPerSecond;
        }

        private bool SetRuntimeSetting(
            ref float field,
            float value,
            float minimum,
            float maximum,
            bool radianceDirty = true)
        {
            float clampedValue = Mathf.Clamp(value, minimum, maximum);
            if (Mathf.Approximately(field, clampedValue))
            {
                return false;
            }

            field = clampedValue;
            QueueRuntimeConfigSave();
            if (radianceDirty)
            {
                _hasRenderedLightState = false;
                _hasStaticRadianceState = false;
            }

            _compositeDirty = true;
            return true;
        }

        private void SetRuntimeColor(ref Color field, Color value, bool radianceDirty)
        {
            Color sanitized = new(
                Mathf.Max(0f, value.r),
                Mathf.Max(0f, value.g),
                Mathf.Max(0f, value.b),
                Mathf.Max(0f, value.a));
            if (field == sanitized)
            {
                return;
            }

            field = sanitized;
            QueueRuntimeConfigSave();
            if (radianceDirty)
            {
                _hasRenderedLightState = false;
                _hasStaticRadianceState = false;
            }

            _compositeDirty = true;
        }

        private void RememberDynamicLightState()
        {
            _hasRenderedLightState = true;
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

        private void EnsureResources(int gridWidth, int gridHeight, Camera camera)
        {
            if (!camera.orthographic)
            {
                throw new InvalidOperationException(
                    "Radiance Cascades requires an orthographic base camera.");
            }

            if (camera.pixelWidth <= 0 || camera.pixelHeight <= 0 ||
                camera.orthographicSize <= 0f || camera.aspect <= 0f)
            {
                throw new InvalidOperationException(
                    $"Radiance Cascades received invalid camera metrics: " +
                    $"pixels={camera.pixelWidth}x{camera.pixelHeight}, " +
                    $"orthographicSize={camera.orthographicSize}, aspect={camera.aspect}.");
            }

            float cameraWorldHeight = camera.orthographicSize * 2f;
            float cameraWorldWidth = cameraWorldHeight * camera.aspect;
            float renderScale = _qualitySettings.RenderScale;
            float horizontalPixelsPerCell =
                camera.pixelWidth * renderScale * GameConstants.World.CellSize / cameraWorldWidth;
            float verticalPixelsPerCell =
                camera.pixelHeight * renderScale * GameConstants.World.CellSize / cameraWorldHeight;
            _requestedPixelsPerCell = Mathf.Max(
                _qualitySettings.LightingMinimumPixelsPerCell,
                Mathf.Min(horizontalPixelsPerCell, verticalPixelsPerCell));

            float scale = _requestedPixelsPerCell;
            float textureDimensionScale = Mathf.Min(
                _qualitySettings.LightingMaximumTextureDimension / (float)gridWidth,
                _qualitySettings.LightingMaximumTextureDimension / (float)gridHeight);
            _textureDimensionLimited = textureDimensionScale < scale;
            scale = Mathf.Min(scale, textureDimensionScale);
            int fieldWidth = Mathf.Max(1, Mathf.CeilToInt(gridWidth * scale));
            int fieldHeight = Mathf.Max(1, Mathf.CeilToInt(gridHeight * scale));
            _cascadeBudgetLimited = FitFieldDimensionsToAtlasBudget(
                ref fieldWidth,
                ref fieldHeight);
            _effectivePixelsPerCell = Mathf.Min(
                fieldWidth / (float)gridWidth,
                fieldHeight / (float)gridHeight);
            if (_fieldWidth >= fieldWidth && _fieldHeight >= fieldHeight &&
                _materialField != null && _ambientOcclusionTexture != null &&
                _radianceAtlas != null)
            {
                _effectivePixelsPerCell = Mathf.Min(
                    _fieldWidth / (float)gridWidth,
                    _fieldHeight / (float)gridHeight);
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
            _automaticNormalField = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Point,
                "_AutomaticNormalField");
            _directTexture = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.ARGBHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_RadianceDirect");
            _ambientOcclusionTexture = CreateTexture(
                fieldWidth,
                fieldHeight,
                RenderTextureFormat.RHalf,
                randomWrite: true,
                FilterMode.Bilinear,
                "_ContactOcclusion");
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
            _hasRenderedLightState = false;
            _hasStaticRadianceState = false;
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

        private bool FitFieldDimensionsToAtlasBudget(ref int width, ref int height)
        {
            long atlasDimension = _qualitySettings.LightingCascadeAtlasLimit;
            long maximumEntryCount = atlasDimension * atlasDimension * 4;
            bool dimensionsReduced = false;
            for (int iteration = 0; iteration < 8; iteration++)
            {
                long requiredEntryCount = CalculateCascadeEntryCount(width, height);
                if (requiredEntryCount <= maximumEntryCount)
                {
                    return dimensionsReduced;
                }

                float reduction = Mathf.Sqrt(maximumEntryCount / (float)requiredEntryCount) * 0.98f;
                width = Mathf.Max(1, Mathf.FloorToInt(width * reduction));
                height = Mathf.Max(1, Mathf.FloorToInt(height * reduction));
                dimensionsReduced = true;
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

            _lightingCompute = Resources.Load<ComputeShader>(ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute) ??
                throw new InvalidOperationException(
                    $"Required compute shader Resources/{ProjectRuntimeContracts.ResourcePaths.WorldLightingCompute} is missing.");
            foreach (string kernelName in RequiredKernels)
            {
                if (!_lightingCompute.HasKernel(kernelName))
                {
                    throw new InvalidOperationException(
                        $"Radiance Cascades compute shader is missing kernel '{kernelName}'.");
                }
            }

            _solveCascadeKernel = _lightingCompute.FindKernel("SolveCascade");
            _solveAutomaticNormalsKernel = _lightingCompute.FindKernel("SolveAutomaticNormals");
            _solveContactOcclusionKernel = _lightingCompute.FindKernel("SolveContactOcclusion");
            _resolveDirectKernel = _lightingCompute.FindKernel("ResolveDirect");
            _solveDiffuseBounceKernel = _lightingCompute.FindKernel("SolveDiffuseBounce");
            _compositeLightingKernel = _lightingCompute.FindKernel("CompositeLighting");
            ValidateKernelSupportOrThrow("SolveCascade", _solveCascadeKernel);
            ValidateKernelSupportOrThrow("SolveAutomaticNormals", _solveAutomaticNormalsKernel);
            ValidateKernelSupportOrThrow("SolveContactOcclusion", _solveContactOcclusionKernel);
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
                !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RHalf) ||
                !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.RHalf) ||
                !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {
                throw new NotSupportedException(
                    "Radiance Cascades requires two MRTs, RGBA8 material, R16F contact AO, and random-write lighting targets.");
            }
        }

        private static void ValidateMaterialFieldPass()
        {
            Shader terrainShader = Shader.Find(ProjectRuntimeContracts.ShaderNames.Terrain) ??
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
                Resources.Load<Shader>("Shaders/Lighting/DynamicEmission") ??
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

        private void ApplyQualitySettings(
            GraphicsPreset preset,
            GraphicsQualitySettings settings)
        {
            GraphicsQualityProfile.ValidateSettings(settings, preset.ToString());
            bool technicalSettingsChanged = _qualitySettings != settings;
            if (technicalSettingsChanged && _materialField != null)
            {
                ReleaseResources();
            }

            _graphicsPreset = preset;
            ApplyUnityQualityLevel(preset);
            _qualitySettings = settings;
            ApplyUnityRenderingSettings(_qualitySettings);
            if (!technicalSettingsChanged)
            {
                return;
            }

            _lastVisibleRegion = new Vector4(float.NaN, float.NaN, float.NaN, float.NaN);
            _fieldDirty = true;
            _nextLightingUpdateTime = 0f;
            _nextDynamicLightingUpdateTime = 0f;
            _dynamicSolveInProgress = false;
            _hasRenderedLightState = false;
            _hasStaticRadianceState = false;
        }

        private static void ApplyUnityQualityLevel(GraphicsPreset preset)
        {
            if (!GraphicsQualityProfile.IsStandard(preset))
            {
                return;
            }

            string targetName = preset.ToString();
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
            _lastDynamicLightCount = 0;
            _lastDroppedDynamicLightCount = 0;
            _lastDroppedDynamicLightIds.Clear();
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
            ReleaseTexture(ref _automaticNormalField);
            ReleaseTexture(ref _directTexture);
            ReleaseTexture(ref _ambientOcclusionTexture);
            ReleaseTexture(ref _bounceTexture);
            ReleaseTexture(ref _lightmapTexture);
            _fieldWidth = 0;
            _fieldHeight = 0;
            _bounceWidth = 0;
            _bounceHeight = 0;
            _cascades.Clear();
            _dynamicSolveInProgress = false;
            _hasStaticRadianceState = false;
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
