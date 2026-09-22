#nullable enable

using System.Collections;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Rendering;
using Kern.World.Lighting;
using Kern.World.Lighting.Quality;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Kern.Tests.PlayMode;

[TestFixture]
[Category("GPU")]
public sealed class GraphicsPresetSwitchPlayModeTests
{
    private const string TestDummyToken = "playmode-graphics-preset-switch-token";

    private BootstrapLifetimeScope _bootstrap = null!;
    private DummyAuthenticationScope _authentication = null!;
    private IClientConfigManager _config = null!;
    private GraphicsPreset _originalPreset;
    private GraphicsQualitySettings _originalSettings;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        Assume.That(SystemInfo.supportsComputeShaders, Is.True, "Graphics switching test needs compute shader support.");

        _authentication = DummyAuthenticationScope.Seed(TestDummyToken);
        yield return PlayModeHarness.StartAtGateway();
        _bootstrap = PlayModeHarness.FindBootstrap()!;
        _config = _bootstrap.Container.Resolve<IClientConfigManager>();
        _originalPreset = _config.Config.GraphicsPreset;
        _originalSettings = _config.Config.GraphicsQualitySettings;
        yield return PlayModeHarness.EnterMainGame(_bootstrap);
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        GraphicsSettingsController? graphics = PlayModeHarness.ResolveInGame<GraphicsSettingsController>();
        if (graphics != null)
        {
            if (GraphicsQualityProfile.IsStandard(_originalPreset))
            {
                graphics.SelectStandardPreset(_originalPreset);
            }
            else
            {
                graphics.SetCustomSettings(_originalSettings);
            }
        }

        yield return PlayModeHarness.Shutdown();
        _authentication.Restore();
    }

    [UnityTest]
    public IEnumerator StandardPresetsAndCustomSettings_SwitchThroughProductionPath()
    {
        GraphicsSettingsController graphics = PlayModeHarness.RequireInGame<GraphicsSettingsController>();
        LightingEngine lighting = PlayModeHarness.RequireInGame<LightingEngine>();
        GraphicsPreset[] presets =
        [
            GraphicsPreset.VeryLow,
            GraphicsPreset.Low,
            GraphicsPreset.Medium,
            GraphicsPreset.High,
            GraphicsPreset.VeryHigh,
            GraphicsPreset.Ultra,
        ];

        foreach (GraphicsPreset preset in presets)
        {
            graphics.SelectStandardPreset(preset);
            yield return PlayModeHarness.Frames(12);

            Assert.That(graphics.SelectedPreset, Is.EqualTo(preset));
            Assert.That(_config.Config.GraphicsPreset, Is.EqualTo(preset));
            Assert.That(lighting.IsInitialized, Is.True, $"Lighting was lost after selecting {preset}.");
        }

        GraphicsQualitySettings custom = _config.Config.GraphicsQualitySettings;
        custom.LightingQuality = LightingQualityMode.Off;
        custom.RenderScale = 1f;
        graphics.SetCustomSettings(custom);
        yield return PlayModeHarness.Frames(12);

        Assert.That(graphics.SelectedPreset, Is.EqualTo(GraphicsPreset.Custom));
        Assert.That(_config.Config.GraphicsPreset, Is.EqualTo(GraphicsPreset.Custom));
        Assert.That(_config.Config.GraphicsQualitySettings.LightingQuality, Is.EqualTo(LightingQualityMode.Off));
    }

    [UnityTest]
    [Timeout(120_000)]
    public IEnumerator RepeatedPresetShrinkAndGrow_DoesNotCorruptDynamicLightSlots()
    {
        GraphicsSettingsController graphics = PlayModeHarness.RequireInGame<GraphicsSettingsController>();
        GraphicsPreset[] sequence =
        [
            GraphicsPreset.Ultra,
            GraphicsPreset.VeryLow,
            GraphicsPreset.Ultra,
            GraphicsPreset.VeryLow,
            GraphicsPreset.High,
            GraphicsPreset.VeryLow,
        ];

        foreach (GraphicsPreset preset in sequence)
        {
            graphics.SelectStandardPreset(preset);
            yield return PlayModeHarness.Frames(20);
            Assert.That(graphics.SelectedPreset, Is.EqualTo(preset));
        }

        // Any IndexOutOfRangeException from DynamicLightTileCache is an
        // unhandled PlayMode failure; this assertion also verifies that the
        // live scene remained in the game after all reallocations.
        Assert.That(PlayModeHarness.FindBootstrap()!.CurrentSceneName,
            Is.EqualTo(ProjectRuntimeContracts.SceneNames.MainGame));
    }
}
