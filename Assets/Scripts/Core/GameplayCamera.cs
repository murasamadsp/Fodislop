#nullable enable

using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Fodinae.Core;

// Resolves THE gameplay camera, as opposed to whatever camera Camera.main
// happens to return.
//
// Camera.main is a tag lookup across every loaded scene, and this project keeps
// two scenes loaded at once by design: MainMenu is not unloaded when the game
// starts - it stays alive until MainMenu.OnWorldLoaded fires, so the whole
// descent runs with both scenes present. For as long as any camera in the menu
// scene is also tagged MainCamera, Camera.main is a coin flip, and it is queried
// at exactly the wrong moment: GameBootstrap.PostStart resolves every manager
// while the menu is still up, and those managers cache the result.
//
// The consequences were not subtle. PostProcessRendererFeature gates its entire
// pass on `cameraData.camera == Camera.main`, so a miss sends the game's
// post-processing to the menu camera and leaves the game with none.
// PostProcessController parents its WorldUICamera to Camera.main and edits that
// camera's culling mask, so a miss strips the UI layer from the game camera and
// bolts an overlay camera onto the menu instead. TerrainRenderer.Start already
// carried a hand-written workaround for the same problem.
//
// Untagging the menu camera fixes the immediate ambiguity. This helper exists so
// the fix does not depend on a serialized tag staying correct: it prefers a
// camera that actually belongs to the active scene, which GameBootstrap sets to
// its own scene precisely so lazily-created objects land in the right place.
public static class GameplayCamera
{
    // Returns null rather than guessing when no gameplay camera exists yet -
    // which is the normal state while only the menu is loaded. Callers are
    // expected to retry; every current caller already re-resolves each frame or
    // on demand.
    public static Camera? Resolve()
    {
        Camera? activeCamera = ResolveIn(SceneManager.GetActiveScene());
        if (activeCamera != null)
        {
            return activeCamera;
        }

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isLoaded && !string.Equals(scene.name, "MainMenu", System.StringComparison.OrdinalIgnoreCase))
            {
                Camera? cam = ResolveIn(scene);
                if (cam != null)
                {
                    return cam;
                }
            }
        }

        return null;
    }

    // Same rules, against a caller-supplied scene.
    //
    // Needed because the active scene is not MainGame for the whole of that
    // scene's startup: GameBootstrap calls SetActiveScene from PostStart, which
    // the player loop runs after every Awake and Start in the frame. A component
    // in MainGame that needs its camera during Start therefore cannot use the
    // active scene, and must ask about its own.
    public static Camera? ResolveIn(Scene scene)
    {
        Camera? tagged = Camera.main;
        if (tagged != null && IsUsable(tagged, scene))
        {
            return tagged;
        }

        // Fallback for a game scene whose camera lost its tag. Deliberately not
        // "the first enabled camera anywhere" - that is how the old workaround
        // in TerrainRenderer could have picked the runtime-created WorldUICamera,
        // which is an Overlay camera with a culling mask of just the UI layer.
        foreach (Camera candidate in Object.FindObjectsByType<Camera>())
        {
            if (IsUsable(candidate, scene))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool IsUsable(Camera camera, Scene activeScene)
    {
        if (!camera.isActiveAndEnabled || camera.gameObject.scene != activeScene)
        {
            return false;
        }

        // Overlay cameras render into another camera's stack and have no
        // standalone output, so one can never be "the" gameplay camera. A camera
        // aimed at a RenderTexture is somebody's offscreen render rig - the menu
        // planet is exactly that - and is likewise never the gameplay camera.
        if (camera.targetTexture != null)
        {
            return false;
        }

        var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
        return cameraData == null || cameraData.renderType == CameraRenderType.Base;
    }
}
