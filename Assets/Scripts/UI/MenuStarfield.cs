#nullable enable

using UnityEngine;

namespace Fodinae.UI
{
    // Draws the menu's starfield into a RenderTexture with no camera and no
    // geometry, so MainMenu can show it as a plain UI Image.
    //
    // It used to be a quad parented to a backdrop camera on its own layer, and
    // that is what put the sky on top of the game. The quad is world geometry,
    // its shader sits in the Background queue with ZTest Always and derives its
    // coordinates from screen position, so ANY camera that renders it repaints
    // the entire frame before that camera draws anything else. MainGame's camera
    // has cullingMask Everything, and MainMenu is not unloaded when the game
    // starts - it lives until MainMenu.OnWorldLoaded - so the overlap was
    // guaranteed, not accidental.
    //
    // Culling masks and layers were the wrong tool for that: a layer only helps
    // against a camera that opts out, and every camera in this project opts in
    // to everything. Removing the geometry removes the failure mode instead of
    // guarding it. The shader needs no mesh - it reads
    // positionCS.xy / _ScreenParams.xy - so a full-screen blit is all it ever
    // needed. (UnpremultiplyAlpha.shader already proves TransformObjectToHClip
    // behaves under Graphics.Blit in this project.)
    [ExecuteAlways]
    public sealed class MenuStarfield : MonoBehaviour
    {
        [SerializeField]
        private Material? _starfieldMaterial;

        private int _targetWidth = 1920;
        private int _targetHeight = 1080;

        private RenderTexture? _texture;

        public RenderTexture? Texture => _texture;

        public void SetDisplaySize(int width, int height)
        {
            int w = Mathf.Max(width, 64);
            int h = Mathf.Max(height, 64);

            if (_texture != null && _texture.width == w && _texture.height == h)
            {
                return;
            }

            _targetWidth = w;
            _targetHeight = h;

            ReleaseTexture();
            EnsureTexture();
        }

        private void OnEnable()
        {
            EnsureTexture();
        }

        private void OnDisable()
        {
            ReleaseTexture();
        }

        private void OnDestroy()
        {
            ReleaseTexture();
        }

        private Vector2 _smoothParallax;

        // Draws one frame of the starfield. Public so the editor capture tool can
        // drive it: LateUpdate does not run on demand outside Play Mode, and the
        // sky is invisible to every camera-based capture.
        public void RenderNow()
        {
            if (_starfieldMaterial == null)
            {
                return;
            }

            EnsureTexture();
            if (_texture == null)
            {
                return;
            }

            Vector2 targetParallax = Vector2.zero;
            if (Application.isPlaying && Screen.width > 0 && Screen.height > 0)
            {
                var mouse = UnityEngine.InputSystem.Mouse.current;
                if (mouse != null)
                {
                    Vector2 mousePos = mouse.position.ReadValue();
                    float normX = (mousePos.x / Screen.width) - 0.5f;
                    float normY = (mousePos.y / Screen.height) - 0.5f;
                    targetParallax = new Vector2(normX * -0.006f, normY * -0.006f);
                }
            }

            _smoothParallax = Vector2.Lerp(_smoothParallax, targetParallax, Time.unscaledDeltaTime * 3.0f);

            float currentTime = Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartup;
            _starfieldMaterial.SetFloat("_ShaderTime", currentTime);
            _starfieldMaterial.SetFloat("_Aspect", (float)_texture.width / Mathf.Max(_texture.height, 1));
            _starfieldMaterial.SetVector("_ParallaxOffset", new Vector4(_smoothParallax.x, _smoothParallax.y, 0f, 0f));
            Graphics.Blit(Texture2D.whiteTexture, _texture, _starfieldMaterial);
        }

        private void LateUpdate()
        {
            RenderNow();
        }

        private void EnsureTexture()
        {
            int width = _targetWidth;
            int height = _targetHeight;

            if (_texture != null && _texture.width == width && _texture.height == height)
            {
                return;
            }

            ReleaseTexture();

            // HDR: bright stars are deliberately over-range so the menu's bloom
            // has something to catch.
            _texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGBHalf)
            {
                name = "MenuStarfieldRT",

                // Clamp, not the default Repeat: the UI Image samples right up
                // to the edge, and Repeat wraps in texels from the far side as a
                // visible seam.
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            _texture.Create();
        }

        private void ReleaseTexture()
        {
            if (_texture == null)
            {
                return;
            }

            _texture.Release();
            if (Application.isPlaying)
            {
                Destroy(_texture);
            }
            else
            {
                DestroyImmediate(_texture);
            }

            _texture = null;
        }
    }
}
