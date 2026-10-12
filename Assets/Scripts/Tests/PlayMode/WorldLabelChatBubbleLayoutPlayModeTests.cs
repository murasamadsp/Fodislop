#nullable enable

using System.Collections;
using Kern.Core;
using Kern.Core.Interfaces;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Kern.Tests.PlayMode;

/// <summary>
/// Раскладка настоящего пузыря локального чата.
///
/// Меряет элемент, созданный через IWorldLabels, а не собранный вручную из USS:
/// тон и границы 9-slice ставит код, и при ручной сборке они не применяются, а
/// тест обязан видеть то, что видит игрок.
///
/// Оба дефекта, которые снимок игрока выдал как «не прозрачный» и «текст внизу»,
/// проверяются числами, а не картинкой.
/// </summary>
[TestFixture]
public sealed class WorldLabelChatBubbleLayoutPlayModeTests
{
    // Альфа тона рамки: LocalChat.prefab, m_Color a=0.6156863 (157/255).
    private const float ExpectedTintAlpha = 0.6156863f;

    // Тело спрайта — верхние 18 px из 32, хвост занимает остальные 14 px снизу.
    private const float SpriteBodyPx = 18f;
    private const float SpriteHeightPx = 32f;

    private IWorldLabels _labels = null!;
    private IWorldLabel? _label;
    private UIDocument _document = null!;
    private Camera _camera = null!;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        yield return PlayModeHarness.StartAtGateway();
        BootstrapLifetimeScope bootstrap = PlayModeHarness.FindBootstrap()!;
        yield return PlayModeHarness.EnterMainGame(bootstrap);
        _labels = PlayModeHarness.RequireInGame<IWorldLabels>();
        _camera = PlayModeHarness.RequireInGame<IGameplayCamera>().Camera;
        _document = PlayModeHarness.FindComponentInScene<UIDocument>(
            PlayModeHarness.Scene(ProjectRuntimeContracts.SceneNames.MainGame))!;
        Assert.That(_document, Is.Not.Null, "MainGame has no UIDocument.");
    }

    [UnityTest]
    public IEnumerator BubbleFrame_IsSemiTransparentAndSlicedFromTheSprite()
    {
        yield return ShowBubble("привет");
        VisualElement frame = Frame();

        Sprite sprite = frame.resolvedStyle.backgroundImage.sprite;
        Assert.That(sprite, Is.Not.Null, "The bubble frame has no sprite background.");
        Assert.That(sprite!.name, Is.EqualTo("LocalChatBubble"));

        // Прозрачность: рамка обязана быть полупрозрачной, как в оригинале.
        // Задаётся кодом рядом со спрайтом, потому что правило тона в USS не
        // разрешается при background-image из кода.
        Color tint = frame.resolvedStyle.unityBackgroundImageTintColor;
        Debug.Log($"[Bubble] tint={tint} slices L={frame.resolvedStyle.unitySliceLeft} " +
                  $"R={frame.resolvedStyle.unitySliceRight} T={frame.resolvedStyle.unitySliceTop} " +
                  $"B={frame.resolvedStyle.unitySliceBottom} bgSize={frame.resolvedStyle.backgroundSize.sizeType}");
        Assert.That(
            tint.a,
            Is.EqualTo(ExpectedTintAlpha).Within(0.01f),
            "The bubble frame must keep the original client's alpha of 157/255.");

        // 9-slice: границы обязаны прийти из ассета, иначе спрайт растягивается
        // целиком и рамка вырождается в эллипс.
        Assert.That(frame.resolvedStyle.unitySliceLeft, Is.EqualTo(sprite.border.x));
        Assert.That(frame.resolvedStyle.unitySliceRight, Is.EqualTo(sprite.border.z));
        Assert.That(frame.resolvedStyle.unitySliceTop, Is.EqualTo(sprite.border.w));
        Assert.That(frame.resolvedStyle.unitySliceBottom, Is.EqualTo(sprite.border.y));
        Assert.That(
            frame.resolvedStyle.backgroundSize.sizeType,
            Is.EqualTo(BackgroundSizeType.Length),
            "The 9-slice background needs an explicit size.");
    }

    [UnityTest]
    public IEnumerator BubbleText_SitsInTheBodyNotUnderTheTail()
    {
        yield return ShowBubble("привет");
        VisualElement frame = Frame();
        Label text = FindText();

        // Смещение считается по worldBound: resolvedStyle.top у статичного
        // flex-ребёнка показывает раскладку, а не то, что видно на экране.
        float scale = ProjectRuntimeContracts.Camera.ReferenceOrthographicSize / _camera.orthographicSize;
        Rect frameBox = frame.worldBound;
        Rect textBox = text.worldBound;
        float offsetTop = (textBox.y - frameBox.y) / scale;

        Debug.Log(
            $"[BubbleText] frame h={frameBox.height} text h={textBox.height} offsetTop={offsetTop} " +
            $"marginT={text.resolvedStyle.marginTop} marginB={text.resolvedStyle.marginBottom} scale={scale}");

        // Базовая тема задаёт Label поля; без их обнуления текст центрировался по
        // боксу целиком и уезжал под хвост (замерено marginTop 4.27 px).
        Assert.That(
            text.resolvedStyle.marginTop,
            Is.EqualTo(0f).Within(0.01f),
            "The base theme's Label margin-top must be reset.");
        Assert.That(text.resolvedStyle.marginBottom, Is.EqualTo(0f).Within(0.01f));

        // Текст начинается у верхней грани тела и в него помещается.
        Assert.That(
            offsetTop,
            Is.EqualTo(0f).Within(1f),
            "The text must start at the top of the frame, not below the centre.");
        Assert.That(
            (textBox.height / scale),
            Is.LessThanOrEqualTo(SpriteBodyPx + 0.5f),
            "The text box must fit the sprite body so it cannot reach the tail.");
    }

    [UnityTest]
    public IEnumerator BubbleFrame_KeepsSpriteHeightAndTextOverflowsWidthOnly()
    {
        yield return ShowBubble("привет");
        VisualElement frame = Frame();
        float height = frame.resolvedStyle.height;

        // Высота обязана остаться постоянной при любой длине текста: она равна
        // высоте спрайта, и вертикальный 9-slice при ней не растягивается.
        _label!.SetText(
            "Очень длинное сообщение локального чата, которое обязано быть обрезано по ширине, "
            + "а не перенесено на вторую строку, иначе вторая строка ушла бы под хвост");
        yield return Frames(3);

        Assert.That(
            frame.resolvedStyle.height,
            Is.EqualTo(height).Within(0.5f),
            "The bubble height must not depend on the message.");
        Assert.That(
            frame.resolvedStyle.height / scaleOf(),
            Is.EqualTo(SpriteHeightPx).Within(0.5f),
            "The bubble height is the sprite height, which is what keeps the tail undistorted.");
    }

    private float scaleOf() =>
        ProjectRuntimeContracts.Camera.ReferenceOrthographicSize / _camera.orthographicSize;

    // Раскладка считается в LateTick, а показать пузырь можно только после
    // кадра с ним перед камерой: иначе метку срежет отсечение по видимости.
    private IEnumerator ShowBubble(string text)
    {
        _label = _labels.Create(WorldLabelKind.ChatBubble);
        _label.SetText(text);
        _label.SetVisible(true);
        yield return Frames(3);
    }

    private VisualElement Frame() => FindFrame();

    private IEnumerator Frames(int count)
    {
        Vector3 inView = _camera.transform.position + (_camera.transform.forward * 5f);
        for (int i = 0; i < count; i++)
        {
            _label!.SetPosition(inView);
            yield return null;
        }
    }

    private VisualElement FindFrame() =>
        _document.rootVisualElement.Q<VisualElement>(className: "world-label-chat")
        ?? throw new AssertionException("The chat bubble frame is not in the panel.");

    private Label FindText() =>
        _document.rootVisualElement.Q<Label>(className: "world-label-chat-text")
        ?? throw new AssertionException("The chat bubble text is not in the panel.");

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        _label?.Dispose();
        _label = null;
        yield return PlayModeHarness.Shutdown();
    }
}