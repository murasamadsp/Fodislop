#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
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
        private const string TransitObjectName = "SurfaceTransit";
        private const string PerspectiveObjectName = "SurfacePerspective";
        private const string RedRockObjectName = "SurfaceRedrock";
        private const string RedRockKeyword = "FODINAE_SURFACE_REDROCK";
        private const string TransitKeyword = "FODINAE_SURFACE_TRANSIT";
        private const string PerspectiveKeyword = "FODINAE_SURFACE_PERSPECTIVE";
        private const float TransitHeight = 2f;
        private const float PerspectiveHeight = 2f;
        private const float TransitTileWidth = 32f;
        private const float PerspectiveTileWidth = 5f;
        private const float BoundaryOverscan = 2f;
        private static readonly int[] QuadTriangles =
        [
            0, 1, 2, 3, 2, 1,
        ];
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissionStrengthId = Shader.PropertyToID("_EmissionStrength");
        private static readonly int OccupancyId = Shader.PropertyToID("_Occupancy");
        private static readonly int BaseMapTileCountId =
            Shader.PropertyToID("_BaseMapTileCount");
        private static readonly int WorldSizeId = Shader.PropertyToID("_WorldSize");

        [Header("Local Assets")]
        [SerializeField]
        private Texture2D? _transitTexture;
        [SerializeField]
        private Texture2D? _perspectiveTexture;
        [SerializeField]
        private Texture2D? _redRockTexture;

        [Header("Rendering")]
        [SerializeField]
        private int _transitSortingOrder = -501;
        [SerializeField]
        private int _perspectiveSortingOrder = -502;

        [Inject]
        private MapManager _mapManager = null!;
        [Inject]
        private LightingGeometryRegistry _lightingGeometryRegistry = null!;
        [Inject]
        private IClientConfigManager _clientConfigManager = null!;

        private readonly Vector3[] _boundaryVertices = new Vector3[12];
        private readonly Vector2[] _boundaryUv = new Vector2[12];
        private readonly Vector2[] _boundaryLightingData = new Vector2[12];
        private readonly int[] _boundaryTriangles = new int[18];
        private readonly Vector3[] _quadVertices = new Vector3[4];
        private readonly Vector2[] _quadUv = new Vector2[4];
        private readonly Vector2[] _quadLightingData = new Vector2[4];

        private Camera? _mainCamera;
        private Mesh? _transitMesh;
        private Mesh? _perspectiveMesh;
        private Mesh? _redRockMesh;
        private Mesh? _transitLightingMesh;
        private Mesh? _perspectiveLightingMesh;
        private Mesh? _redRockLightingMesh;
        private Material? _transitMaterial;
        private Material? _perspectiveMaterial;
        private Material? _redRockMaterial;
        private ulong _lightingGeometryRevision = 1;
        private int _lastWorldWidth = int.MinValue;
        private int _lastWorldHeight = int.MinValue;
        private Vector3 _lastCameraPosition = new(float.NaN, float.NaN, float.NaN);
        private float _lastCameraOrthoSize = float.NaN;
        private float _lastCameraAspect = float.NaN;
        private bool _initialized;
        private bool _registered;

        private enum SurfaceKind
        {
            RedRock,
            Transit,
            Perspective,
        }

        public ulong LightingGeometryRevision => _lightingGeometryRevision;

        public void ApplyClientConfig()
        {
            if (!_initialized)
            {
                return;
            }

            ClientConfig config = _clientConfigManager.Config ??
                throw new InvalidOperationException(
                    "SurfaceRenderer requires an initialized ClientConfig.");
            Material transitMaterial = _transitMaterial ??
                throw new InvalidOperationException(
                    "SurfaceRenderer transit material is not initialized.");
            Material perspectiveMaterial = _perspectiveMaterial ??
                throw new InvalidOperationException(
                    "SurfaceRenderer perspective material is not initialized.");
            Material redRockMaterial = _redRockMaterial ??
                throw new InvalidOperationException(
                    "SurfaceRenderer redrock material is not initialized.");

            ApplyMaterialConfig(
                transitMaterial,
                config.TransitEmissionColor,
                config.TransitEmissionStrength,
                config.SurfaceOccupancy);
            ApplyMaterialConfig(
                perspectiveMaterial,
                config.PerspectiveEmissionColor,
                config.PerspectiveEmissionStrength,
                occupancy: 0f);
            ApplyMaterialConfig(
                redRockMaterial,
                Color.clear,
                emissionStrength: 0f,
                occupancy: 1f);
            _lightingGeometryRevision++;
        }

        public void SetLocalAssets(
            Texture2D? transitTexture,
            Texture2D? perspectiveTexture,
            Texture2D? redRockTexture)
        {
            if (_initialized)
            {
                if (_transitTexture == transitTexture &&
                    _perspectiveTexture == perspectiveTexture &&
                    _redRockTexture == redRockTexture)
                {
                    return;
                }

                throw new InvalidOperationException(
                    "Surface assets cannot be replaced after SurfaceRenderer initialization.");
            }

            _transitTexture = transitTexture;
            _perspectiveTexture = perspectiveTexture;
            _redRockTexture = redRockTexture;
        }

        public void RenderLightingFields(
            CommandBuffer commandBuffer,
            in LightingFieldContext context)
        {
            if (!_initialized || _transitLightingMesh == null ||
                _perspectiveLightingMesh == null || _redRockLightingMesh == null ||
                _transitMaterial == null || _perspectiveMaterial == null ||
                _redRockMaterial == null)
            {
                throw new InvalidOperationException(
                    "Surface lighting fields cannot be rendered before surface initialization.");
            }

            Rect lightingRect = Rect.MinMaxRect(
                context.WorldRect.x,
                context.WorldRect.y,
                context.WorldRect.x + context.WorldRect.z,
                context.WorldRect.y + context.WorldRect.w);
            UpdateBoundaryMesh(
                _redRockLightingMesh,
                lightingRect,
                _mapManager.WorldWidth,
                _mapManager.WorldHeight);
            UpdateTransitMesh(
                _transitLightingMesh,
                lightingRect,
                _mapManager.WorldHeight);
            UpdatePerspectiveMesh(
                _perspectiveLightingMesh,
                lightingRect,
                _mapManager.WorldHeight);

            DrawLightingMesh(commandBuffer, _redRockLightingMesh, _redRockMaterial);
            DrawLightingMesh(commandBuffer, _perspectiveLightingMesh, _perspectiveMaterial);
            DrawLightingMesh(commandBuffer, _transitLightingMesh, _transitMaterial);
        }

        protected void OnEnable()
        {
            if (_initialized && !_registered && _lightingGeometryRegistry != null)
            {
                _lightingGeometryRegistry.Register(this);
                _registered = true;
            }
        }

        protected void LateUpdate()
        {
            if (!_mapManager.IsWorldInitialized)
            {
                return;
            }

            if (!EnsureInitialized())
            {
                return;
            }

            if (_mainCamera == null)
            {
                _mainCamera = GameplayCamera.Resolve();
            }

            Camera mainCamera = _mainCamera ??
                throw new InvalidOperationException(
                    "SurfaceRenderer requires a tagged Main Camera.");
            if (_lastWorldWidth == _mapManager.WorldWidth &&
                _lastWorldHeight == _mapManager.WorldHeight &&
                _lastCameraPosition == mainCamera.transform.position &&
                Mathf.Approximately(_lastCameraOrthoSize, mainCamera.orthographicSize) &&
                Mathf.Approximately(_lastCameraAspect, mainCamera.aspect))
            {
                return;
            }

            RebuildVisibleGeometry(
                _mapManager.WorldWidth,
                _mapManager.WorldHeight,
                mainCamera);
        }

        protected void OnDisable()
        {
            UnregisterLightingContributor();
        }

        protected void OnDestroy()
        {
            UnregisterLightingContributor();
            DestroyOwnedObject(_transitMesh);
            DestroyOwnedObject(_perspectiveMesh);
            DestroyOwnedObject(_redRockMesh);
            DestroyOwnedObject(_transitLightingMesh);
            DestroyOwnedObject(_perspectiveLightingMesh);
            DestroyOwnedObject(_redRockLightingMesh);
            DestroyOwnedObject(_transitMaterial);
            DestroyOwnedObject(_perspectiveMaterial);
            DestroyOwnedObject(_redRockMaterial);

            // Runtime children belong to the parent GameObject and Unity restores them
            // across domain reloads. Destroying them here during a reload can leave the
            // restored SurfaceRenderer bound to objects pending destruction.
            if (!Application.isPlaying)
            {
                DestroyOwnedChild(TransitObjectName);
                DestroyOwnedChild(PerspectiveObjectName);
                DestroyOwnedChild(RedRockObjectName);
            }
        }

        private bool EnsureInitialized()
        {
            if (_initialized)
            {
                return true;
            }

            // SceneSetup assigns these asynchronously via SetLocalAssets() once its texture
            // load completes; a null here means "still loading", not a failure.
            if (_transitTexture == null || _perspectiveTexture == null || _redRockTexture == null)
            {
                return false;
            }

            Texture2D transitTexture = _transitTexture;
            Texture2D perspectiveTexture = _perspectiveTexture;
            Texture2D redRockTexture = _redRockTexture;
            ClientConfig clientConfig = _clientConfigManager.Config ??
                throw new InvalidOperationException(
                    "SurfaceRenderer requires an initialized ClientConfig.");
            if (_mapManager.WorldWidth <= 0 || _mapManager.WorldHeight <= 0)
            {
                throw new InvalidOperationException(
                    $"SurfaceRenderer requires valid world dimensions, received " +
                    $"{_mapManager.WorldWidth}x{_mapManager.WorldHeight}.");
            }

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
                clientConfig.TransitEmissionColor,
                clientConfig.TransitEmissionStrength,
                clientConfig.SurfaceOccupancy,
                Vector2.one,
                new Vector2(_mapManager.WorldWidth, _mapManager.WorldHeight),
                SurfaceKind.Transit);
            _perspectiveMaterial = CreateMaterial(
                surfaceShader,
                "World Surface Perspective",
                perspectiveTexture,
                clientConfig.PerspectiveEmissionColor,
                clientConfig.PerspectiveEmissionStrength,
                occupancy: 0f,
                baseMapTileCount: Vector2.one,
                worldSize: new Vector2(_mapManager.WorldWidth, _mapManager.WorldHeight),
                kind: SurfaceKind.Perspective);
            _redRockMaterial = CreateMaterial(
                surfaceShader,
                "World Surface Redrock",
                redRockTexture,
                Color.clear,
                emissionStrength: 0f,
                occupancy: 1f,
                baseMapTileCount: GetTerrainSheetTileCount(redRockTexture),
                worldSize: new Vector2(_mapManager.WorldWidth, _mapManager.WorldHeight),
                kind: SurfaceKind.RedRock);

            _transitMesh = CreateMesh("World Surface Transit Mesh");
            _perspectiveMesh = CreateMesh("World Surface Perspective Mesh");
            _redRockMesh = CreateMesh("World Surface Redrock Mesh");
            _transitLightingMesh = CreateMesh("World Surface Transit Lighting Mesh");
            _perspectiveLightingMesh = CreateMesh("World Surface Perspective Lighting Mesh");
            _redRockLightingMesh = CreateMesh("World Surface Redrock Lighting Mesh");

            BindBandObject(
                TransitObjectName,
                _transitMesh,
                _transitMaterial,
                _transitSortingOrder);
            BindBandObject(
                PerspectiveObjectName,
                _perspectiveMesh,
                _perspectiveMaterial,
                _perspectiveSortingOrder);
            BindBandObject(
                RedRockObjectName,
                _redRockMesh,
                _redRockMaterial,
                _transitSortingOrder);

            _lightingGeometryRegistry.Register(this);
            _registered = true;
            _initialized = true;
            return true;
        }

        private void RebuildVisibleGeometry(
            int worldWidth,
            int worldHeight,
            Camera mainCamera)
        {
            if (worldWidth <= 0 || worldHeight <= 0)
            {
                throw new InvalidOperationException(
                    $"SurfaceRenderer received invalid world dimensions {worldWidth}x{worldHeight}.");
            }

            float halfHeight = mainCamera.orthographicSize + BoundaryOverscan;
            float halfWidth = (mainCamera.orthographicSize * mainCamera.aspect) +
                BoundaryOverscan;
            Vector3 cameraPosition = mainCamera.transform.position;
            Rect visibleRect = Rect.MinMaxRect(
                cameraPosition.x - halfWidth,
                cameraPosition.y - halfHeight,
                cameraPosition.x + halfWidth,
                cameraPosition.y + halfHeight);

            UpdateBoundaryMesh(_redRockMesh!, visibleRect, worldWidth, worldHeight);
            UpdateTransitMesh(_transitMesh!, visibleRect, worldHeight);
            UpdatePerspectiveMesh(_perspectiveMesh!, visibleRect, worldHeight);

            if (_lastWorldWidth != worldWidth || _lastWorldHeight != worldHeight)
            {
                SetMaterialWorldSize(worldWidth, worldHeight);
                _lightingGeometryRevision++;
            }

            _lastWorldWidth = worldWidth;
            _lastWorldHeight = worldHeight;
            _lastCameraPosition = cameraPosition;
            _lastCameraOrthoSize = mainCamera.orthographicSize;
            _lastCameraAspect = mainCamera.aspect;
        }

        private void UpdateBoundaryMesh(
            Mesh mesh,
            Rect coverageRect,
            int worldWidth,
            int worldHeight)
        {
            int vertexCount = 0;
            int indexCount = 0;
            AppendBoundaryQuad(
                coverageRect.xMin,
                coverageRect.yMin,
                Mathf.Min(coverageRect.xMax, 0f),
                Mathf.Min(coverageRect.yMax, worldHeight),
                ref vertexCount,
                ref indexCount);
            AppendBoundaryQuad(
                Mathf.Max(coverageRect.xMin, worldWidth),
                coverageRect.yMin,
                coverageRect.xMax,
                Mathf.Min(coverageRect.yMax, worldHeight),
                ref vertexCount,
                ref indexCount);
            AppendBoundaryQuad(
                Mathf.Max(coverageRect.xMin, 0f),
                coverageRect.yMin,
                Mathf.Min(coverageRect.xMax, worldWidth),
                Mathf.Min(coverageRect.yMax, 0f),
                ref vertexCount,
                ref indexCount);

            mesh.Clear(keepVertexLayout: false);
            if (vertexCount == 0)
            {
                return;
            }

            mesh.SetVertices(_boundaryVertices, 0, vertexCount);
            mesh.SetUVs(channel: 0, _boundaryUv, 0, vertexCount);
            mesh.SetUVs(channel: 1, _boundaryLightingData, 0, vertexCount);
            mesh.SetTriangles(
                _boundaryTriangles,
                trianglesStart: 0,
                trianglesLength: indexCount,
                submesh: 0,
                calculateBounds: true);
        }

        private void AppendBoundaryQuad(
            float left,
            float bottom,
            float right,
            float top,
            ref int vertexCount,
            ref int indexCount)
        {
            if (right <= left || top <= bottom)
            {
                return;
            }

            int firstVertex = vertexCount;
            WriteBoundaryVertex(left, bottom, ref vertexCount);
            WriteBoundaryVertex(left, top, ref vertexCount);
            WriteBoundaryVertex(right, bottom, ref vertexCount);
            WriteBoundaryVertex(right, top, ref vertexCount);
            _boundaryTriangles[indexCount++] = firstVertex;
            _boundaryTriangles[indexCount++] = firstVertex + 1;
            _boundaryTriangles[indexCount++] = firstVertex + 2;
            _boundaryTriangles[indexCount++] = firstVertex + 3;
            _boundaryTriangles[indexCount++] = firstVertex + 2;
            _boundaryTriangles[indexCount++] = firstVertex + 1;
        }

        private void WriteBoundaryVertex(float x, float y, ref int vertexCount)
        {
            _boundaryVertices[vertexCount] = new Vector3(x, y, 0f);
            _boundaryUv[vertexCount] = new Vector2(x, y);
            _boundaryLightingData[vertexCount] = Vector2.zero;
            vertexCount++;
        }

        private void UpdateTransitMesh(Mesh mesh, Rect coverageRect, int worldHeight)
        {
            UpdateBandMesh(
                mesh,
                coverageRect,
                bottom: worldHeight,
                top: worldHeight + TransitHeight,
                tileWidth: TransitTileWidth,
                uvProjectionHeight: TransitHeight,
                emissionMask: 1f);
        }

        private void UpdatePerspectiveMesh(Mesh mesh, Rect coverageRect, int worldHeight)
        {
            float bottom = worldHeight + TransitHeight;
            UpdateBandMesh(
                mesh,
                coverageRect,
                bottom,
                top: bottom + PerspectiveHeight,
                tileWidth: PerspectiveTileWidth,
                uvProjectionHeight: PerspectiveHeight,
                emissionMask: 1f);
        }

        private void UpdateBandMesh(
            Mesh mesh,
            Rect coverageRect,
            float bottom,
            float top,
            float tileWidth,
            float uvProjectionHeight,
            float emissionMask)
        {
            float clippedBottom = Mathf.Max(coverageRect.yMin, bottom);
            float clippedTop = Mathf.Min(coverageRect.yMax, top);
            if (coverageRect.xMax <= coverageRect.xMin ||
                clippedTop <= clippedBottom || tileWidth <= 0f ||
                uvProjectionHeight <= 0f)
            {
                mesh.Clear(keepVertexLayout: false);
                return;
            }

            float left = coverageRect.xMin;
            float right = coverageRect.xMax;
            _quadVertices[0] = new Vector3(left, clippedBottom, 0f);
            _quadVertices[1] = new Vector3(left, clippedTop, 0f);
            _quadVertices[2] = new Vector3(right, clippedBottom, 0f);
            _quadVertices[3] = new Vector3(right, clippedTop, 0f);
            float uLeft = left / tileWidth;
            float uRight = right / tileWidth;
            float vBottom = (clippedBottom - bottom) / uvProjectionHeight;
            float vTop = (clippedTop - bottom) / uvProjectionHeight;
            _quadUv[0] = new Vector2(uLeft, vBottom);
            _quadUv[1] = new Vector2(uLeft, vTop);
            _quadUv[2] = new Vector2(uRight, vBottom);
            _quadUv[3] = new Vector2(uRight, vTop);
            Vector2 lightingData = new(emissionMask, 0f);
            _quadLightingData[0] = lightingData;
            _quadLightingData[1] = lightingData;
            _quadLightingData[2] = lightingData;
            _quadLightingData[3] = lightingData;

            mesh.Clear(keepVertexLayout: false);
            mesh.SetVertices(_quadVertices);
            mesh.SetUVs(channel: 0, _quadUv);
            mesh.SetUVs(channel: 1, _quadLightingData);
            mesh.SetTriangles(QuadTriangles, submesh: 0, calculateBounds: true);
        }

        private void BindBandObject(
            string objectName,
            Mesh mesh,
            Material material,
            int sortingOrder)
        {
            Transform? existingTransform = transform.Find(objectName);
            GameObject bandObject;
            if (existingTransform == null)
            {
                bandObject = new GameObject(objectName);
                bandObject.transform.SetParent(transform, worldPositionStays: false);
            }
            else
            {
                bandObject = existingTransform.gameObject;
            }

            bandObject.layer = gameObject.layer;
            bandObject.transform.SetLocalPositionAndRotation(
                Vector3.zero,
                Quaternion.identity);
            bandObject.transform.localScale = Vector3.one;
            MeshFilter meshFilter = GetOrAddComponent<MeshFilter>(bandObject);
            MeshRenderer meshRenderer = GetOrAddComponent<MeshRenderer>(bandObject);
            meshFilter.sharedMesh = mesh;
            meshRenderer.sharedMaterial = material;
            meshRenderer.sortingOrder = sortingOrder;
            bandObject.SetActive(true);
        }

        private static T GetOrAddComponent<T>(GameObject gameObject)
            where T : Component
        {
            T? component;
            bool found = gameObject.TryGetComponent(out component);

            // TryGetComponent performs the native lookup again. This matters after a
            // domain reload, where a restored child can retain a stale managed reference
            // even though its native component was removed during teardown.
            if (!found || component == null)
            {
                component = gameObject.AddComponent<T>();
            }

            if (component == null)
            {
                throw new MissingComponentException(
                    $"Failed to attach required component {typeof(T).Name} to " +
                    $"surface object '{gameObject.name}'.");
            }

            return component;
        }

        private static Material CreateMaterial(
            Shader shader,
            string materialName,
            Texture2D texture,
            Color emissionColor,
            float emissionStrength,
            float occupancy,
            Vector2 baseMapTileCount,
            Vector2 worldSize,
            SurfaceKind kind)
        {
            var material = new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.DontSave,
            };
            RequireShaderProperties(material);
            material.SetTexture(BaseMapId, texture);
            material.SetColor(EmissionColorId, emissionColor);
            material.SetFloat(EmissionStrengthId, emissionStrength);
            material.SetFloat(OccupancyId, occupancy);
            material.SetVector(
                BaseMapTileCountId,
                new Vector4(baseMapTileCount.x, baseMapTileCount.y, 0f, 0f));
            material.SetVector(
                WorldSizeId,
                new Vector4(worldSize.x, worldSize.y, 0f, 0f));
            material.EnableKeyword(kind switch
            {
                SurfaceKind.RedRock => RedRockKeyword,
                SurfaceKind.Transit => TransitKeyword,
                SurfaceKind.Perspective => PerspectiveKeyword,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown surface kind."),
            });
            return material;
        }

        private static void RequireShaderProperties(Material material)
        {
            string[] requiredProperties =
            [
                "_BaseMap",
                "_EmissionColor",
                "_EmissionStrength",
                "_Occupancy",
                "_BaseMapTileCount",
                "_WorldSize",
            ];
            foreach (string propertyName in requiredProperties)
            {
                if (!material.HasProperty(propertyName))
                {
                    throw new InvalidOperationException(
                        $"World surface shader '{material.shader.name}' is missing required property " +
                        $"'{propertyName}'. Client graphics settings cannot be applied.");
                }
            }
        }

        private static void ApplyMaterialConfig(
            Material material,
            Color emissionColor,
            float emissionStrength,
            float occupancy)
        {
            material.SetColor(EmissionColorId, emissionColor);
            material.SetFloat(EmissionStrengthId, emissionStrength);
            material.SetFloat(OccupancyId, occupancy);
        }

        private static Mesh CreateMesh(string meshName)
        {
            var mesh = new Mesh
            {
                name = meshName,
                hideFlags = HideFlags.DontSave,
            };
            mesh.MarkDynamic();
            return mesh;
        }

        private static Vector2 GetTerrainSheetTileCount(Texture2D texture)
        {
            const int tileSize = RenderingConstants.CELL_SIZE;
            if (texture.width <= 0 || texture.height <= 0 ||
                texture.width % tileSize != 0 || texture.height % tileSize != 0)
            {
                throw new InvalidOperationException(
                    $"Surface terrain sheet '{texture.name}' dimensions " +
                    $"{texture.width}x{texture.height} must be positive multiples " +
                    $"of the terrain tile size {tileSize}.");
            }

            return new Vector2(texture.width / tileSize, texture.height / tileSize);
        }

        private void SetMaterialWorldSize(int worldWidth, int worldHeight)
        {
            Material transitMaterial = _transitMaterial ??
                throw new InvalidOperationException(
                    "SurfaceRenderer transit material is not initialized.");
            Material perspectiveMaterial = _perspectiveMaterial ??
                throw new InvalidOperationException(
                    "SurfaceRenderer perspective material is not initialized.");
            Material redRockMaterial = _redRockMaterial ??
                throw new InvalidOperationException(
                    "SurfaceRenderer redrock material is not initialized.");
            Vector4 worldSize = new(worldWidth, worldHeight, 0f, 0f);
            transitMaterial.SetVector(WorldSizeId, worldSize);
            perspectiveMaterial.SetVector(WorldSizeId, worldSize);
            redRockMaterial.SetVector(WorldSizeId, worldSize);
        }

        private static void DrawLightingMesh(
            CommandBuffer commandBuffer,
            Mesh mesh,
            Material material)
        {
            if (mesh.vertexCount == 0)
            {
                return;
            }

            int pass = material.FindPass("LightingMaterialField");
            if (pass < 0)
            {
                throw new InvalidOperationException(
                    $"Surface material '{material.name}' is missing LightingMaterialField pass.");
            }

            commandBuffer.DrawMesh(
                mesh,
                Matrix4x4.identity,
                material,
                submeshIndex: 0,
                shaderPass: pass);
        }

        private void UnregisterLightingContributor()
        {
            if (!_registered)
            {
                return;
            }

            _lightingGeometryRegistry?.Unregister(this);
            _registered = false;
        }

        private void DestroyOwnedChild(string objectName)
        {
            Transform? ownedChild = transform.Find(objectName);
            if (ownedChild != null)
            {
                DestroyOwnedObject(ownedChild.gameObject);
            }
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
