#nullable enable

using System.IO;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using Fodinae.Rendering;
using Fodinae.Rendering.PostProcessing;
using NUnit.Framework;
using UnityEngine;

namespace Fodinae.Tests.Core;

public sealed class ClientConfigMigrationTests
{
    private GraphicsQualityProfile _profile = null!;

    [SetUp]
    public void SetUp()
    {
        _profile = ScriptableObject.CreateInstance<GraphicsQualityProfile>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_profile);
    }

    [Test]
    public void Migrate_V14CustomConfig_AppliesV15LimitsAndCurrentDefaultsHash()
    {
        var migration = new ClientConfigMigration(new StubProjectDefaults("defaults-v2"), _profile);
        var config = new ClientConfig
        {
            SchemaVersion = 14,
            ProjectDefaultsHash = "defaults-v1",
            GraphicsPreset = GraphicsPreset.Custom,
            BloomIntensity = 9f,
            MotionBlurIntensity = 3f,
            AdvancedPostProcess = new AdvancedPostProcessSettings
            {
                LensDirtIntensity = 2f,
                LightStability = 2f,
            },
        };

        bool migrated = migration.Migrate(config);

        Assert.That(migrated, Is.True);
        Assert.That(config.SchemaVersion, Is.EqualTo(ClientConfig.CurrentSchemaVersion));
        Assert.That(config.ProjectDefaultsHash, Is.EqualTo("defaults-v2"));
        Assert.That(config.BloomIntensity, Is.EqualTo(2f));
        Assert.That(config.MotionBlurIntensity, Is.EqualTo(0.5f));
        Assert.That(config.AdvancedPostProcess.LensDirtIntensity, Is.EqualTo(0.35f));
        Assert.That(config.AdvancedPostProcess.LightStability, Is.EqualTo(0.9f));
    }

    [Test]
    public void Migrate_CurrentCustomConfigWithMatchingHash_IsIdempotent()
    {
        var migration = new ClientConfigMigration(new StubProjectDefaults("defaults-v1"), _profile);
        var config = new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
            ProjectDefaultsHash = "defaults-v1",
            GraphicsPreset = GraphicsPreset.Custom,
        };

        bool migrated = migration.Migrate(config);

        Assert.That(migrated, Is.False);
    }

    [Test]
    public void Validator_RejectsNonFiniteRuntimeSetting()
    {
        var validator = new ClientConfigValidator(
            new StubProjectDefaults("defaults-v1"),
            _profile);
        var config = new ClientConfig
        {
            SchemaVersion = ClientConfig.CurrentSchemaVersion,
            ProjectDefaultsHash = "defaults-v1",
            GraphicsPreset = GraphicsPreset.Custom,
            UIScale = float.NaN,
        };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => validator.Validate(config))!;

        Assert.That(exception.Message, Does.Contain(nameof(config.UIScale)));
    }

    private sealed class StubProjectDefaults(string contentHash) : IProjectDefaults
    {
        public int SchemaVersion => 1;

        public string ContentHash => contentHash;

        public ClientDefaultsSnapshot Client => default;

        public LightingDefaultsSnapshot Lighting => default;

        public ShaderDefaultsSnapshot Shaders => default;
    }
}
