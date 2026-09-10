#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Core.Lifecycle;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using VContainer;

namespace Fodinae.Game
{
    /// <summary>
    /// Renders compatible world sprites and robot tentacles through one mesh,
    /// one material and one runtime texture atlas.
    /// </summary>
    public class WorldEntityBatchRenderer : MonoBehaviour
    {
        // Matches the five-point tail used by the stable June implementation.
        public const int POINT_COUNT = 5;
        private const int VERTS_PER_TENTACLE = POINT_COUNT * 2;
        private const int TRIS_PER_TENTACLE = (POINT_COUNT - 1) * 6;
        private const int INITIAL_CAPACITY = 64;
        private const int BATCH_SORTING_ORDER = -1;
        private const int OVERLAY_BATCH_SORTING_ORDER = 600;
        private const int TENTACLE_SORTING_ORDER = -1;

        private static readonly ProfilerMarker _LateUpdateMarker =
            new("Fodinae.WorldEntities.LateUpdate");

        private readonly List<Tentacle> _tentacles = [];
        private readonly List<SpriteHandle> _sprites = [];

        /// <summary>Видимые спрайты этого кадра, разложенные по слоям отрисовки.</summary>
        /// <remarks>
        /// Списки живут в поле, а не заводятся на кадр: пересборка идёт каждый
        /// кадр, пока в мире хоть что-то движется, и три новых списка на кадр
        /// были бы мусором на ровном месте.
        /// </remarks>
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

        [Inject]
        private ISceneObjectFactory _sceneObjects = null!;
        [Inject]
        private ISharedMaterialCache _sharedMaterials = null!;
        [Inject]
        private IGameplayCamera? _gameplayCamera;

        public sealed class SpriteHandle : WorldEntitySpriteHandle
        {
            internal SpriteHandle(Transform transform, int sortingOrder, bool isStatic = false)
                : base(transform, sortingOrder, isStatic)
            {
            }
        }

        public SpriteHandle RegisterSprite(Transform spriteTransform, int sortingOrder, bool isStatic = false)
        {
            var handle = new SpriteHandle(spriteTransform, sortingOrder, isStatic);
            _sprites.Add(handle);
            _sprites.Sort(static (left, right) => left.SortingOrder.CompareTo(right.SortingOrder));
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
            if (handle != null && _sprites.Remove(handle))
            {
                _geometryDirty = true;
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

        /// <remarks>
        /// ПОРЯДОК ЗДЕСЬ ОБЯЗАТЕЛЕН. Сперва один опрос трансформов на кадр,
        /// и только потом всё остальное: проверка изменений, отбор видимых,
        /// запись геометрии и снимок состояния читают снятые значения и в
        /// движок больше не ходят.
        ///
        /// Раньше опроса как такового не было — каждый из этих проходов
        /// спрашивал трансформ сам. Проходов было пять, и все шли по всему
        /// списку зарегистрированных спрайтов, а не по видимым: здания
        /// сидят в хвосте по порядку сортировки, и город платил за себя
        /// в каждом кадре просто фактом своего существования.
        /// </remarks>
        protected void LateUpdate()
        {
            using var marker = _LateUpdateMarker.Auto();
            for (int i = 0; i < _sprites.Count; i++)
            {
                _sprites[i].RefreshFrameState();
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

            Camera? camera = _gameplayCamera?.Camera;
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

        /// <summary>
        /// Раскладывает видимые спрайты по слоям одним проходом.
        /// </summary>
        /// <remarks>
        /// Границы слоёв — по порядку сортировки, а список отсортирован по нему
        /// же, но опираться на это отбором с ранним выходом нельзя: отсечение по
        /// видимости прореживает список внутри каждого слоя, и выйти раньше
        /// значит потерять спрайт. Проход остаётся один на кадр, и в нём нет ни
        /// одного обращения в движок.
        /// </remarks>
        private void CollectVisibleSprites(bool hasCamera, in Rect visibleRect)
        {
            _visibleUnderTentacles.Clear();
            _visibleOverTentacles.Clear();
            _visibleOverlay.Clear();

            for (int i = 0; i < _sprites.Count; i++)
            {
                SpriteHandle handle = _sprites[i];
                if (!IsRenderable(handle) || !IsInView(handle, hasCamera, visibleRect))
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

            Transform camTransform = camera.transform;
            Vector3 camPos = camTransform.position;
            float halfHeight = camera.orthographic
                ? camera.orthographicSize
                : (Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Abs(camPos.z));
            float halfWidth = halfHeight * camera.aspect;

            const float margin = 8.0f;
            float minX = camPos.x - halfWidth - margin;
            float minY = camPos.y - halfHeight - margin;
            float width = (halfWidth + margin) * 2f;
            float height = (halfHeight + margin) * 2f;

            visibleRect = new Rect(minX, minY, width, height);
            return true;
        }

        private static bool IsInView(SpriteHandle handle, bool hasCamera, in Rect visibleRect)
        {
            if (!hasCamera)
            {
                return true;
            }

            Vector3 pos = handle.GetWorldPosition();
            return pos.x >= visibleRect.xMin && pos.x <= visibleRect.xMax &&
                   pos.y >= visibleRect.yMin && pos.y <= visibleRect.yMax;
        }

        private static bool IsTentacleInView(Tentacle tentacle, bool hasCamera, in Rect visibleRect)
        {
            if (!hasCamera)
            {
                return true;
            }

            Vector3 pos = tentacle.RootPosition;
            return pos.x >= visibleRect.xMin && pos.x <= visibleRect.xMax &&
                   pos.y >= visibleRect.yMin && pos.y <= visibleRect.yMax;
        }

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
                        new Vector3(visibleRect.size.x, visibleRect.size.y, 10f));
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
            if (_mesh != null)
            {
                Destroy(_mesh);
                _mesh = null;
            }

            if (_atlas != null)
            {
                _atlas.Dispose();
                _atlas = null;
            }

            _overlayBatch?.Dispose();
            _overlayBatch = null;

            _tentacles.Clear();
            _sprites.Clear();
        }
    }
}
