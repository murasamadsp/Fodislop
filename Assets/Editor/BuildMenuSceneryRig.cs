#nullable enable

using System;
using Fodinae.Rendering.PostProcessing;
using Fodinae.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Editor
{
    // Builds the whole "living 3D" main menu background rig (red dwarf star,
    // planet, orbiting Federation station, orbit ring, camera, post-process
    // volume) in one deterministic pass. Re-runnable/idempotent by design so
    // it can be re-executed after tuning constants below, instead of chaining
    // many separate MCP calls that each carry their own silent-failure risk
    // (asset-reference fields in particular).
    internal static class BuildMenuSceneryRig
    {
        private const string LayerName = "MenuScenery";
        private const string ProfilePath = "Assets/Settings/MenuSceneryVolumeProfile.asset";

        // Layers this rig used to create and no longer needs. Cleared on every
        // build so the project setting does not keep a name nothing references -
        // "MenuBackdrop" existed only to keep a starfield quad off other
        // cameras, and the starfield is no longer geometry at all.
        // "PlanetPreview" predates this rig and has never been referenced by any
        // scene, prefab or script.
        private static readonly string[] ObsoleteLayerNames = { "MenuBackdrop", "PlanetPreview" };

        // Shared by the moving station and the drawn ring - if these two ever
        // disagree the station visibly drifts off its own orbit line.
        private static readonly Vector3 OrbitTilt = new Vector3(72f, 0f, -19f);

        // Where the offscreen render rig lives. See the comment at the call site.
        private static readonly Vector3 RigParkPosition = new Vector3(0f, 20000f, 0f);

        [MenuItem("Fodinae/Art/Build Menu Scenery Rig")]
        public static void Build()
        {
            int layer = EnsureLayer(LayerName);
            foreach (string obsolete in ObsoleteLayerNames)
            {
                RemoveLayer(obsolete);
            }

            // The stock sphere primitive's silhouette is a visible polygon at the
            // size this planet is drawn, so the rig supplies its own dense mesh.
            BuildPlanetMesh.Build();

            GameObject root = FindOrCreate(null, "MenuScenery");

            // The whole rig is parked far outside every other camera's frustum.
            //
            // The planet, its atmosphere shell and the station have to stay real
            // world geometry - a camera renders them into a RenderTexture that
            // the UI then draws. But MainGame's camera and Bootstrap's are both
            // set to cullingMask Everything, and MainMenu stays loaded until the
            // world has finished loading, so anything this rig leaves at the
            // world origin is rendered by the gameplay camera too.
            //
            // MenuSceneryCamera is a child of this root, so moving the root
            // changes nothing about the framing. Every other camera in the
            // project has a far plane of 1000, so none of them can reach 20km.
            // This is belt to the culling-mask braces in PostProcessController:
            // the mask depends on a layer name resolving, this does not depend
            // on anything.
            root.transform.SetParent(null, worldPositionStays: false);
            root.transform.localPosition = RigParkPosition;
            root.transform.localRotation = Quaternion.identity;

            GameObject? camObj = GameObject.Find("Main Camera");
            if (camObj == null)
            {
                camObj = GameObject.Find("MenuSceneryCamera");
            }

            if (camObj == null)
            {
                Debug.LogError("[BuildMenuSceneryRig] Could not find 'Main Camera' / 'MenuSceneryCamera' to repurpose.");
                return;
            }

            camObj.name = "MenuSceneryCamera";

            // Untagged, explicitly and every rebuild.
            //
            // This object started life as the scene's "Main Camera" and kept the
            // MainCamera tag, which made Camera.main a coin flip: MainMenu is not
            // unloaded when the game starts (see MainMenu.OnWorldLoaded), so both
            // scenes carry a MainCamera-tagged camera for the whole descent -
            // exactly when GameBootstrap.PostStart resolves every manager and
            // those managers cache Camera.main. PostProcessRendererFeature gates
            // its entire pass on that value, so a miss silently moved the game's
            // post-processing onto this camera. Fixing it once in the scene is
            // not enough: this method would hand the tag straight back.
            camObj.tag = "Untagged";
            camObj.transform.SetParent(root.transform, worldPositionStays: false);
            var cam = camObj.GetComponent<Camera>();
            cam.orthographic = false;

            // Wider than the tight framing this replaced - Bloom's glow around
            // the ring/atmosphere/star extends past the planet's silhouette,
            // and a tight frame hard-clips that soft glow into a straight
            // edge right at the RenderTexture boundary (read as a stray
            // "line" cutting through the halo instead of it fading out).
            // Tightened from 46: at that framing the disc filled only half
            // the render target, so most of the texture was empty margin and
            // the planet could never read as large in the menu no matter how
            // big the UI element got. 34 puts the disc at ~70% of the frame
            // while still leaving room for the orbit ring and its bloom.
            cam.fieldOfView = 36f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 60f;
            cam.cullingMask = 1 << layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowHDR = true;

            cam.depth = -101f;
            camObj.transform.localPosition = new Vector3(0f, 0f, -7.5f);
            camObj.transform.localRotation = Quaternion.identity;

            // The gameplay camera gets this same volumeLayerMask/volumeTrigger
            // setup explicitly (see PostProcessController.EnsureCameraSetup) -
            // URP does not reliably default it for a bare Camera otherwise.
            var camData = camObj.GetComponent<UniversalAdditionalCameraData>();
            if (camData == null)
            {
                camData = camObj.AddComponent<UniversalAdditionalCameraData>();
            }

            camData.renderPostProcessing = false;

            // ONLY the MenuScenery layer, not ~0.
            //
            // With ~0 the gameplay post-process volume also applied to this
            // camera, and its Eigengrau component (a deliberate black-level lift
            // for the underground 2D game) flattened the planet's night side into
            // the same olive as the day side - the terminator vanished entirely
            // in Play Mode while still looking correct in the Editor, because
            // PostProcessController does not run outside play. The menu volume
            // lives under this rig root and is moved onto the layer below.
            camData.volumeLayerMask = 1 << layer;
            camData.volumeTrigger = camObj.transform;

            // Lives on the rig root (not the camera) so GetComponentInChildren
            // finds both the camera and the station regardless of sibling order.
            var scenery = root.GetComponent<MenuSceneryController>();
            if (scenery == null)
            {
                scenery = root.AddComponent<MenuSceneryController>();
            }

            // The camera composites internally with premultiplied alpha, but UI
            // Toolkit blends its Image with straight alpha - so the controller
            // runs a resolve blit between them. Wire the material as a serialized
            // asset reference: a shader only ever reached via Shader.Find looks
            // unused to the build-time shader stripper.
            Material resolveMat = GetOrCreateMaterial(
                "UnpremultiplyAlpha",
                "Fodinae/UI/UnpremultiplyAlpha");
            var sceneryObject = new SerializedObject(scenery);
            sceneryObject.FindProperty("_resolveMaterialAsset").objectReferenceValue = resolveMat;
            sceneryObject.ApplyModifiedProperties();

            // The star sits BEHIND the camera and slightly to its left, so the
            // disc is lit almost face-on with the terminator pushed out to the
            // right limb: mostly day, a dark crescent on the right. It is
            // therefore never itself on screen, and there is deliberately no
            // star sphere any more - a lit disc plus a separate visible sun
            // would have to disagree about where the light comes from, and the
            // flat emissive circle that used to stand in for it read as a
            // sticker pasted over the render.
            // Swung further to the side than a pure behind-the-camera vector: at
            // (-0.38, 0.16, -0.91) the terminator sat only ~24 degrees off the
            // limb and the dark crescent was too thin to read at menu size.
            // Solving N.L = 0 across the disc, |L.z| = 0.62 puts the terminator
            // at 0.63R from the centre - about 81% of the visible disc lit. The
            // geometric split is not the whole story though: N.L falls to zero
            // approaching the terminator, so a wide band reads as dark long
            // before it is unlit. 0.42 was geometrically 71% lit but looked
            // half-dark for exactly that reason.
            Vector3 sunDir = new Vector3(-0.68f, 0.24f, 0.52f).normalized;

            // Radii must agree with PlanetAtmosphere's _PlanetRadiusRatio, which
            // is what tells the ray-march where the crust is. Derived here from
            // the two scales so they cannot drift apart.
            const float planetScale = 3.0f;
            const float shellScale = 3.28f;

            GameObject planetSurface = FindOrCreatePrimitive(root, "PlanetSurface", PrimitiveType.Sphere);
            planetSurface.transform.localPosition = Vector3.zero;
            planetSurface.transform.localScale = Vector3.one * planetScale;

            // Tilted so the terrain's polar axis is not aligned with the frame,
            // which reads as a body in space rather than a textured ball.
            planetSurface.transform.localRotation = Quaternion.Euler(-18f, 24f, 8f);
            AssignMesh(planetSurface, BuildPlanetMesh.PlanetMeshPath);
            AssignMaterial(planetSurface, "Assets/Materials/PlanetSurface.mat");

            // Slow axial spin.
            ConfigureSpin(planetSurface, 0.35f);

            GameObject atmosphere = FindOrCreatePrimitive(root, "PlanetAtmosphere", PrimitiveType.Sphere);
            atmosphere.transform.localPosition = planetSurface.transform.localPosition;
            atmosphere.transform.localScale = Vector3.one * shellScale;

            // Same axial tilt as the crust. The haze itself is spherically
            // symmetric and does not care, but the cloud deck's zonal bands are
            // evaluated in this object's space, and bands that ignore the tilt
            // would sit at right angles to the body they belong to.
            atmosphere.transform.localRotation = planetSurface.transform.localRotation;

            // Nearly frozen drift: a full turn takes over three hours, so the
            // deck reads as motionless while you watch and only as alive on a
            // timelapse. Any visible rotation gives away the scale - a body
            // this size cannot turn in seconds - and the earlier 1.6 deg/s
            // super-rotation made the cloud bands crawl across the disc. The
            // deck still never lines up with the crust beneath it: their
            // periods differ, which is all the original super-rotation was for.
            ConfigureSpin(atmosphere, 0.03f);
            AssignMesh(atmosphere, BuildPlanetMesh.ShellMeshPath);
            AssignMaterial(atmosphere, "Assets/Materials/PlanetAtmosphere.mat");

            // Reset to shader defaults before applying rig-owned values: these
            // materials carried properties from several earlier shader revisions,
            // and a stale serialized value that no longer matches the shader's
            // default is invisible in the inspector but changes the render.
            ResetMaterialToShaderDefaults("Assets/Materials/PlanetSurface.mat");
            ResetMaterialToShaderDefaults("Assets/Materials/PlanetAtmosphere.mat");

            SetMaterialVector("Assets/Materials/PlanetSurface.mat", "_SunDirWS", sunDir);
            SetMaterialVector("Assets/Materials/PlanetAtmosphere.mat", "_SunDirWS", sunDir);
            SetMaterialFloat("Assets/Materials/PlanetAtmosphere.mat", "_PlanetRadiusRatio", planetScale / shellScale);

            // The star sphere stays deleted - it belongs behind the camera now,
            // so a visible disc would have to disagree with the shading about
            // where the light comes from.
            foreach (string staleName in new[] { "RedDwarfStar", "DecorativeOrbitRing" })
            {
                Transform? stale = root.transform.Find(staleName);
                if (stale != null)
                {
                    UnityEngine.Object.DestroyImmediate(stale.gameObject);
                }

                GameObject? loose = GameObject.Find(staleName);
                if (loose != null)
                {
                    UnityEngine.Object.DestroyImmediate(loose);
                }
            }

            // Orbiting station and its orbit ring.
            //
            // Recoloured from the previous bright cyan: a saturated neon circle
            // over a photoreal planet reads as a HUD overlay composited on top,
            // not as an object sharing the scene's light. A thin, dim, warm-grey
            // line lit like everything else sits inside the image instead. It is
            // also deliberately below the bloom threshold, so it stays a line
            // rather than acquiring a glow the geometry cannot justify.
            GameObject station = FindOrCreate(root, "FederationStation");
            var orbit = station.GetComponent<OrbitalStationMotion>();
            if (orbit == null)
            {
                orbit = station.AddComponent<OrbitalStationMotion>();
            }

            // Pulled in with the tighter FOV so the ring still clears the
            // render-target edge instead of being sliced by it.
            const float orbitRadius = 1.72f;
            ConfigureOrbit(orbit, planetSurface.transform, orbitRadius, degreesPerSecond: 3.5f, startAngle: 30f);

            foreach (string staleName in new[] { "Hull", "PanelLeft", "PanelRight", "Light1", "Light2", "Light3" })
            {
                Transform? stale = station.transform.Find(staleName);
                if (stale != null)
                {
                    UnityEngine.Object.DestroyImmediate(stale.gameObject);
                }
            }

            // Station itself keeps a slight over-range value so it reads as a
            // lit object catching the sun rather than a flat painted dot.
            Material pointMat = GetOrCreateUnlitMaterial("StationPoint", new Color(1.7f, 1.5f, 1.15f, 1f), 3120);
            GameObject point = FindOrCreatePrimitive(station, "Point", PrimitiveType.Sphere);
            point.transform.localPosition = Vector3.zero;
            point.transform.localScale = Vector3.one * 0.042f;
            AssignMaterial(point, pointMat);

            Material ringMat = GetOrCreateUnlitMaterial("OrbitRingLine", new Color(0.42f, 0.40f, 0.34f, 0.30f), 3110);
            GameObject ringObj = FindOrCreate(root, "StationOrbitRing");
            var lineRenderer = ringObj.GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                lineRenderer = ringObj.AddComponent<LineRenderer>();
            }

            lineRenderer.sharedMaterial = ringMat;

            // Wider, and with no end caps. At 0.009 the line resolved to under a
            // pixel once the render target was downscaled, and the rounded cap
            // generated at every one of the 220 segments then popped in and out
            // against the sub-pixel line - which read as a beaded, crawling
            // chain rather than a smooth orbit.
            lineRenderer.widthMultiplier = 0.016f;
            lineRenderer.numCapVertices = 0;
            lineRenderer.numCornerVertices = 0;
            lineRenderer.alignment = LineAlignment.View;
            lineRenderer.textureMode = LineTextureMode.Stretch;

            // Depth-tested against the opaque crust, so the far half of the
            // orbit passes behind the planet instead of drawing over it - the
            // single cheapest cue that the ring is in the scene, not on it.
            lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            lineRenderer.receiveShadows = false;

            var ringRenderer = ringObj.GetComponent<OrbitRingRenderer>();
            if (ringRenderer == null)
            {
                ringRenderer = ringObj.AddComponent<OrbitRingRenderer>();
            }

            ConfigureRing(ringRenderer, planetSurface.transform, orbitRadius, lineWidth: 0.016f, tilt: OrbitTilt);

            // Post-process volume (fixed decorative preset, not tied to ClientConfig)
            VolumeProfile profile = BuildVolumeProfile();
            GameObject volumeObj = FindOrCreate(root, "MenuVolume");
            var volume = volumeObj.GetComponent<Volume>();
            if (volume == null)
            {
                volume = volumeObj.AddComponent<Volume>();
            }

            volume.isGlobal = true;
            volume.priority = 0f;
            volume.weight = 1f;
            volume.sharedProfile = profile;

            SetLayerRecursive(root, layer);

            // UI Toolkit Screen Space Overlay needs no camera to render, but
            // with MenuSceneryCamera dedicated entirely to the offscreen RT,
            // zero cameras target the actual Display - harmless in a real
            // build, but the Editor Game view shows a "No cameras rendering"
            // overlay over everything, which reads as broken. A trivial
            // black-clearing backdrop camera (renders nothing, layer Nothing)
            // silences it.
            GameObject displayCamObj = FindOrCreate(null, "MenuDisplayBackdropCamera");
            displayCamObj.layer = 0;
            displayCamObj.transform.SetParent(null, worldPositionStays: false);
            displayCamObj.transform.localPosition = Vector3.zero;
            displayCamObj.transform.localRotation = Quaternion.identity;

            var displayCam = displayCamObj.GetComponent<Camera>();
            if (displayCam == null)
            {
                displayCam = displayCamObj.AddComponent<Camera>();
            }

            // Renders NOTHING, deliberately, and that is its entire job.
            //
            // For a while this camera carried the starfield on its own layer.
            // That is what put the sky over the game: the quad was world
            // geometry, and MainGame's camera renders every layer while the menu
            // scene is still loaded. The starfield is now a Graphics.Blit into a
            // RenderTexture with no camera and no mesh (MenuStarfield), so there
            // is nothing left here to leak.
            displayCam.clearFlags = CameraClearFlags.SolidColor;
            displayCam.backgroundColor = new Color(0.012f, 0.018f, 0.032f, 1f);
            displayCam.cullingMask = 0;
            displayCam.depth = -100f;
            displayCam.targetTexture = null;
            displayCam.orthographic = true;
            displayCam.orthographicSize = 1f;
            displayCam.nearClipPlane = 0.1f;
            displayCam.farClipPlane = 1f;
            displayCam.allowHDR = true;

            // Not tagged: see the note on MenuSceneryCamera above. Two cameras
            // in this scene, neither of them the gameplay camera.
            displayCamObj.tag = "Untagged";

            // Remove the previous quad-and-layer implementation wherever it may
            // still exist, so a scene built by an older version of this tool
            // converges instead of keeping a second, invisible starfield.
            foreach (string staleName in new[] { "StarfieldQuad" })
            {
                Transform? stale = displayCamObj.transform.Find(staleName);
                if (stale != null)
                {
                    UnityEngine.Object.DestroyImmediate(stale.gameObject);
                }

                GameObject? loose = GameObject.Find(staleName);
                if (loose != null)
                {
                    UnityEngine.Object.DestroyImmediate(loose);
                }
            }

            // The starfield now lives on the rig root as a pure blit source.
            Material starMat = GetOrCreateMaterial("Starfield", "Fodinae/UI/Starfield");
            var starfield = root.GetComponent<MenuStarfield>();
            if (starfield == null)
            {
                starfield = root.AddComponent<MenuStarfield>();
            }

            var starfieldObject = new SerializedObject(starfield);
            starfieldObject.FindProperty("_starfieldMaterial").objectReferenceValue = starMat;
            starfieldObject.ApplyModifiedProperties();

            // Guard against the historical "material assignment silently didn't
            // take" failure mode before trusting anything visually.
            Material? boundAtmoMat = atmosphere.GetComponent<MeshRenderer>().sharedMaterial;
            if (boundAtmoMat == null || boundAtmoMat.name != "PlanetAtmosphere")
            {
                Debug.LogError($"[BuildMenuSceneryRig] PlanetAtmosphere material did not bind (got '{boundAtmoMat?.name ?? "null"}').");
            }

            EditorUtility.SetDirty(root);
            AssetDatabase.SaveAssets();

            // Saved, not left dirty. Everything above is a scene mutation, and
            // leaving it unsaved meant "I rebuilt the rig" and "the project
            // contains the rebuilt rig" were two different things - which is
            // exactly how a fix can look applied and still not be present.
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();

            Debug.Log("[BuildMenuSceneryRig] Rig build complete and scene saved.");
        }

        private static void ConfigureSpin(GameObject target, float degreesPerSecond)
        {
            var spin = target.GetComponent<MenuPlanetSpin>();
            if (spin == null)
            {
                spin = target.AddComponent<MenuPlanetSpin>();
            }

            var so = new SerializedObject(spin);
            so.FindProperty("_degreesPerSecond").floatValue = degreesPerSecond;
            so.ApplyModifiedProperties();
        }

        private static void ConfigureOrbit(
            OrbitalStationMotion orbit,
            Transform center,
            float radius,
            float degreesPerSecond,
            float startAngle)
        {
            var so = new SerializedObject(orbit);
            so.FindProperty("_center").objectReferenceValue = center;
            so.FindProperty("_radius").floatValue = radius;
            so.FindProperty("_degreesPerSecond").floatValue = degreesPerSecond;
            so.FindProperty("_startAngleDegrees").floatValue = startAngle;
            so.FindProperty("_orbitPlaneEulerAngles").vector3Value = OrbitTilt;
            so.ApplyModifiedProperties();
        }

        private static void ConfigureRing(OrbitRingRenderer ring, Transform center, float radius, float lineWidth, Vector3 tilt)
        {
            var so = new SerializedObject(ring);
            so.FindProperty("_center").objectReferenceValue = center;
            so.FindProperty("_radius").floatValue = radius;
            so.FindProperty("_orbitPlaneEulerAngles").vector3Value = tilt;
            so.FindProperty("_segments").intValue = 220;
            so.FindProperty("_lineWidth").floatValue = lineWidth;
            so.ApplyModifiedProperties();
        }

        private static Material GetOrCreateMaterial(string name, string shaderName)
        {
            string path = $"Assets/Materials/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader? shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    throw new InvalidOperationException($"Shader '{shaderName}' not found.");
                }

                mat = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(mat, path);
                EditorUtility.SetDirty(mat);
            }

            return mat;
        }

        // renderQueue is explicit because these share a bounding-box centre with
        // the atmosphere shell (everything is centred on the planet), which
        // leaves distance sorting between them with no tie-breaker at all.
        private static Material GetOrCreateUnlitMaterial(string name, Color color, int renderQueue)
        {
            Material mat = GetOrCreateMaterial(name, "Fodinae/UI/MenuLineUnlit");

            // Existing assets may still carry the old Sprites/Default shader.
            Shader? expected = Shader.Find("Fodinae/UI/MenuLineUnlit");
            if (expected != null && mat.shader != expected)
            {
                mat.shader = expected;
            }

            mat.SetColor("_Color", color);
            mat.renderQueue = renderQueue;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static VolumeProfile BuildVolumeProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            else
            {
                profile.components.Clear();
            }

            // The old settings bloomed the atmosphere's white Fresnel rim into a
            // hard white ring around the disc. With the rim gone, bloom only
            // needs to catch the genuinely over-range parts (the sunlit limb and
            // the rift glow), so the threshold sits well above the crust's
            // exposure and the tint is warm rather than neutral white.
            var bloom = profile.Add<BloomComponent>(true);
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.58f;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.15f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.70f;
            bloom.tint.overrideState = true;
            bloom.tint.value = new Color(1f, 0.88f, 0.72f);
            bloom.active = true;

            var chromatic = profile.Add<ChromaticAberrationComponent>(true);
            chromatic.intensity.overrideState = true;
            chromatic.intensity.value = 0.18f;
            chromatic.active = true;

            var vignette = profile.Add<VignetteComponent>(true);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0.28f;
            vignette.color.overrideState = true;
            vignette.color.value = Color.black;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.70f;
            vignette.center.overrideState = true;
            vignette.center.value = new Vector2(0.5f, 0.5f);
            vignette.active = true;

            var colorGrading = profile.Add<ColorGradingComponent>(true);
            colorGrading.exposure.overrideState = true;
            colorGrading.exposure.value = 0f;
            colorGrading.colorFilter.overrideState = true;
            colorGrading.colorFilter.value = new Color(1.03f, 1f, 0.98f);
            colorGrading.contrast.overrideState = true;
            colorGrading.contrast.value = 0.14f;
            colorGrading.saturation.overrideState = true;
            colorGrading.saturation.value = 1.10f;

            colorGrading.toneMapping.overrideState = true;
            colorGrading.toneMapping.value = true;
            colorGrading.toneMappingWhitePoint.overrideState = true;
            colorGrading.toneMappingWhitePoint.value = 3.2f;
            colorGrading.active = true;

            EditorUtility.SetDirty(profile);
            return profile;
        }

        private static void AssignMesh(GameObject go, string meshPath)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                Debug.LogError($"[BuildMenuSceneryRig] Mesh not found at '{meshPath}'.");
                return;
            }

            var filter = go.GetComponent<MeshFilter>();
            if (filter == null)
            {
                Debug.LogError($"[BuildMenuSceneryRig] '{go.name}' has no MeshFilter.");
                return;
            }

            filter.sharedMesh = mesh;
        }

        private static void AssignMaterial(GameObject go, string materialPath)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null)
            {
                Debug.LogError($"[BuildMenuSceneryRig] Material not found at '{materialPath}'.");
                return;
            }

            AssignMaterial(go, mat);
        }

        private static void AssignMaterial(GameObject go, Material mat)
        {
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                Debug.LogError($"[BuildMenuSceneryRig] '{go.name}' has no MeshRenderer.");
                return;
            }

            renderer.sharedMaterial = mat;
        }

        // Copies every property back to its shader-declared default, clearing
        // values serialized against older revisions of these shaders.
        private static void ResetMaterialToShaderDefaults(string materialPath)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null)
            {
                Debug.LogError($"[BuildMenuSceneryRig] Material not found at '{materialPath}' while resetting defaults.");
                return;
            }

            var pristine = new Material(mat.shader);
            mat.CopyPropertiesFromMaterial(pristine);
            UnityEngine.Object.DestroyImmediate(pristine);
            EditorUtility.SetDirty(mat);
        }

        private static void SetMaterialVector(string materialPath, string property, Vector3 value)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null)
            {
                Debug.LogError($"[BuildMenuSceneryRig] Material not found at '{materialPath}' while setting {property}.");
                return;
            }

            if (!mat.HasProperty(property))
            {
                Debug.LogError($"[BuildMenuSceneryRig] Material '{materialPath}' has no property '{property}'.");
                return;
            }

            mat.SetVector(property, new Vector4(value.x, value.y, value.z, 0f));
            EditorUtility.SetDirty(mat);
        }

        private static void SetMaterialFloat(string materialPath, string property, float value)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (mat == null)
            {
                Debug.LogError($"[BuildMenuSceneryRig] Material not found at '{materialPath}' while setting {property}.");
                return;
            }

            if (!mat.HasProperty(property))
            {
                Debug.LogError($"[BuildMenuSceneryRig] Material '{materialPath}' has no property '{property}'.");
                return;
            }

            mat.SetFloat(property, value);
            EditorUtility.SetDirty(mat);
        }

        private static GameObject FindOrCreatePrimitive(GameObject parent, string name, PrimitiveType type)
        {
            Transform? existing = parent.transform.Find(name);
            if (existing != null)
            {
                return existing.gameObject;
            }

            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Collider? collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            go.transform.SetParent(parent.transform, worldPositionStays: false);
            return go;
        }

        private static GameObject FindOrCreate(GameObject? parent, string name)
        {
            Transform? existing = parent == null
                ? GameObject.Find(name)?.transform
                : parent.transform.Find(name);
            if (existing != null)
            {
                return existing.gameObject;
            }

            var go = new GameObject(name);
            if (parent != null)
            {
                go.transform.SetParent(parent.transform, worldPositionStays: false);
            }

            return go;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }

        // Blanks a user layer slot by name. Safe to call for a name that is not
        // defined - the common case once the cleanup has run once.
        private static void RemoveLayer(string layerName)
        {
            UnityEngine.Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagManagerAssets.Length == 0)
            {
                return;
            }

            var tagManager = new SerializedObject(tagManagerAssets[0]);
            SerializedProperty layersProp = tagManager.FindProperty("layers");

            for (int i = 8; i < layersProp.arraySize; i++)
            {
                SerializedProperty sp = layersProp.GetArrayElementAtIndex(i);
                if (sp.stringValue != layerName)
                {
                    continue;
                }

                sp.stringValue = string.Empty;
                tagManager.ApplyModifiedProperties();
                EditorUtility.SetDirty(tagManagerAssets[0]);
                AssetDatabase.SaveAssetIfDirty(tagManagerAssets[0]);
                Debug.Log($"[BuildMenuSceneryRig] Removed unused layer '{layerName}' (slot {i}).");
                return;
            }
        }

        private static int EnsureLayer(string layerName)
        {
            UnityEngine.Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (tagManagerAssets.Length == 0)
            {
                throw new InvalidOperationException("ProjectSettings/TagManager.asset not found.");
            }

            var tagManager = new SerializedObject(tagManagerAssets[0]);
            SerializedProperty layersProp = tagManager.FindProperty("layers");

            int index = -1;
            for (int i = 8; i < layersProp.arraySize; i++)
            {
                if (layersProp.GetArrayElementAtIndex(i).stringValue == layerName)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                for (int i = 8; i < layersProp.arraySize; i++)
                {
                    SerializedProperty sp = layersProp.GetArrayElementAtIndex(i);
                    if (string.IsNullOrEmpty(sp.stringValue))
                    {
                        sp.stringValue = layerName;
                        tagManager.ApplyModifiedProperties();
                        index = i;
                        break;
                    }
                }
            }

            if (index < 0)
            {
                throw new InvalidOperationException("No free user layer slots (8-31) available.");
            }

            // Flushed unconditionally, including when the layer was already
            // present in memory.
            //
            // This ran once in an earlier session and the name reached the live
            // editor but never reached ProjectSettings/TagManager.asset on disk.
            // Every subsequent build then took the "already exists" path and
            // skipped saving entirely, so the scene kept referencing a layer
            // index that is blank in the committed project - the rig looked
            // correct in this editor and would have arrived nameless anywhere
            // else. AssetDatabase.SaveAssets() does not cover ProjectSettings;
            // SetDirty plus SaveAssetIfDirty on the object itself does.
            EditorUtility.SetDirty(tagManagerAssets[0]);
            AssetDatabase.SaveAssetIfDirty(tagManagerAssets[0]);
            return index;
        }
    }
}
