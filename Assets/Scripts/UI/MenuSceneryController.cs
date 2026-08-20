#nullable enable

using UnityEngine;

namespace Fodinae.UI
{
    [ExecuteAlways]
    public class MenuSceneryController : MonoBehaviour
    {
        private const int MinimumDisplayDimension = 128;

        // Supersampling factor over the size the UI actually displays this at.
        //
        // Replaces a hardcoded 1800x1800 target with 4x MSAA. Both parts of that
        // were wrong. The size ignored how large the element really is, so it
        // over-rendered on a small window and under-rendered on a large one. And
        // MSAA was the wrong tool entirely: it antialiases triangle coverage, but
        // almost none of this image's aliasing comes from triangle edges. The
        // planet is three smooth shells - the shimmer is in the surface's noise
        // octaves, the cloud bands and the terminator, all of which are shader
        // output that MSAA resolves identically across every sample of a pixel.
        // It was paying for four samples per pixel to fix the one artifact it did
        // not have.
        //
        // Supersampling filters shader detail and geometric edges alike, because
        // it genuinely shades more points. At exactly 2 the downsample below is
        // an exact 2x2 box average, so keep this at 1 or 2 - a higher factor
        // would need a real reduction filter rather than one bilinear tap.
        [SerializeField]
        [Range(1, 2)]
        private int _supersample = 2;

        // Ceiling on the rendered dimension, so a 5K display cannot ask for a
        // target that costs more than the rest of the menu put together.
        [SerializeField]
        private int _maximumRenderDimension = 2048;

        private int _displayWidth;
        private int _displayHeight;

        private Camera? _sceneryCamera;
        private OrbitalStationMotion? _station;
        private Transform? _planet;
        private Transform? _occluder;

        // What the camera draws into. Its contents are premultiplied-alpha,
        // because that is the only blend that lets the atmosphere shell add
        // in-scattered light and attenuate the crust behind it in one pass.
        private RenderTexture? _cameraTarget;

        // What the UI samples: the same image converted to straight alpha.
        private RenderTexture? _outputTexture;

        // Assigned as an asset by BuildMenuSceneryRig. A serialized reference
        // rather than a bare Shader.Find at runtime: a shader reached only
        // through Shader.Find is not counted as used by any asset, so the build
        // shader stripper is free to drop it - and the failure would only show
        // up in a player, as a suddenly invisible atmosphere.
        [SerializeField]
        private Material? _resolveMaterialAsset;

        private Material? _resolveMaterial;
        private bool _ownsResolveMaterial;

        public RenderTexture? OutputTexture => _outputTexture;

        /// <summary>
        /// Tells the rig how many screen pixels the UI element actually covers.
        /// </summary>
        /// <remarks>
        /// The controller has no way to find this out on its own — the element
        /// lives in a UI Toolkit hierarchy in another scene, and its size comes
        /// from USS plus the panel's scaling. Without it the only honest choice
        /// is a guess from the screen size, which is what <see cref="EnsureTargets"/>
        /// falls back to.
        /// </remarks>
        public void SetDisplaySize(int width, int height)
        {
            int clampedWidth = Mathf.Clamp(width, MinimumDisplayDimension, _maximumRenderDimension);
            int clampedHeight = Mathf.Clamp(height, MinimumDisplayDimension, _maximumRenderDimension);
            if (clampedWidth == _displayWidth && clampedHeight == _displayHeight)
            {
                return;
            }

            _displayWidth = clampedWidth;
            _displayHeight = clampedHeight;
            EnsureTargets();
        }

        /// <summary>
        /// Allocates the supersampled camera target and the display-sized output,
        /// reallocating only when the required size changes.
        /// </summary>
        private void EnsureTargets()
        {
            if (_displayWidth <= 0 || _displayHeight <= 0)
            {
                // Nobody has reported the element size yet. The planet is drawn
                // as a square roughly filling the shorter screen axis, which is
                // close enough to keep the menu correct on the first frames and
                // is replaced as soon as MainMenu resolves its layout.
                int fallback = Mathf.Clamp(
                    Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.7f),
                    MinimumDisplayDimension,
                    _maximumRenderDimension);
                _displayWidth = fallback;
                _displayHeight = fallback;
            }

