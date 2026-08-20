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

        // Below native resolution on purpose. Stars are a tight core plus a wide
        // low-amplitude wing - there is no high-frequency detail to preserve -
        // and the UI samples this with bilinear filtering, which softens the
        // upscale in a way that suits a point-spread function. Rendering the
        // whole screen every frame at native resolution for a background would
        // be the expensive way to get the same image.
        [SerializeField]
        private int _height = 900;

        private RenderTexture? _texture;

        public RenderTexture? Texture => _texture;

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

            // Blitting from whiteTexture rather than null: a null source leaves
            // the bound source texture undefined, and the shader ignores it
            // either way.
            Graphics.Blit(Texture2D.whiteTexture, _texture, _starfieldMaterial);
        }

        private void LateUpdate()
        {
            RenderNow();
        }

        private void EnsureTexture()
        {
            int height = Mathf.Max(_height, 64);

            // Clamped, because Screen.width/height do not mean the game view
            // outside Play Mode - in the editor they can report whichever EditorWindow
            // is current, which produced a 3.6:1 target for a 1.7:1 screen. The
            // Image is ScaleAndCrop so a mismatch crops instead of stretching,
            // but there is no reason to allocate an absurd texture either.
            float rawAspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f;
            float aspect = Mathf.Clamp(rawAspect, 1f, 2.4f);
            int width = Mathf.Max(Mathf.RoundToInt(height * aspect), 64);

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
