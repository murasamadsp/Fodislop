#if UNITY_EDITOR
#nullable enable

using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kern.Editor.AssetMaintenance
{
    // Idempotent repair for the project's output-only default volume profile.
    // Artistic post effects are authored by Kern's render pipeline.
    public static class DefaultVolumeProfileMigration
    {
        public static void EnforceOutputOnlyDefaultProfile()
        {
            const string ProfilePath = "Assets/Settings/DefaultVolumeProfile.asset";
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath)
                ?? throw new System.InvalidOperationException($"Missing volume profile: {ProfilePath}");

            VolumeComponent[] nativePostEffects = profile.components
                .Where(component => component != null &&
                    component.GetType().Namespace == "UnityEngine.Rendering.Universal" &&
                    component.GetType().Name != "Tonemapping")
                .ToArray();
            if (nativePostEffects.Length > 0)
            {
                foreach (VolumeComponent effect in nativePostEffects)
                {
                    profile.Remove(effect.GetType());
                    Object.DestroyImmediate(effect, allowDestroyingAssets: true);
                }

                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
#endif
