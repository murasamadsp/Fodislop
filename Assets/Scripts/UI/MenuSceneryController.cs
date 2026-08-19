#nullable enable

using UnityEngine;

namespace Fodinae.UI
{
    [ExecuteAlways]
    public class MenuSceneryController : MonoBehaviour
    {
        // The planet is drawn around 800 screen pixels wide, so this renders at
        // better than 2x and downsamples - which is what keeps the surface crisp
        // and the thin orbit line from breaking up.
        [SerializeField]
        private int _renderTextureWidth = 1800;
        [SerializeField]
        private int _renderTextureHeight = 1800;

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

            if (_cameraTarget == null)
            {
                // HDR format so the post-process Bloom pass has values above 1.0
                // to threshold against (the star/window emissives rely on this).
                // ARGBHalf specifically (not DefaultHDR) - DefaultHDR resolved
                // to a format with no real alpha channel on this system, so
                // the "transparent" clear color always came back opaque and
                // the whole render showed as a solid black box in the UI.
                _cameraTarget = new RenderTexture(_renderTextureWidth, _renderTextureHeight, 16, RenderTextureFormat.ARGBHalf)
                {
                    name = "MenuSceneryRT_Premultiplied",
                    antiAliasing = 4,

                    // Default wrap mode is Repeat; bilinear sampling near the
                    // Image element's UV edges then blends in texels from the
                    // opposite side of the texture, showing up as a thin seam
                    // line along the render bounds.
                    wrapMode = TextureWrapMode.Clamp,
                };
                _cameraTarget.Create();
            }

            if (_outputTexture == null)
            {
                // No depth and no MSAA: this one is only ever a blit destination.
                _outputTexture = new RenderTexture(_renderTextureWidth, _renderTextureHeight, 0, RenderTextureFormat.ARGBHalf)
                {
                    name = "MenuSceneryRT",
                    wrapMode = TextureWrapMode.Clamp,
                };
                _outputTexture.Create();
            }

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
            if (_cameraTarget == null || _outputTexture == null || _resolveMaterial == null)
            {
                return;
            }

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
