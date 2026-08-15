#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Game.Managers;
using Fodinae.World.Lighting;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;

namespace Fodinae.World
{
    [DisallowMultipleComponent]
    public class SurfaceRenderer : MonoBehaviour, ILightingGeometryContributor
    {
        private const string SurfaceShaderName = ProjectRuntimeContracts.ShaderNames.WorldSurface;
        private const float TransitHeight = 2f;
        private const float PerspectiveHeight = 2f;
        private const float TransitTileWidth = 32f;
        private const float PerspectiveTileWidth = 5f;
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int WrapBaseMapId = Shader.PropertyToID("_WrapBaseMap");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissionStrengthId = Shader.PropertyToID("_EmissionStrength");
        private static readonly int OccupancyId = Shader.PropertyToID("_Occupancy");
        private static readonly int[] QuadTriangles =
        [
            0, 1, 2, 3, 2, 1,
        ];

        [Header("Local Assets")]
        [SerializeField]
        private Texture2D? _transitTexture;
        [SerializeField]
        private Texture2D? _perspectiveTexture;

        [Header("Lighting")]
        [SerializeField]
        [ColorUsage(showAlpha: false, hdr: true)]
        private Color _transitEmissionColor = new(1f, 0.7f, 0.35f, 1f);
        [SerializeField]
        [Range(0f, 8f)]
        private float _transitEmissionStrength = 0.35f;
        [SerializeField]
        [ColorUsage(showAlpha: false, hdr: true)]
        private Color _perspectiveEmissionColor = new(0.45f, 0.65f, 1f, 1f);
        [SerializeField]
        [Range(0f, 8f)]
        private float _perspectiveEmissionStrength = 0.12f;
        [Header("Rendering")]
        [SerializeField]
        private int _transitSortingOrder = -501;
        [SerializeField]
        private int _perspectiveSortingOrder = -502;

        [Inject]
        private MapManager? _mapManager;
        [Inject]
        private LightingGeometryRegistry? _lightingGeometryRegistry;

        private Camera? _mainCamera;
        private Mesh? _transitMesh;
        private Mesh? _perspectiveMesh;
        private Mesh? _transitLightingMesh;
        private Mesh? _perspectiveLightingMesh;
        private Material? _transitMaterial;
        private Material? _perspectiveMaterial;
        private ulong _lightingGeometryRevision = 1;
        private int _lastWorldWidth = int.MinValue;
        private int _lastWorldHeight = int.MinValue;
        private Vector3 _lastCameraPosition = new(float.NaN, float.NaN, float.NaN);
        private float _lastCameraOrthoSize = float.NaN;
        private float _lastCameraAspect = float.NaN;
        private bool _initialized;
        private bool _registered;

        public ulong LightingGeometryRevision => _lightingGeometryRevision;

        public void SetLocalAssets(Texture2D? transitTexture, Texture2D? perspectiveTexture)
        {
            if (_initialized)
            {
                throw new InvalidOperationException(
                    "Surface assets cannot be replaced after SurfaceRenderer initialization.");
            }

            _transitTexture = transitTexture;
            _perspectiveTexture = perspectiveTexture;
        }

        protected void LateUpdate()
        {
            MapManager mapManager = _mapManager ??
                throw new InvalidOperationException(
                    "SurfaceRenderer requires MapManager injection.");
            if (!mapManager.IsWorldInitialized)
            {
                return;
            }

            EnsureInitialized();

            Camera mainCamera = _mainCamera ??= Camera.main ??
                throw new InvalidOperationException(
                    "SurfaceRenderer requires a tagged Main Camera.");
            if (_lastWorldWidth == mapManager.WorldWidth &&
                _lastWorldHeight == mapManager.WorldHeight &&
                _lastCameraPosition == mainCamera.transform.position &&
                Mathf.Approximately(_lastCameraOrthoSize, mainCamera.orthographicSize) &&
                Mathf.Approximately(_lastCameraAspect, mainCamera.aspect))
            {
                return;
            }

            RebuildBands(mapManager.WorldWidth, mapManager.WorldHeight, mainCamera);
        }

