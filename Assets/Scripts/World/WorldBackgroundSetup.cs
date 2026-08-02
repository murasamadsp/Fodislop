#nullable enable

using System;
using Fodinae.World.Terrain;
using UnityEngine;

namespace Fodinae.World
{
    public class WorldBackgroundSetup : MonoBehaviour
    {
        private TerrainRenderer? _backgroundRenderer;

        protected void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            SetupBackgroundRenderer();
            EnsureBackgroundConfiguration();
        }

        private void SetupBackgroundRenderer()
        {
            _backgroundRenderer = FindAnyObjectByType<TerrainRenderer>(
                FindObjectsInactive.Include) ??
                throw new InvalidOperationException(
                    "WorldBackgroundSetup requires the scene TerrainRenderer.");
        }

        private void EnsureBackgroundConfiguration()
        {
            if (_backgroundRenderer == null)
            {
                return;
            }

            MeshRenderer? renderer = _backgroundRenderer.GetComponent<MeshRenderer>();
            Transform trans = _backgroundRenderer.transform;

            if (renderer != null && renderer.sortingOrder != -1000)
            {
                renderer.sortingOrder = -1000;
            }

            if (trans.position.z != 0f)
            {
                Vector3 pos = trans.position;
                pos.z = 0f;
                trans.position = pos;
            }
        }

        public TerrainRenderer? GetBackgroundRenderer()
        {
            return _backgroundRenderer;
        }
    }
}
