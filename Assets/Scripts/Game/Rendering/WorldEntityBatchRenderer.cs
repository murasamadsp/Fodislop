#nullable enable

using Kern.Core.Interfaces.Diagnostics;
using System;
using System.Collections.Generic;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Lifecycle;
using Kern.World;
using Kern.World.Lighting;
using Kern.World.Streaming;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;

namespace Kern.Game
{
    public class WorldEntityBatchRenderer : MonoBehaviour, ILightingGeometryContributor
    {
        // Matches the five-point tail used by the stable June implementation.
        public const int POINT_COUNT = 5;
        private const int VERTS_PER_TENTACLE = POINT_COUNT * 2;
        private const int TRIS_PER_TENTACLE = (POINT_COUNT - 1) * 6;
        private const int INITIAL_CAPACITY = 64;
        private const int BATCH_SORTING_ORDER = -1;
        private const int OVERLAY_BATCH_SORTING_ORDER = 600;
        private const int TENTACLE_SORTING_ORDER = -1;
        private static readonly float VisibilityPrefetchMargin =
            StreamingPolicy.Default.AllocationQuantumCells;

        private static readonly ProfilerMarker _LateUpdateMarker =
            new("Kern.WorldEntities.LateUpdate");

        private static readonly AllocationLedger.Entry _AllocationEntry =
            AllocationLedger.Register("Сущности мира — LateUpdate");

        private readonly List<Tentacle> _tentacles = [];
        private readonly List<SpriteHandle> _sprites = [];
        private readonly SpatialShardGrid<SpriteHandle> _spatialGrid = new();
        private readonly List<SpriteHandle> _candidateSprites = [];

        private readonly List<SpriteHandle> _visibleUnderTentacles = [];
        private readonly List<SpriteHandle> _visibleOverTentacles = [];
        private readonly List<SpriteHandle> _visibleOverlay = [];
        private Vector3[] _verts = new Vector3[VERTS_PER_TENTACLE * INITIAL_CAPACITY];
        private Vector2[] _uvs = new Vector2[VERTS_PER_TENTACLE * INITIAL_CAPACITY];
        private Color32[] _colors = new Color32[VERTS_PER_TENTACLE * INITIAL_CAPACITY];
        private int[] _tris = new int[TRIS_PER_TENTACLE * INITIAL_CAPACITY];
        private Mesh? _mesh;
        private WorldEntityOverlayBatch? _overlayBatch;
        private WorldEntityTextureAtlas? _atlas;
        private int _uploadedTentacleCount = -1;
        private int _uploadedSpriteCount = -1;
        private bool _geometryDirty = true;
        private Vector3 _lastCameraPosition;
        private float _lastCameraOrthographicSize;
        private float _lastCameraAspect;
        private bool _hasCameraState;
        private Rect _cachedVisibleRect;
        private bool _hasCachedVisibleRect;

        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;
        [Inject]
        private ISharedMaterialCache _sharedMaterials = null!;
        [Inject]
        private IGameplayCamera? _gameplayCamera;
        [Inject]
        private LightingGeometryRegistry? _lightingGeometryRegistry;

        // Light-emitting sprites are drawn into the lighting fields from their
        // own mesh. The revision follows only their state — camera motion
        // rebuilds the visible batch every frame and must not re-solve light.
        private Material? _batchMaterial;
        private Mesh? _lightingMesh;
        private Vector3[] _lightingVerts = new Vector3[4];
        private Vector2[] _lightingUvs = new Vector2[4];
        private Color32[] _lightingColors = new Color32[4];
        private int[] _lightingTris = new int[6];
        private ulong _lightingGeometryRevision = 1;
        private int _emissiveStateHash = EmptyEmissiveStateHash;
        private bool _lightingContributorRegistered;
        private const int EmptyEmissiveStateHash = 17;

        public ulong LightingGeometryRevision => _lightingGeometryRevision;

        public sealed class SpriteHandle : WorldEntitySpriteHandle
        {
            internal SpriteHandle(Transform transform, int sortingOrder, bool isStatic, bool emitsLight)
                : base(transform, sortingOrder, isStatic, emitsLight)
            {
            }
        }

