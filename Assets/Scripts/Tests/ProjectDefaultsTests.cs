#nullable enable

using System.Reflection;
using Fodinae.Core;
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
        }

        [Test]
        public void ClientConfigDoesNotExposeASecondDefaultsSource()
        {
            PropertyInfo? defaultsProperty = typeof(ClientConfig).GetProperty(
                "Defaults",
                BindingFlags.Public | BindingFlags.Static);

            Assert.That(defaultsProperty, Is.Null);
        }
    }
}
