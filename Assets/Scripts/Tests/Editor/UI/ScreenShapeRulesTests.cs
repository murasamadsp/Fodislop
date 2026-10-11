#nullable enable

using Kern.Core.Interfaces;
using Kern.UI;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.UI;

public sealed class ScreenShapeRulesTests
{
    private static readonly MobileDevice[] Devices = MobileDeviceMatrix.All;

    [TestCaseSource(nameof(Devices))]
    public void TouchDevice_IsClassifiedByPhysicalSize(MobileDevice device)
    {
        ScreenShape shape = Resolve(device);

        Assert.That(shape.Form, Is.EqualTo(device.ExpectedForm));
        Assert.That(shape.IsTouch, Is.True);
        Assert.That(shape.UsesPhysicalScale, Is.True);
    }

    [TestCaseSource(nameof(Devices))]
    public void PhysicalPanelScale_MakesOnePanelPixelOneDp(MobileDevice device)
    {
        ScreenShape shape = Resolve(device);
        float scale = ScreenShapeRules.PhysicalPanelScale(shape, 1f);

        // Независимый оракул: dp = px · 160 / ppi.
        float expectedWidthDp = device.Pixels.x * 160f / device.Dpi;
        Assert.That(device.Pixels.x / scale, Is.EqualTo(expectedWidthDp).Within(0.01f));
    }

    [TestCaseSource(nameof(Devices))]
    public void TouchForms_UseTouchTier(MobileDevice device)
    {
        ScreenShape shape = Resolve(device);
        string tier = UILayoutTier.ResolveTier(shape, device.Pixels.x);
        string expected = device.ExpectedForm == ScreenForm.Tablet
            ? UILayoutTier.StandardClass
            : UILayoutTier.CompactClass;

        Assert.That(tier, Is.EqualTo(expected));
    }

    [Test]
    public void UnknownDensity_FallsBackToXhdpi()
    {
        ScreenShape shape = ScreenShapeRules.Resolve(
            new Vector2Int(2400, 1080), 0f, new Rect(0, 0, 2400, 1080), usesPhysicalScale: true, isTouch: true);

        Assert.That(shape.Dpi, Is.EqualTo(320f));
        Assert.That(shape.Form, Is.EqualTo(ScreenForm.Phone));
    }

    [Test]
    public void Desktop_KeepsReferenceLayout()
    {
        ScreenShape shape = ScreenShapeRules.Resolve(
            new Vector2Int(1920, 1080), 96f, new Rect(0, 0, 1920, 1080), usesPhysicalScale: false, isTouch: false);

        Assert.That(shape.Form, Is.EqualTo(ScreenForm.Desktop));
        Assert.That(UILayoutTier.ResolveTier(shape, 1920f), Is.EqualTo(UILayoutTier.WideClass));
        Assert.That(UILayoutTier.ResolveTier(shape, 1280f), Is.EqualTo(UILayoutTier.StandardClass));
        Assert.That(UILayoutTier.ResolveTier(shape, 800f), Is.EqualTo(UILayoutTier.CompactClass));
    }

    [Test]
    public void EmptySafeArea_MeansWholeScreen()
    {
        ScreenShape shape = ScreenShapeRules.Resolve(
            new Vector2Int(2400, 1080), 428f, Rect.zero, usesPhysicalScale: true, isTouch: true);

        Assert.That(shape.SafeAreaPixels, Is.EqualTo(new Rect(0, 0, 2400, 1080)));
    }

    private static ScreenShape Resolve(MobileDevice device) =>
        ScreenShapeRules.Resolve(device.Pixels, device.Dpi, device.SafeArea, usesPhysicalScale: true, isTouch: true);
}
