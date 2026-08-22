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
        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.Equals(activeScene.name, "MainMenu", System.StringComparison.OrdinalIgnoreCase))
        {
            Camera? activeCamera = ResolveIn(activeScene);
            if (activeCamera != null)
            {
                return activeCamera;
            }
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

    public static Camera? ResolveIn(Scene scene)
    {
        if (string.Equals(scene.name, "MainMenu", System.StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Camera? tagged = Camera.main;
        if (tagged != null && IsUsable(tagged, scene))
        {
            return tagged;
        }

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

        if (string.Equals(camera.gameObject.scene.name, "MainMenu", System.StringComparison.OrdinalIgnoreCase) ||
            camera.name.Contains("Menu", System.StringComparison.OrdinalIgnoreCase) ||
            camera.name.Contains("Backdrop", System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (camera.targetTexture != null)
        {
            return false;
        }

        var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
        return cameraData == null || cameraData.renderType == CameraRenderType.Base;
    }
}
