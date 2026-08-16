#nullable enable

using UnityEditor;
using UnityEngine.Rendering;
using Fodinae.Rendering.PostProcessing;

namespace Fodinae.Editor
{
    internal static class NormalizePostProcessProfiles
    {
        [MenuItem("Fodinae/Diagnostics/Normalize Post-Process Profiles")]
        private static void Normalize()
        {
            NormalizeProfile("Assets/Settings/PostProcessVolumeProfile.asset");
            NormalizeProfile("Assets/Settings/DefaultVolumeProfile.asset");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static void NormalizeProfile(string assetPath)
        {
            VolumeProfile? profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(assetPath);
            if (profile == null)
            {
                return;
            }

            SetInactive<BloomComponent>(profile);
            SetInactive<VignetteComponent>(profile);
            SetInactive<ChromaticAberrationComponent>(profile);
            SetInactive<EigengrauComponent>(profile);
            SetInactive<MotionBlurComponent>(profile);

            if (profile.TryGet<ColorGradingComponent>(out var colorGrading) && colorGrading != null)
            {
                colorGrading.active = false;
                colorGrading.toneMapping.value = false;
            }

            EditorUtility.SetDirty(profile);
        }

        private static void SetInactive<T>(VolumeProfile profile)
            where T : VolumeComponent
        {
            if (profile.TryGet<T>(out var component) && component != null)
            {
                component.active = false;
            }
        }
    }
}