        public void RenderLightingFields(
            CommandBuffer commandBuffer,
            in LightingFieldContext context)
        {
            if (!_initialized || _transitLightingMesh == null ||
                _perspectiveLightingMesh == null ||
                _transitMaterial == null || _perspectiveMaterial == null)
            {
                throw new InvalidOperationException(
                    "Surface lighting fields cannot be rendered before surface initialization.");
            }

            int transitPass = RequireLightingPass(_transitMaterial);
            int perspectivePass = RequireLightingPass(_perspectiveMaterial);
            commandBuffer.DrawMesh(
                _perspectiveLightingMesh,
                transform.localToWorldMatrix,
                _perspectiveMaterial,
                submeshIndex: 0,
                shaderPass: perspectivePass);
            commandBuffer.DrawMesh(
                _transitLightingMesh,
                transform.localToWorldMatrix,
                _transitMaterial,
                submeshIndex: 0,
                shaderPass: transitPass);
        }

        protected void OnDestroy()
        {
            if (_registered)
            {
                _lightingGeometryRegistry?.Unregister(this);
                _registered = false;
            }

            DestroyOwnedObject(_transitMesh);
            DestroyOwnedObject(_perspectiveMesh);
            DestroyOwnedObject(_transitLightingMesh);
            DestroyOwnedObject(_perspectiveLightingMesh);
            DestroyOwnedObject(_transitMaterial);
            DestroyOwnedObject(_perspectiveMaterial);
            _transitMesh = null;
            _perspectiveMesh = null;
            _transitLightingMesh = null;
            _perspectiveLightingMesh = null;
            _transitMaterial = null;
            _perspectiveMaterial = null;
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            Texture2D transitTexture = _transitTexture ??
                throw new InvalidOperationException(
                    "SurfaceRenderer transit texture is not assigned.");
            Texture2D perspectiveTexture = _perspectiveTexture ??
                throw new InvalidOperationException(
                    "SurfaceRenderer perspective texture is not assigned.");
            Shader surfaceShader = Shader.Find(SurfaceShaderName);
            if (surfaceShader == null || !surfaceShader.isSupported)
            {
                throw new InvalidOperationException(
                    $"Required surface shader '{SurfaceShaderName}' is missing or unsupported.");
            }

            _transitMaterial = CreateMaterial(
                surfaceShader,
                "World Surface Transit",
                transitTexture,
                _transitEmissionColor,
                _transitEmissionStrength,
                occupancy: 1f,
                wrapBaseMap: true);
            _perspectiveMaterial = CreateMaterial(
                surfaceShader,
                "World Surface Perspective",
                perspectiveTexture,
                _perspectiveEmissionColor,
                _perspectiveEmissionStrength,
                occupancy: 0f,
                wrapBaseMap: true);
            _transitMesh = CreateBandMesh("World Surface Transit Mesh");
            _perspectiveMesh = CreateBandMesh("World Surface Perspective Mesh");
            _transitLightingMesh = CreateBandMesh("World Surface Transit Lighting Mesh");
            _perspectiveLightingMesh = CreateBandMesh(
                "World Surface Perspective Lighting Mesh");

            CreateBandObject(
                "SurfaceTransit",
                _transitMesh,
                _transitMaterial,
                _transitSortingOrder);
            CreateBandObject(
                "SurfacePerspective",
                _perspectiveMesh,
                _perspectiveMaterial,
                _perspectiveSortingOrder);
            LightingGeometryRegistry registry = _lightingGeometryRegistry ??
                throw new InvalidOperationException(
                    "SurfaceRenderer requires LightingGeometryRegistry injection.");
            registry.Register(this);
            _registered = true;
            _initialized = true;
        }

