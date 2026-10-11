#nullable enable

using Kern.Core;
using Kern.Core.Interfaces;
using Kern.UI;
using NUnit.Framework;
using UnityEngine;

namespace Kern.Tests.UI;

public sealed class MobileScreenContractTests
{
    private static readonly MobileDevice[] Devices = MobileDeviceMatrix.All;

    // Видимая ширина и высота кадра в клетках: короткая сторона равна
    // удвоенному зуму, длинная — не больше двух коротких.
    [TestCase(16f / 9f, 35f, 62.2f)]
    [TestCase(2f, 35f, 70f)]
    [TestCase(20f / 9f, 31.5f, 70f)]
    [TestCase(4f / 3f, 35f, 46.7f)]
    [TestCase(924f / 794f, 35f, 40.7f)]
    public void Camera_KeepsShortSideAndCapsLongSide(float aspect, float expectedHeight, float expectedWidth)
    {
        const float zoom = 17.5f;
        float height = CameraViewRules.OrthographicSize(zoom, aspect) * 2f;
        float width = height * aspect;

        Assert.That(height, Is.EqualTo(expectedHeight).Within(0.1f));
        Assert.That(width, Is.EqualTo(expectedWidth).Within(0.1f));
    }

    [Test]
    public void Camera_PortraitWindowKeepsWidthAsShortSide()
    {
        float height = CameraViewRules.OrthographicSize(10f, 0.5f) * 2f;

        Assert.That(height * 0.5f, Is.EqualTo(20f).Within(0.01f));
        Assert.That(height, Is.EqualTo(40f).Within(0.01f));
    }

    // Отступы панели — это вырез, переведённый в пиксели панели: их сумма с
    // безопасной областью в пикселях панели даёт весь экран.
    [TestCaseSource(nameof(Devices))]
    public void SafeArea_InsetsTileThePanel(MobileDevice device)
    {
        ScreenShape shape = ScreenShapeRules.Resolve(device.Pixels, device.Dpi, device.SafeArea, true, true);
        Vector2 panel = shape.SizeDp;
        UISafeArea.Insets insets = UISafeArea.Compute(shape, panel);

        float pxToPanel = panel.x / device.Pixels.x;
        Assert.That(insets.Left, Is.EqualTo(device.SafeArea.xMin * pxToPanel).Within(0.01f));
        Assert.That(insets.Left + insets.Right + (device.SafeArea.width * pxToPanel), Is.EqualTo(panel.x).Within(0.05f));
        Assert.That(insets.Top + insets.Bottom + (device.SafeArea.height * pxToPanel), Is.EqualTo(panel.y).Within(0.05f));
        Assert.That(insets.Bottom, Is.EqualTo(device.SafeArea.yMin * pxToPanel).Within(0.01f));
    }
}
