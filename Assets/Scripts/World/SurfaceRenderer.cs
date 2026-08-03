#nullable enable

using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Game.Managers;
using UnityEngine;

namespace Fodinae.World
{
    public class SurfaceRenderer : MonoBehaviour
    {
        [Header("Materials")]
        [SerializeField]
        private Material? _transitMaterial;
        [SerializeField]
        private Material? _perspectiveMaterial;

        [Header("Settings")]
        [SerializeField]
        private string _transitTexturePath = "transit";
        [SerializeField]
        private string _perspectiveTexturePath = "perspective";
        [SerializeField]
        private int _transitSortingOrder = -501;
        [SerializeField]
        private int _perspectiveSortingOrder = -502;

        private const float TRANSIT_HEIGHT = 2f;
        private const float PERSPECTIVE_HEIGHT = 2f;
        private const float TILE_SIZE = 32f;
        private Mesh? _transitMesh;
        private Mesh? _perspectiveMesh;
        private MeshFilter? _transitFilter;
        private MeshFilter? _perspectiveFilter;
        private MeshRenderer? _transitRenderer;
        private MeshRenderer? _perspectiveRenderer;
        private bool _ownsPerspectiveMaterial;

        private readonly Vector2[] _uvTransit = new Vector2[4];
        private readonly Vector2[] _uvPers = new Vector2[4];
        private readonly Vector3[] _verticesTransit = new Vector3[4];
        private readonly Vector3[] _verticesPers = new Vector3[4];
        private static readonly int[] Triangles = { 0, 1, 2, 3, 2, 1 };

        private Camera? _mainCamera;
        private bool _texturesLoading;
        private float _lastCameraX = float.NaN;
        private float _lastCameraOrthoSize = float.NaN;
        private float _lastCameraAspect = float.NaN;
        private int _lastWorldHeight = int.MinValue;

        public void SetMaterials(Material? transitMaterial, Material? perspectiveMaterial)
        {
            _transitMaterial = transitMaterial;
            _perspectiveMaterial = perspectiveMaterial;
            if (_transitMaterial != null && _transitMaterial == _perspectiveMaterial)
            {
                Material sharedMaterial = _perspectiveMaterial;
                _perspectiveMaterial = new Material(sharedMaterial)
                {
                    name = $"{sharedMaterial.name} (Perspective)",
                };
                _ownsPerspectiveMaterial = true;
            }
        }

        protected void Start()
        {
            _mainCamera = Camera.main;

            var transitGO = new GameObject("SurfaceTransit");
            transitGO.transform.SetParent(transform, false);
            _transitFilter = transitGO.AddComponent<MeshFilter>();
            _transitRenderer = transitGO.AddComponent<MeshRenderer>();
            _transitRenderer.sortingOrder = _transitSortingOrder;
            _transitMesh = new Mesh();
            _transitMesh.MarkDynamic();
            _transitMesh.vertices = _verticesTransit;
            _transitMesh.uv = _uvTransit;
            _transitMesh.triangles = Triangles;
            _transitFilter.mesh = _transitMesh;

            var persGO = new GameObject("SurfacePerspective");
            persGO.transform.SetParent(transform, false);
            _perspectiveFilter = persGO.AddComponent<MeshFilter>();
            _perspectiveRenderer = persGO.AddComponent<MeshRenderer>();
            _perspectiveRenderer.sortingOrder = _perspectiveSortingOrder;
            _perspectiveMesh = new Mesh();
            _perspectiveMesh.MarkDynamic();
            _perspectiveMesh.vertices = _verticesPers;
            _perspectiveMesh.uv = _uvPers;
            _perspectiveMesh.triangles = Triangles;
            _perspectiveFilter.mesh = _perspectiveMesh;

            if (_transitMaterial == null)
            {
                throw new InvalidOperationException("[SurfaceRenderer] Transit material is not assigned in the inspector");
            }

            if (_perspectiveMaterial == null)
            {
                throw new InvalidOperationException("[SurfaceRenderer] Perspective material is not assigned in the inspector");
            }

            // These materials are owned by the component. Using .material
            // would ask Unity to instantiate another material per renderer.
            _transitRenderer.sharedMaterial = _transitMaterial;
            _perspectiveRenderer.sharedMaterial = _perspectiveMaterial;

            LoadTexturesAsync().Forget();
        }

        private async UniTaskVoid LoadTexturesAsync()
        {
            if (_texturesLoading)
            {
                return;
            }

            _texturesLoading = true;

            try
            {
                var loader = ServiceLocator.Resolve<IAssetLoader>() as ClientAssetLoader;
                if (loader == null)
                {
                    throw new InvalidOperationException("[SurfaceRenderer] ClientAssetLoader is not registered");
                }

                var transitTex = await loader.GetTextureAsync(_transitTexturePath);
                if (transitTex != null && _transitMaterial != null)
                {
                    _transitMaterial.mainTexture = transitTex;
                }

                var persTex = await loader.GetTextureAsync(_perspectiveTexturePath);
                if (persTex != null && _perspectiveMaterial != null)
                {
                    _perspectiveMaterial.mainTexture = persTex;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SurfaceRenderer] Failed to load textures: {ex.Message}");
                throw;
            }
            finally
            {
                _texturesLoading = false;
            }
        }

