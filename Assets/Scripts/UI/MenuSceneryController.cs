#nullable enable

using UnityEngine;

namespace Fodinae.UI
{
    [ExecuteAlways]
    public class MenuSceneryController : MonoBehaviour
    {
        private Camera? _sceneryCamera;
        private OrbitalStationMotion? _station;
        private Transform? _planet;
        private Transform? _occluder;

        private int _targetWidth = 512;
        private int _targetHeight = 512;

        private RenderTexture? _cameraTarget;
        private RenderTexture? _outputTexture;

        [SerializeField]
        private Material? _resolveMaterialAsset;

        private Material? _resolveMaterial;
        private bool _ownsResolveMaterial;

        public RenderTexture? OutputTexture => _outputTexture;

        public void SetDisplaySize(int width, int height)
        {
            int w = Mathf.Max(width, 64);
            int h = Mathf.Max(height, 64);

            if (_cameraTarget != null && _cameraTarget.width == w && _cameraTarget.height == h)
            {
                return;
            }

            _targetWidth = w;
            _targetHeight = h;

            ReleaseTexture(ref _cameraTarget);
            ReleaseTexture(ref _outputTexture);

            EnsureTargets();
        }

        private void EnsureTargets()
        {
            if (_cameraTarget == null)
            {
                _cameraTarget = new RenderTexture(_targetWidth, _targetHeight, 16, RenderTextureFormat.ARGBHalf)
                {
                    name = "MenuSceneryRT_Premultiplied",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                _cameraTarget.Create();

                if (_sceneryCamera != null)
                {
                    _sceneryCamera.targetTexture = _cameraTarget;
                    _sceneryCamera.ResetAspect();
                    _sceneryCamera.ResetProjectionMatrix();
                }
            }

            if (_outputTexture == null)
            {
                _outputTexture = new RenderTexture(_targetWidth, _targetHeight, 0, RenderTextureFormat.ARGBHalf)
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

            if (_planet != null)
            {
                _planet.localPosition = Vector3.zero;
                var atmo = transform.Find("PlanetAtmosphere");
                if (atmo != null)
                {
                    atmo.localPosition = Vector3.zero;
                }
            }

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
                    Shader? resolve = Shader.Find("Fodinae/UI/UnpremultiplyAlpha");
                    if (resolve == null)
                    {
                        Debug.LogError("[MenuSceneryController] No resolve material assigned and shader 'Fodinae/UI/UnpremultiplyAlpha' not found.");
                    }
                    else
                    {
                        _resolveMaterial = new Material(resolve) { hideFlags = HideFlags.HideAndDontSave };
                        _ownsResolveMaterial = true;
                    }
                }
            }

            _sceneryCamera.allowHDR = true;
            _sceneryCamera.fieldOfView = 36f;
            _sceneryCamera.ResetAspect();
            _sceneryCamera.ResetProjectionMatrix();
            _sceneryCamera.transform.localPosition = new Vector3(0f, 0f, -7.5f);
            _sceneryCamera.transform.localRotation = Quaternion.identity;
            if (_cameraTarget != null)
            {
                _sceneryCamera.targetTexture = _cameraTarget;
            }
        }

        public void ResolveOutput()
        {
            EnsureTargets();
            if (_cameraTarget == null || _outputTexture == null || _resolveMaterial == null)
            {
                return;
            }

            Graphics.Blit(_cameraTarget, _outputTexture, _resolveMaterial);
        }

        private void LateUpdate()
        {
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

        /// <summary>
        /// Calculates the on-screen viewport position for a fixed point along the orbital ring.
        /// </summary>
        public bool TryGetOrbitPointViewportPosition(float angleDegrees, out Vector2 viewportPosition)
        {
            viewportPosition = default;
            if (_sceneryCamera == null)
            {
                return false;
            }

            Transform centerTransform = _planet != null ? _planet : transform;
            const float orbitRadius = 1.72f;
            var orbitTilt = new Vector3(72f, 0f, -19f);
            var localOffset = new Vector3(
                Mathf.Cos(angleDegrees * Mathf.Deg2Rad),
                0f,
                Mathf.Sin(angleDegrees * Mathf.Deg2Rad)) * orbitRadius;
            Quaternion orbitPlane = Quaternion.Euler(orbitTilt);
            Vector3 pointWS = centerTransform.position + (orbitPlane * localOffset);

            Vector3 viewport = _sceneryCamera.WorldToViewportPoint(pointWS);
            if (viewport.z <= 0f)
            {
                return false;
            }

            viewportPosition = new Vector2(viewport.x, viewport.y);
            return true;
        }

        /// <summary>
        /// Calculates the on-screen viewport position for a fixed landing point on the planet's surface.
        /// </summary>
        public bool TryGetPlanetSurfaceViewportPosition(Vector3 localSurfaceDir, out Vector2 viewportPosition)
        {
            viewportPosition = default;
            if (_sceneryCamera == null || _planet == null)
            {
                return false;
            }

            float planetRadius = 0.5f * _planet.lossyScale.x;
            Vector3 pointWS = _planet.position + (localSurfaceDir.normalized * planetRadius);

            Vector3 viewport = _sceneryCamera.WorldToViewportPoint(pointWS);
            if (viewport.z <= 0f)
            {
                return false;
            }

            viewportPosition = new Vector2(viewport.x, viewport.y);
            return true;
        }
    }
}
