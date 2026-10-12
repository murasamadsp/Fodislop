#nullable enable

using Kern.Core;
using Kern.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Kern.Tests.Core;

[TestFixture]
public sealed class BootstrapLoadingScreenTests
{
    private GameObject? _go;

    [TearDown]
    public void TearDown()
    {
        if (_go != null)
        {
            Object.DestroyImmediate(_go);
        }
    }

    [Test]
    public void ShowDirect_SetsPhaseTextAndKeepsVisualTreeAttached()
    {
        _go = new GameObject("TestBootstrapLoadingScreen");
        var doc = _go.AddComponent<UIDocument>();
        var loadingScreen = _go.AddComponent<BootstrapLoadingScreen>();

        loadingScreen.ShowDirect("Compiling shaders (50%)");

        VisualElement root = doc.rootVisualElement;
        var overlay = root.Q<VisualElement>("BootstrapLoadingOverlay");
        var phase = root.Q<Label>("BootstrapLoadingPhase");

        Assert.That(overlay, Is.Not.Null, "BootstrapLoadingOverlay must be attached to rootVisualElement.");
        Assert.That(phase, Is.Not.Null, "BootstrapLoadingPhase must be attached to rootVisualElement.");
        Assert.That(phase!.text, Is.EqualTo("Compiling shaders (50%)"));
        Assert.That(UIState.IsHidden(overlay), Is.False, "Overlay must be visible after ShowDirect.");

        loadingScreen.SetPhaseText("Compiling shaders (75%)");
        Assert.That(phase.text, Is.EqualTo("Compiling shaders (75%)"));
    }

    [Test]
    public void ShowDirect_CalledMultipleTimes_PreservesHierarchyAndUpdatesPhase()
    {
        _go = new GameObject("TestBootstrapLoadingScreenRepeated");
        var doc = _go.AddComponent<UIDocument>();
        var loadingScreen = _go.AddComponent<BootstrapLoadingScreen>();

        loadingScreen.ShowDirect("0%");
        loadingScreen.SetPhaseText("50%");
        loadingScreen.SetPhaseText("100%");

        VisualElement root = doc.rootVisualElement;
        var overlays = root.Query<VisualElement>("BootstrapLoadingOverlay").ToList();
        Assert.That(overlays.Count, Is.EqualTo(1), "BootstrapLoadingOverlay must not be duplicated on repeated updates.");

        var phase = root.Q<Label>("BootstrapLoadingPhase");
        Assert.That(phase, Is.Not.Null);
        Assert.That(phase!.text, Is.EqualTo("100%"));
    }
}