        public SpriteHandle RegisterSprite(
            Transform spriteTransform,
            int sortingOrder,
            bool isStatic = false,
            bool emitsLight = false)
        {
            var handle = new SpriteHandle(spriteTransform, sortingOrder, isStatic, emitsLight);
            _sprites.Add(handle);
            _sprites.Sort(static (left, right) => left.SortingOrder.CompareTo(right.SortingOrder));
            _spatialGrid.Insert(handle, spriteTransform.position);
            _geometryDirty = true;
            return handle;
        }

        public void SetSprite(SpriteHandle handle, Sprite? sprite)
        {
            if (sprite != null)
            {
                EnsureRenderer();
                EnsureTextureInAtlas(sprite.texture);
            }

            handle.SetSprite(sprite);
            _geometryDirty = true;
        }

        public void UnregisterSprite(SpriteHandle? handle)
        {
            if (handle != null)
            {
                _spatialGrid.Remove(handle);
                if (_sprites.Remove(handle))
                {
                    _geometryDirty = true;
                }
            }
        }

        public void Register(Tentacle tentacle, Texture2D texture)
        {
            if (tentacle == null || texture == null)
            {
                return;
            }

            EnsureRenderer();
            EnsureTextureInAtlas(texture);
            if (!_tentacles.Contains(tentacle))
            {
                _tentacles.Add(tentacle);
                _geometryDirty = true;
            }
        }

        public void Unregister(Tentacle tentacle, Texture2D texture)
        {
            if (tentacle != null && _tentacles.Remove(tentacle))
            {
                _geometryDirty = true;
            }
        }

        public void MarkDirty(Texture2D texture)
        {
            _geometryDirty = true;
        }

        internal Rect GetAtlasRect(Texture2D texture)
        {
            return _atlas?.GetRect(texture) ?? throw new InvalidOperationException(
                "World-entity atlas is not initialized.");
        }

        protected void Start()
        {
            if (_lightingGeometryRegistry != null && !_lightingContributorRegistered)
            {
                _lightingGeometryRegistry.Register(this);
                _lightingContributorRegistered = true;
            }
        }

        protected void LateUpdate()
        {
            using var marker = _LateUpdateMarker.Auto();
            using var allocationScope = AllocationLedger.Measure(_AllocationEntry);
            for (int i = 0; i < _sprites.Count; i++)
            {
                SpriteHandle handle = _sprites[i];
                handle.RefreshFrameState();
                if (!handle.IsStatic)
                {
                    _spatialGrid.Update(handle, handle.FramePosition);
                }
            }

            UpdateEmissiveRevision();

            Camera? camera = _gameplayCamera?.Camera;
            if (camera != null)
            {
                Vector3 camPos = camera.transform.position;
                float orthoSize = camera.orthographicSize;
                float aspect = camera.aspect;
                bool cameraChanged = !_hasCameraState ||
                    (camPos - _lastCameraPosition).sqrMagnitude > 0.0001f ||
                    Mathf.Abs(orthoSize - _lastCameraOrthographicSize) > 0.001f ||
                    Mathf.Abs(aspect - _lastCameraAspect) > 0.001f;
                if (cameraChanged &&
                    (!TryGetVisibleRect(camera, out Rect currentVisibleRect) ||
                    !_hasCachedVisibleRect ||
                    !Contains(_cachedVisibleRect, currentVisibleRect)))
                {
                    _geometryDirty = true;
                }

                if (cameraChanged)
                {
                    _lastCameraPosition = camPos;
                    _lastCameraOrthographicSize = orthoSize;
                    _lastCameraAspect = aspect;
                    _hasCameraState = true;
                }
            }

            if (!_geometryDirty)
            {
                for (int i = 0; i < _sprites.Count; i++)
                {
                    if (_sprites[i].HasChanged())
                    {
                        _geometryDirty = true;
                        break;
                    }
                }
            }

            if (!_geometryDirty || _mesh == null)
            {
                return;
            }

            bool hasCamera = TryGetVisibleRect(camera, out Rect visibleRect);
            CollectVisibleSprites(hasCamera, visibleRect);
            RebuildMesh(hasCamera, visibleRect);
            _overlayBatch?.Rebuild(_visibleOverlay, GetAtlasRect, _mesh.bounds);

            for (int i = 0; i < _sprites.Count; i++)
            {
                _sprites[i].CaptureState();
            }

            _geometryDirty = false;
        }