            int supersample = Mathf.Clamp(_supersample, 1, 2);
            int renderWidth = Mathf.Min(_displayWidth * supersample, _maximumRenderDimension);
            int renderHeight = Mathf.Min(_displayHeight * supersample, _maximumRenderDimension);

            bool cameraTargetMatches = _cameraTarget != null &&
                _cameraTarget.width == renderWidth &&
                _cameraTarget.height == renderHeight;
            bool outputMatches = _outputTexture != null &&
                _outputTexture.width == _displayWidth &&
                _outputTexture.height == _displayHeight &&
                !_outputTexture.useMipMap;
            if (cameraTargetMatches && outputMatches)
            {
                return;
            }

            if (!cameraTargetMatches)
            {
                ReleaseTexture(ref _cameraTarget);

                // HDR format so the post-process Bloom pass has values above 1.0
                // to threshold against (the star/window emissives rely on this).
                // ARGBHalf specifically (not DefaultHDR) - DefaultHDR resolved
                // to a format with no real alpha channel on this system, so
                // the "transparent" clear color always came back opaque and
                // the whole render showed as a solid black box in the UI.
                //
                // antiAliasing is left at 1. See _supersample for why MSAA was
                // the wrong mechanism here; it also cost four times this
                // texture's memory, on a target this size the single largest
                // allocation in the menu.
                _cameraTarget = new RenderTexture(renderWidth, renderHeight, 16, RenderTextureFormat.ARGBHalf)
                {
                    name = "MenuSceneryRT_Premultiplied",

                    // Bilinear matters here: at a supersample of 2 the resolve
                    // blit reads exactly the centre of each 2x2 source quad, and
                    // a bilinear tap at that point returns their exact average.
                    // The downsample IS the antialiasing, so a point filter here
                    // would silently throw away three quarters of the work.
                    filterMode = FilterMode.Bilinear,

                    // Default wrap mode is Repeat; bilinear sampling near the
                    // Image element's UV edges then blends in texels from the
                    // opposite side of the texture, showing up as a thin seam
                    // line along the render bounds.
                    wrapMode = TextureWrapMode.Clamp,
                };
                _cameraTarget.Create();
                if (_sceneryCamera != null)
                {
                    _sceneryCamera.targetTexture = _cameraTarget;
                }
            }

            if (!outputMatches)
            {
                ReleaseTexture(ref _outputTexture);

                // Display-sized, so the UI samples it at roughly 1:1 and there is
                // no minification left to filter. That is what removes the need
                // for the mip chain this used to rebuild every single frame:
                // mips existed only because the output was 1800px feeding an
                // ~860px element, and downsampling in the resolve blit solves
                // the same problem once instead of over a whole chain.
                _outputTexture = new RenderTexture(_displayWidth, _displayHeight, 0, RenderTextureFormat.ARGBHalf)
                {
                    name = "MenuSceneryRT",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    useMipMap = false,
                    anisoLevel = 0,
                };
                _outputTexture.Create();
            }
        }

        private void OnEnable()
        {
            _sceneryCamera = GetComponentInChildren<Camera>(includeInactive: true);
            _station = GetComponentInChildren<OrbitalStationMotion>(includeInactive: true);
            _planet = transform.Find("PlanetSurface");

            // Occlusion is tested against the atmosphere shell when present: the
            // dense haze hides the station across a noticeably wider band than
            // the crust alone, and testing the crust radius left the marker drawn
            // over the disc with no visible point inside it.
            _occluder = transform.Find("PlanetAtmosphere") ?? _planet;
            if (_sceneryCamera == null)
            {
                return;
            }

            EnsureTargets();
            if (_resolveMaterial == null)
            {
                if (_resolveMaterialAsset != null)
                {
                    _resolveMaterial = _resolveMaterialAsset;
                }
                else
                {
                    // Editor-only fallback so a rig that has not been rebuilt yet
                    // still renders correctly.
                    Shader? resolve = Shader.Find("Fodinae/UI/UnpremultiplyAlpha");
                    if (resolve == null)
                    {
                        Debug.LogError("[MenuSceneryController] No resolve material assigned and shader 'Fodinae/UI/UnpremultiplyAlpha' not found - the atmosphere will render far too faint.");
                    }
                    else
                    {
                        _resolveMaterial = new Material(resolve) { hideFlags = HideFlags.HideAndDontSave };
                        _ownsResolveMaterial = true;
                    }
                }
            }

            _sceneryCamera.allowHDR = true;
            _sceneryCamera.targetTexture = _cameraTarget;
        }

