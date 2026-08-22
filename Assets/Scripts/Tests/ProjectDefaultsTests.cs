#if UNITY_EDITOR
#nullable enable

using System;
using System.Reflection;
using Fodinae.Core;
using Fodinae.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace Fodinae.Tests.Core
{
    [TestFixture]
    public sealed class ProjectDefaultsTests
    {
        [Test]
        public void ResourcesContainExactlyOneValidProjectDefaultsAsset()
        {
            ProjectDefaults[] assets = Resources.LoadAll<ProjectDefaults>("Configuration");

            Assert.That(assets, Has.Length.EqualTo(1));
            Assert.DoesNotThrow(assets[0].Validate);
        }

        [Test]
        public void SnapshotIsStableAndVersioned()
        {
            ProjectDefaults asset = Resources.Load<ProjectDefaults>(
                "Configuration/ProjectDefaults");
            Assert.That(asset, Is.Not.Null);

            ProjectDefaultsSnapshot first = asset.CreateSnapshot();
            ProjectDefaultsSnapshot second = asset.CreateSnapshot();

            Assert.That(first.SchemaVersion, Is.EqualTo(ProjectDefaults.CurrentSchemaVersion));
            Assert.That(first.ContentHash, Is.Not.Empty);
            Assert.That(second.ContentHash, Is.EqualTo(first.ContentHash));
            Assert.That(second.Client, Is.EqualTo(first.Client));
            Assert.That(second.Lighting, Is.EqualTo(first.Lighting));
            Assert.That(second.Shaders, Is.EqualTo(first.Shaders));
        }

        [Test]
        public void ClientConfigDoesNotExposeASecondDefaultsSource()
        {
            PropertyInfo? defaultsProperty = typeof(ClientConfig).GetProperty(
                "Defaults",
                BindingFlags.Public | BindingFlags.Static);

            Assert.That(defaultsProperty, Is.Null);
        }

        [Test]
        public void GraphicsQualitySettingsContainOnlyTechnicalQualityControls()
        {
            FieldInfo[] fields = typeof(GraphicsQualitySettings).GetFields(
                BindingFlags.Public | BindingFlags.Instance);

            Assert.That(
                fields,
                Has.None.Matches<FieldInfo>(field =>
                    field.Name.Contains("Extinction", StringComparison.Ordinal) ||
                    field.Name.Contains("Bounce", StringComparison.Ordinal)));
        }

        [Test]
        public void GraphicsQualityProfileContainsSixValidStandardPresets()
        {
            GraphicsQualityProfile profile = Resources.Load<GraphicsQualityProfile>(
                "GraphicsQualityProfile") ??
                throw new InvalidOperationException(
                    "Resources/GraphicsQualityProfile.asset is missing.");

            Assert.DoesNotThrow(profile.Validate);
            GraphicsQualitySettings[] settings =
                new GraphicsQualitySettings[GraphicsQualityProfile.StandardPresetCount];
            for (int index = 0; index < GraphicsQualityProfile.StandardPresetCount; index++)
            {
                GraphicsPreset preset = (GraphicsPreset)index;
                Assert.That(GraphicsQualityProfile.IsStandard(preset), Is.True);
                Assert.DoesNotThrow(() => profile.Get(preset));
                settings[index] = profile.Get(preset);
            }

            Assert.That(settings, Is.Unique);
            Assert.That(GraphicsQualityProfile.IsStandard(GraphicsPreset.Custom), Is.False);
            Assert.Throws<ArgumentException>(() => profile.Get(GraphicsPreset.Custom));
        }
    }
}
#endif