        private void CollectVisibleSprites(bool hasCamera, in Rect visibleRect)
        {
            _visibleUnderTentacles.Clear();
            _visibleOverTentacles.Clear();
            _visibleOverlay.Clear();

            List<SpriteHandle> source;
            if (hasCamera)
            {
                _candidateSprites.Clear();
                _spatialGrid.QueryRect(visibleRect, _candidateSprites);
                _candidateSprites.Sort(static (left, right) => left.SortingOrder.CompareTo(right.SortingOrder));
                source = _candidateSprites;
            }
            else
            {
                source = _sprites;
            }

            if (hasCamera)
            {
                _cachedVisibleRect = visibleRect;
                _hasCachedVisibleRect = true;
            }

            for (int i = 0; i < source.Count; i++)
            {
                SpriteHandle handle = source[i];
                if (!IsRenderable(handle) || (hasCamera && !IsInView(handle, true, visibleRect)))
                {
                    continue;
                }

                if (handle.SortingOrder >= OVERLAY_BATCH_SORTING_ORDER)
                {
                    _visibleOverlay.Add(handle);
                }
                else if (handle.SortingOrder < TENTACLE_SORTING_ORDER)
                {
                    _visibleUnderTentacles.Add(handle);
                }
                else
                {
                    _visibleOverTentacles.Add(handle);
                }
            }
        }

        private static bool TryGetVisibleRect(Camera? camera, out Rect visibleRect)
        {
            if (camera == null)
            {
                visibleRect = default;
                return false;
            }

            Vector3 camPos = camera.transform.position;
            float halfHeight = camera.orthographic
                ? camera.orthographicSize
                : Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Abs(camPos.z);
            float halfWidth = halfHeight * camera.aspect;

            visibleRect = new Rect(
                camPos.x - halfWidth - VisibilityPrefetchMargin,
                camPos.y - halfHeight - VisibilityPrefetchMargin,
                (halfWidth + VisibilityPrefetchMargin) * 2f,
                (halfHeight + VisibilityPrefetchMargin) * 2f);
            return true;
        }

        private static bool IsInView(SpriteHandle handle, bool hasCamera, in Rect visibleRect) =>
            !hasCamera || visibleRect.Contains((Vector2)handle.GetWorldPosition());

        private static bool Contains(in Rect outer, in Rect inner) =>
            inner.xMin >= outer.xMin &&
            inner.xMax <= outer.xMax &&
            inner.yMin >= outer.yMin &&
            inner.yMax <= outer.yMax;

        private static bool IsTentacleInView(Tentacle tentacle, bool hasCamera, in Rect visibleRect) =>
            !hasCamera || visibleRect.Contains((Vector2)tentacle.RootPosition);

        private void EnsureRenderer()
        {
            if (_mesh != null)
            {
                return;
            }

            _atlas = new WorldEntityTextureAtlas();

            GameObject renderObject = _sceneObjects.Create("WorldEntityBatch");

            _mesh = new Mesh
            {
                name = "WorldEntityBatch",
                indexFormat = IndexFormat.UInt32,
            };
            _mesh.MarkDynamic();

            var filter = renderObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _mesh;

            var renderer = renderObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _sharedMaterials.GetForTexture(_atlas.Texture);
            _batchMaterial = renderer.sharedMaterial;
            renderer.sortingOrder = BATCH_SORTING_ORDER;

            _overlayBatch = new WorldEntityOverlayBatch(
                _sceneObjects,
                renderer.sharedMaterial,
                OVERLAY_BATCH_SORTING_ORDER);
        }

        private void EnsureTextureInAtlas(Texture2D texture)
        {
            WorldEntityTextureAtlas atlas = _atlas ?? throw new InvalidOperationException(
                "World-entity atlas must exist before a texture is registered.");
            atlas.EnsureTexture(texture);
        }

