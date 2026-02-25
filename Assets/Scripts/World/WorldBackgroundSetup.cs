using UnityEngine;

namespace Fodinae.Assets.Scripts.World
{
    /// <summary>
    /// Automatically sets up the world background renderer in the scene.
    /// This ensures the world is always rendered as a background layer.
    /// </summary>
    [ExecuteAlways]
    public class WorldBackgroundSetup : MonoBehaviour
    {
        [Header("Background Renderer Settings")]
        [Tooltip("Prefab for the world background renderer")]
        [SerializeField] private WorldBackgroundRenderer _backgroundRendererPrefab;
        
        [Tooltip("Parent object for the background renderer")]
        [SerializeField] private Transform _backgroundParent;

        private WorldBackgroundRenderer _backgroundRenderer;

        void Awake()
        {
            SetupBackgroundRenderer();
        }

        void Update()
        {
            // Ensure the background renderer stays properly configured
            if (_backgroundRenderer != null)
            {
                EnsureBackgroundConfiguration();
            }
        }

        private void SetupBackgroundRenderer()
        {
            // Check if we already have a background renderer in the scene
            _backgroundRenderer = FindObjectOfType<WorldBackgroundRenderer>();
            
            if (_backgroundRenderer == null)
            {
                // Create a new background renderer
                if (_backgroundRendererPrefab != null)
                {
                    // Instantiate from prefab
                    _backgroundRenderer = Instantiate(_backgroundRendererPrefab, transform);
                    _backgroundRenderer.name = "WorldBackgroundRenderer";
                }
                else
                {
                    // Create a new GameObject with the component
                    var backgroundGO = new GameObject("WorldBackgroundRenderer");
                    _backgroundRenderer = backgroundGO.AddComponent<WorldBackgroundRenderer>();
                    
                    // Set up basic configuration
                    var meshRenderer = _backgroundRenderer.GetComponent<MeshRenderer>();
                    if (meshRenderer != null)
                    {
                        meshRenderer.sortingOrder = -1000;
                    }
                    
                    var transformComp = _backgroundRenderer.GetComponent<Transform>();
                    if (transformComp != null)
                    {
                        transformComp.position = new Vector3(0, 0, -10);
                    }
                }

                // Set parent if specified
                if (_backgroundParent != null)
                {
                    _backgroundRenderer.transform.SetParent(_backgroundParent);
                }

                Debug.Log("WorldBackgroundRenderer automatically created and configured");
            }
            else
            {
                // Check if this is the same instance as the one we're managing
                if (_backgroundRenderer != this.GetComponent<WorldBackgroundRenderer>())
                {
                    Debug.Log("WorldBackgroundRenderer already exists in scene - using existing instance");
                    
                    // Ensure it's properly configured
                    EnsureBackgroundConfiguration();
                }
                else
                {
                    Debug.Log("WorldBackgroundRenderer is this component - no action needed");
                }
            }
        }

        private void EnsureBackgroundConfiguration()
        {
            if (_backgroundRenderer == null) return;

            var renderer = _backgroundRenderer.GetComponent<MeshRenderer>();
            var transform = _backgroundRenderer.GetComponent<Transform>();

            // Ensure proper background configuration
            if (renderer.sortingOrder != -1000)
            {
                renderer.sortingOrder = -1000;
            }

            if (transform.position.z != -10f)
            {
                var pos = transform.position;
                pos.z = -10f;
                transform.position = pos;
            }

            // Ensure it's using the correct shader for 2D rendering
            if (renderer.sharedMaterial != null && 
                renderer.sharedMaterial.shader.name != "Universal Render Pipeline/Unlit")
            {
                renderer.sharedMaterial.shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
        }

        /// <summary>
        /// Manual method to force setup of background renderer (for editor use)
        /// </summary>
        public void ForceSetup()
        {
            SetupBackgroundRenderer();
        }

        /// <summary>
        /// Get the current background renderer instance
        /// </summary>
        public WorldBackgroundRenderer GetBackgroundRenderer()
        {
            return _backgroundRenderer;
        }
    }
}