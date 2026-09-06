#nullable enable

using NUnit.Framework;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fodinae.Tests.Core;

public sealed class DisplayOutputProfileTests
{
    [TestCase("Assets/Settings/DefaultVolumeProfile.asset")]
    [TestCase("Assets/Settings/PostProcessVolumeProfile.asset")]
    [TestCase("Assets/Settings/MenuSceneryVolumeProfile.asset")]
    public void AuthoredProfilesDoNotIntroduceASecondNativePostProcess(string path)
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        Assert.That(profile, Is.Not.Null, path);
        foreach (VolumeComponent component in profile.components)
        {
            bool nativePostEffect = component != null &&
                component.GetType().Namespace == typeof(Tonemapping).Namespace &&
                component is IPostProcessComponent;
            Assert.That(nativePostEffect, Is.False,
                $"{path}: {component?.GetType().Name}; artistic effects belong to Fodinae, output tonemapping to Bootstrap.");
        }
    }
}
