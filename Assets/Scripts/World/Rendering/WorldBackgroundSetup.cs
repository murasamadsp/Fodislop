#nullable enable

using Fodinae.World.Terrain;
using UnityEngine;
using VContainer;

namespace Fodinae.World
{
    public class WorldBackgroundSetup : MonoBehaviour
    {
        [Inject]
        private TerrainRenderer _backgroundRenderer = null!;

        protected void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            EnsureBackgroundConfiguration();
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

        public TerrainRenderer GetBackgroundRenderer()
        {
            return _backgroundRenderer;
        }
    }
}