        private void RebuildMesh(bool hasCamera, in Rect visibleRect)
        {
            Mesh mesh = _mesh ?? throw new InvalidOperationException(
                "Tentacle mesh must exist before geometry is rebuilt.");
            int activeCount = 0;
            for (int i = 0; i < _tentacles.Count; i++)
            {
                Tentacle tentacle = _tentacles[i];
                if (tentacle.IsActive && IsTentacleInView(tentacle, hasCamera, visibleRect))
                {
                    activeCount++;
                }
            }

            int activeSpriteCount = _visibleUnderTentacles.Count + _visibleOverTentacles.Count;
            int vertexCount = (activeCount * VERTS_PER_TENTACLE) + (activeSpriteCount * 4);
            int indexCount = (activeCount * TRIS_PER_TENTACLE) + (activeSpriteCount * 6);
            EnsureGeometryCapacity(vertexCount, indexCount);

            int vertexCursor = 0;
            int indexCursor = 0;
            WriteSprites(_visibleUnderTentacles, ref vertexCursor, ref indexCursor);

            for (int i = 0; i < _tentacles.Count; i++)
            {
                Tentacle tentacle = _tentacles[i];
                if (!tentacle.IsActive || !IsTentacleInView(tentacle, hasCamera, visibleRect))
                {
                    continue;
                }

                int vertexOffset = vertexCursor;
                tentacle.WriteGeometry(
                    _verts,
                    _uvs,
                    vertexOffset,
                    GetAtlasRect(tentacle.Texture));
                for (int vertex = 0; vertex < VERTS_PER_TENTACLE; vertex++)
                {
                    _colors[vertexOffset + vertex] = Color.white;
                }

                int indexOffset = indexCursor;
                for (int segment = 0; segment < POINT_COUNT - 1; segment++)
                {
                    int baseVertex = vertexOffset + (segment * 2);
                    int triangle = indexOffset + (segment * 6);
                    _tris[triangle] = baseVertex;
                    _tris[triangle + 1] = baseVertex + 1;
                    _tris[triangle + 2] = baseVertex + 2;
                    _tris[triangle + 3] = baseVertex + 2;
                    _tris[triangle + 4] = baseVertex + 1;
                    _tris[triangle + 5] = baseVertex + 3;
                }

                vertexCursor += VERTS_PER_TENTACLE;
                indexCursor += TRIS_PER_TENTACLE;
            }

            WriteSprites(_visibleOverTentacles, ref vertexCursor, ref indexCursor);

            vertexCount = vertexCursor;
            indexCount = indexCursor;

            bool topologyChanged =
                _uploadedTentacleCount != activeCount ||
                _uploadedSpriteCount != activeSpriteCount;
            if (topologyChanged)
            {
                mesh.Clear(keepVertexLayout: true);
            }

            if (vertexCount > 0)
            {
                mesh.SetVertices(_verts, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
                mesh.SetUVs(0, _uvs, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
                mesh.SetColors(_colors, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
                if (topologyChanged)
                {
                    mesh.SetIndices(
                        _tris,
                        0,
                        indexCount,
                        MeshTopology.Triangles,
                        0,
                        calculateBounds: false);
                }

                if (hasCamera)
                {
                    mesh.bounds = new Bounds(
                        new Vector3(visibleRect.center.x, visibleRect.center.y, 0f),
                        new Vector3(visibleRect.size.x + 8f, visibleRect.size.y + 8f, 20f));
                }
                else
                {
                    Vector3 minimum = _verts[0];
                    Vector3 maximum = minimum;
                    for (int i = 1; i < vertexCount; i++)
                    {
                        minimum = Vector3.Min(minimum, _verts[i]);
                        maximum = Vector3.Max(maximum, _verts[i]);
                    }

                    mesh.bounds = new Bounds(
                        (minimum + maximum) * 0.5f,
                        maximum - minimum + new Vector3(0.1f, 0.1f, 0.1f));
                }
            }

            _uploadedTentacleCount = activeCount;
            _uploadedSpriteCount = activeSpriteCount;
        }

        private static bool IsRenderable(SpriteHandle handle)
        {
            return handle.Enabled && handle.FrameAlive && handle.Sprite != null;
        }

        private void UpdateEmissiveRevision()
        {
            int hash = EmptyEmissiveStateHash;
            for (int i = 0; i < _sprites.Count; i++)
            {
                SpriteHandle handle = _sprites[i];
                if (!handle.EmitsLight || !IsRenderable(handle))
                {
                    continue;
                }

                hash = HashCode.Combine(
                    hash,
                    handle.Sprite!,
                    handle.FrameLocalToWorld,
                    handle.Color);
            }

            if (hash != _emissiveStateHash)
            {
                _emissiveStateHash = hash;
                _lightingGeometryRevision++;
            }
        }

        public void RenderLightingFields(CommandBuffer commandBuffer, in LightingFieldContext context)
        {
            if (_batchMaterial == null || _atlas == null)
            {
                return;
            }

            int emissiveCount = 0;
            for (int i = 0; i < _sprites.Count; i++)
            {
                if (_sprites[i].EmitsLight && IsRenderable(_sprites[i]))
                {
                    emissiveCount++;
                }
            }

            if (emissiveCount == 0)
            {
                return;
            }

            int pass = _batchMaterial.FindPass(ProjectRuntimeContracts.ShaderPassNames.LightingMaterialField);
            if (pass < 0)
            {
                throw new InvalidOperationException(
                    $"World-entity material '{_batchMaterial.name}' is missing the LightingMaterialField pass.");
            }

            int vertexCount = emissiveCount * 4;
            int indexCount = emissiveCount * 6;
            if (_lightingVerts.Length < vertexCount)
            {
                Array.Resize(ref _lightingVerts, vertexCount);
                Array.Resize(ref _lightingUvs, vertexCount);
                Array.Resize(ref _lightingColors, vertexCount);
            }

            if (_lightingTris.Length < indexCount)
            {
                Array.Resize(ref _lightingTris, indexCount);
            }

            int vertexCursor = 0;
            int indexCursor = 0;
            for (int i = 0; i < _sprites.Count; i++)
            {
                SpriteHandle handle = _sprites[i];
                if (!handle.EmitsLight || !IsRenderable(handle))
                {
                    continue;
                }

                WorldEntityGeometry.WriteSprite(
                    _lightingVerts,
                    _lightingUvs,
                    _lightingColors,
                    _lightingTris,
                    handle,
                    GetAtlasRect(handle.Sprite!.texture),
                    vertexCursor,
                    indexCursor);
                vertexCursor += 4;
                indexCursor += 6;
            }

            if (_lightingMesh == null)
            {
                _lightingMesh = new Mesh
                {
                    name = "WorldEntityLightingField",
                    indexFormat = IndexFormat.UInt32,
                };
                _lightingMesh.MarkDynamic();
            }

            _lightingMesh.Clear(keepVertexLayout: true);
            _lightingMesh.SetVertices(_lightingVerts, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
            _lightingMesh.SetUVs(0, _lightingUvs, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
            _lightingMesh.SetColors(_lightingColors, 0, vertexCount, MeshUpdateFlags.DontRecalculateBounds);
            _lightingMesh.SetIndices(_lightingTris, 0, indexCount, MeshTopology.Triangles, 0, calculateBounds: false);
            commandBuffer.DrawMesh(_lightingMesh, Matrix4x4.identity, _batchMaterial, 0, pass);
        }

        private void WriteSprites(
            List<SpriteHandle> handles,
            ref int vertexCursor,
            ref int indexCursor)
        {
            for (int i = 0; i < handles.Count; i++)
            {
                SpriteHandle handle = handles[i];
                Sprite sprite = handle.Sprite ?? throw new InvalidOperationException(
                    "An enabled batched sprite requires a Sprite.");
                WorldEntityGeometry.WriteSprite(
                    _verts,
                    _uvs,
                    _colors,
                    _tris,
                    handle,
                    GetAtlasRect(sprite.texture),
                    vertexCursor,
                    indexCursor);
                vertexCursor += 4;
                indexCursor += 6;
            }
        }

        private void EnsureGeometryCapacity(int vertexCount, int indexCount)
        {
            int vertexCapacity = Mathf.Max(1, vertexCount);
            if (_verts.Length < vertexCapacity)
            {
                Array.Resize(ref _verts, vertexCapacity);
                Array.Resize(ref _uvs, vertexCapacity);
                Array.Resize(ref _colors, vertexCapacity);
            }

            int indexCapacity = Mathf.Max(1, indexCount);
            if (_tris.Length < indexCapacity)
            {
                Array.Resize(ref _tris, indexCapacity);
            }
        }

        protected void OnDestroy()
        {
            if (_lightingContributorRegistered)
            {
                _lightingGeometryRegistry?.Unregister(this);
                _lightingContributorRegistered = false;
            }

            if (_lightingMesh != null)
            {
                Destroy(_lightingMesh);
                _lightingMesh = null;
            }

            if (_mesh != null)
            {
                Destroy(_mesh);
                _mesh = null;
            }

            _atlas?.Dispose();
            _atlas = null;
            _overlayBatch?.Dispose();
            _overlayBatch = null;
            _tentacles.Clear();
            _sprites.Clear();
        }
    }
}