        private void RebuildBands(int worldWidth, int worldHeight, Camera mainCamera)
        {
            if (worldWidth <= 0 || worldHeight <= 0)
            {
                throw new InvalidOperationException(
                    $"SurfaceRenderer received invalid world dimensions {worldWidth}x{worldHeight}.");
            }

            float halfHeight = mainCamera.orthographicSize;
            float halfWidth = halfHeight * mainCamera.aspect;
            float cameraLeft = mainCamera.transform.position.x - halfWidth;
            float cameraRight = mainCamera.transform.position.x + halfWidth;
            float surfaceLeft = Mathf.Clamp(cameraLeft, 0f, worldWidth);
            float surfaceRight = Mathf.Clamp(cameraRight, surfaceLeft, worldWidth);
            bool worldDimensionsChanged =
                _lastWorldWidth != worldWidth || _lastWorldHeight != worldHeight;

            if (worldDimensionsChanged)
            {
                UpdateTopBandMesh(
                    _transitLightingMesh!,
                    0f,
                    worldWidth,
                    bottom: worldHeight,
                    thickness: TransitHeight,
                    tileLength: TransitTileWidth,
                    lightSampleY: worldHeight - 0.5f);
                UpdateTopBandMesh(
                    _perspectiveLightingMesh!,
                    0f,
                    worldWidth,
                    bottom: worldHeight + TransitHeight,
                    thickness: PerspectiveHeight,
                    tileLength: PerspectiveTileWidth,
                    lightSampleY: worldHeight - 0.5f);
                _lightingGeometryRevision++;
            }

            UpdateTopBandMesh(
                _transitMesh!,
                surfaceLeft,
                surfaceRight,
                bottom: worldHeight,
                thickness: TransitHeight,
                tileLength: TransitTileWidth,
                lightSampleY: worldHeight - 0.5f);
            UpdateTopBandMesh(
                _perspectiveMesh!,
                surfaceLeft,
                surfaceRight,
                bottom: worldHeight + TransitHeight,
                thickness: PerspectiveHeight,
                tileLength: PerspectiveTileWidth,
                lightSampleY: worldHeight - 0.5f);
            _lastWorldWidth = worldWidth;
            _lastWorldHeight = worldHeight;
            _lastCameraPosition = mainCamera.transform.position;
            _lastCameraOrthoSize = mainCamera.orthographicSize;
            _lastCameraAspect = mainCamera.aspect;
        }

        private void CreateBandObject(
            string objectName,
            Mesh mesh,
            Material material,
            int sortingOrder)
        {
            var bandObject = new GameObject(objectName);
            bandObject.transform.SetParent(transform, worldPositionStays: false);
            MeshFilter meshFilter = bandObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = bandObject.AddComponent<MeshRenderer>();
            meshFilter.sharedMesh = mesh;
            meshRenderer.sharedMaterial = material;
            meshRenderer.sortingOrder = sortingOrder;
        }

        private static Material CreateMaterial(
            Shader shader,
            string materialName,
            Texture2D texture,
            Color emissionColor,
            float emissionStrength,
            float occupancy,
            bool wrapBaseMap)
        {
            var material = new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.DontSave,
            };
            material.SetTexture(BaseMapId, texture);
            material.SetFloat(WrapBaseMapId, wrapBaseMap ? 1f : 0f);
            material.SetColor(EmissionColorId, emissionColor);
            material.SetFloat(EmissionStrengthId, emissionStrength);
            material.SetFloat(OccupancyId, occupancy);
            return material;
        }

        private static Mesh CreateBandMesh(string meshName)
        {
            var mesh = new Mesh
            {
                name = meshName,
                hideFlags = HideFlags.DontSave,
            };
            mesh.MarkDynamic();
            return mesh;
        }

        private static void UpdateTopBandMesh(
            Mesh mesh,
            float left,
            float right,
            float bottom,
            float thickness,
            float tileLength,
            float lightSampleY)
        {
            Vector3[] vertices =
            [
                new(left, bottom, 0f),
                new(left, bottom + thickness, 0f),
                new(right, bottom, 0f),
                new(right, bottom + thickness, 0f),
            ];
            float uLeft = -(left - (Mathf.Floor(left / tileLength) * tileLength)) /
                tileLength;
            float uRight = uLeft + ((left - right) / tileLength);
            Vector2[] uv =
            [
                new(uLeft, 0f),
                new(uLeft, 1f),
                new(uRight, 0f),
                new(uRight, 1f),
            ];
            Vector2[] lightingData =
            [
                new(1f, lightSampleY - bottom),
                new(1f, lightSampleY - (bottom + thickness)),
                new(1f, lightSampleY - bottom),
                new(1f, lightSampleY - (bottom + thickness)),
            ];

            mesh.Clear(keepVertexLayout: false);
            mesh.SetVertices(vertices);
            mesh.SetUVs(channel: 0, uv);
            mesh.SetUVs(channel: 1, lightingData);
            mesh.SetTriangles(QuadTriangles, submesh: 0, calculateBounds: true);
        }

        private static int RequireLightingPass(Material material)
        {
            int pass = material.FindPass("LightingMaterialField");
            if (pass < 0)
            {
                throw new InvalidOperationException(
                    $"Surface material '{material.name}' is missing LightingMaterialField pass.");
            }

            return pass;
        }

        private static void DestroyOwnedObject(UnityEngine.Object? ownedObject)
        {
            if (ownedObject == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(ownedObject);
            }
            else
            {
                DestroyImmediate(ownedObject);
            }
        }
    }
}