        // Converts the camera's premultiplied output into the straight-alpha
        // texture the UI samples. Public so the editor capture tool can run the
        // same conversion after a manual Camera.Render and preview exactly what
        // the menu shows.
        public void ResolveOutput()
        {
            EnsureTargets();
            if (_cameraTarget == null || _outputTexture == null || _resolveMaterial == null)
            {
                return;
            }

            // One blit does both jobs. The hardware bilinear tap averages the
            // supersampled premultiplied values - correct in that order, since
            // premultiplied alpha is exactly the representation that stays linear
            // under filtering - and the material then divides out alpha.
            Graphics.Blit(_cameraTarget, _outputTexture, _resolveMaterial);
        }

        private void LateUpdate()
        {
            // Runs a frame behind the camera render, which is invisible here: the
            // scene is a slowly drifting station and an otherwise static planet.
            ResolveOutput();
        }

        private void OnDisable()
        {
            if (_sceneryCamera != null)
            {
                _sceneryCamera.targetTexture = null;
            }
        }

        private void OnDestroy()
        {
            ReleaseTexture(ref _cameraTarget);
            ReleaseTexture(ref _outputTexture);

            // Only destroy the fallback instance this component created; the
            // serialized asset must not be destroyed.
            if (_resolveMaterial != null && _ownsResolveMaterial)
            {
                if (Application.isPlaying)
                {
                    Destroy(_resolveMaterial);
                }
                else
                {
                    DestroyImmediate(_resolveMaterial);
                }
            }

            _resolveMaterial = null;
            _ownsResolveMaterial = false;
        }

        private static void ReleaseTexture(ref RenderTexture? texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.Release();
            if (Application.isPlaying)
            {
                Destroy(texture);
            }
            else
            {
                DestroyImmediate(texture);
            }

            texture = null;
        }

        // Reports the orbiting station's on-screen position as a 0..1 viewport
        // fraction (origin bottom-left, matching Camera.WorldToViewportPoint),
        // so UI Toolkit callers can convert it into their own panel space.
        //
        // Returns false while the station is not actually visible, so a label
        // anchored to it can be hidden rather than left hovering over the disc
        // with nothing underneath.
        public bool TryGetStationViewportPosition(out Vector2 viewportPosition)
        {
            viewportPosition = default;
            if (_sceneryCamera == null || _station == null)
            {
                return false;
            }

            Vector3 stationWS = _station.transform.position;
            Vector3 viewport = _sceneryCamera.WorldToViewportPoint(stationWS);
            if (viewport.z <= 0f)
            {
                return false;
            }

            if (_occluder != null)
            {
                Vector3 cameraWS = _sceneryCamera.transform.position;
                Vector3 toPlanet = _occluder.position - cameraWS;
                Vector3 toStation = stationWS - cameraWS;

                // Occluded when the station is on the far side of the planet's
                // centre AND falls inside its silhouette. A sphere makes this a
                // cheap exact test - no depth buffer read needed.
                if (toStation.magnitude > toPlanet.magnitude)
                {
                    float radius = _occluder.lossyScale.x * 0.5f;
                    float offAxis = Vector3.ProjectOnPlane(toStation, toPlanet.normalized).magnitude;
                    if (offAxis < radius)
                    {
                        return false;
                    }
                }
            }

            viewportPosition = new Vector2(viewport.x, viewport.y);
            return true;
        }
    }
}