        protected void LateUpdate()
        {
            if (_mainCamera == null)
            {
                return;
            }

            var mapManager = ServiceLocator.Resolve<MapManager>();
            if (mapManager == null)
            {
                return;
            }

            int worldHeight = mapManager.WorldHeight;
            float camX = _mainCamera.transform.position.x;
            float cameraOrthoSize = _mainCamera.orthographicSize;
            float cameraAspect = _mainCamera.aspect;
            if (Mathf.Approximately(_lastCameraX, camX) &&
                Mathf.Approximately(_lastCameraOrthoSize, cameraOrthoSize) &&
                Mathf.Approximately(_lastCameraAspect, cameraAspect) &&
                _lastWorldHeight == worldHeight)
            {
                return;
            }

            _lastCameraX = camX;
            _lastCameraOrthoSize = cameraOrthoSize;
            _lastCameraAspect = cameraAspect;
            _lastWorldHeight = worldHeight;

            float halfScreenW = cameraOrthoSize * cameraAspect;

            float left = camX - halfScreenW;
            float right = camX + halfScreenW;
            float baseY = worldHeight;

            UpdateTransit(left, right, baseY, camX);
            UpdatePerspective(left, right, baseY, camX);
        }

        private void UpdateTransit(float left, float right, float baseY, float camX)
        {
            if (_transitMesh == null)
            {
                return;
            }

            float uLeft = -(left - (Mathf.Floor(left / TILE_SIZE) * TILE_SIZE)) / TILE_SIZE;
            float uRight = uLeft + ((left - right) / TILE_SIZE);

            _uvTransit[0] = new Vector2(uLeft, 0f);
            _uvTransit[1] = new Vector2(uLeft, 1f);
            _uvTransit[2] = new Vector2(uRight, 0f);
            _uvTransit[3] = new Vector2(uRight, 1f);

            _verticesTransit[0] = new Vector3(left, baseY, 0f);
            _verticesTransit[1] = new Vector3(left, baseY + TRANSIT_HEIGHT, 0f);
            _verticesTransit[2] = new Vector3(right, baseY, 0f);
            _verticesTransit[3] = new Vector3(right, baseY + TRANSIT_HEIGHT, 0f);

            _transitMesh.vertices = _verticesTransit;
            _transitMesh.uv = _uvTransit;
            _transitMesh.bounds = new Bounds(new Vector3(camX, baseY + 1f, 0f), new Vector3(100f, 100f, 10f));
        }

        private void UpdatePerspective(float left, float right, float baseY, float camX)
        {
            if (_perspectiveMesh == null)
            {
                return;
            }

            const float PERS_TILE_SIZE = 5f;
            float uLeft = -(left - (Mathf.Floor(left / PERS_TILE_SIZE) * PERS_TILE_SIZE)) / PERS_TILE_SIZE;
            float uRight = uLeft + ((left - right) / PERS_TILE_SIZE);

            float uMid = 0.5f * (uLeft + uRight);
            float uWidth = uRight - uLeft;
            float persLeft = uMid - (0.5f * uWidth);
            float persRight = uMid + (0.5f * uWidth);

            _uvPers[0] = new Vector2(persLeft, 0f);
            _uvPers[1] = new Vector2(persLeft, 1f);
            _uvPers[2] = new Vector2(persRight, 0f);
            _uvPers[3] = new Vector2(persRight, 1f);

            _verticesPers[0] = new Vector3(left, baseY + TRANSIT_HEIGHT, 0f);
            _verticesPers[1] = new Vector3(left, baseY + TRANSIT_HEIGHT + PERSPECTIVE_HEIGHT, 0f);
            _verticesPers[2] = new Vector3(right, baseY + TRANSIT_HEIGHT, 0f);
            _verticesPers[3] = new Vector3(right, baseY + TRANSIT_HEIGHT + PERSPECTIVE_HEIGHT, 0f);

            _perspectiveMesh.vertices = _verticesPers;
            _perspectiveMesh.uv = _uvPers;
            _perspectiveMesh.bounds = new Bounds(new Vector3(camX, baseY + 3f, 0f), new Vector3(100f, 100f, 10f));
        }

        protected void OnDestroy()
        {
            if (_transitMesh != null)
            {
                Destroy(_transitMesh);
                _transitMesh = null;
            }

            if (_perspectiveMesh != null)
            {
                Destroy(_perspectiveMesh);
                _perspectiveMesh = null;
            }

            if (_ownsPerspectiveMaterial && _perspectiveMaterial != null)
            {
                Destroy(_perspectiveMaterial);
                _perspectiveMaterial = null;
                _ownsPerspectiveMaterial = false;
            }
        }
    }
}
