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
/// Живая раскладка облака локального чата.
///
/// EditMode-пробаpanel'а ничего не доказывала: resolvedStyle нельзя читать до
/// прохода стилей, resource() в скриптовой метке не резолвится, а font-size и
/// color у метки по умолчанию совпадают со значениями класса. Здесь метка живёт
/// в настоящей панели с настоящей темой KernTheme, и кадрыyield'ятся, поэтому
/// числа настоящие.
///
/// Проверяется ровно то, что было сломано: облако обязано расти по высоте под
/// перенос текста, обжимать текст по ширине, не вылезать за max-width, и его
/// подложка обязана совпадать с боксом, а не жить отдельно от текста.
/// </summary>
[TestFixture]
public sealed class WorldLabelChatLayoutPlayModeTests
{
    private const string LongMessage =
        "Очень длинное сообщение локального чата, которое обязано перенестись на несколько строк "
        + "и поэтому обязано сделать облако заметно выше";

    private GameObject _host = null!;
    private UIDocument _document = null!;

    [UnityTest]
    public IEnumerator ChatBubble_GrowsWithWrappedTextAndHugsContent()
    {
        yield return PlayModeHarness.StartAtGateway();

        _document = FindDocument();
        Assert.That(_document, Is.Not.Null, "No active UIDocument in the Gateway scene.");

        Label chat = CreateLabel(LongMessage);
        Label shortChat = CreateLabel("привет");
        yield return null;
        yield return null;
        yield return null;

        IResolvedStyle style = chat.resolvedStyle;
        IResolvedStyle shortStyle = shortChat.resolvedStyle;

        Debug.Log(
            $"[ChatBubble] long w={style.width} h={style.height} maxWidth={style.maxWidth} " +
            $"padL={style.paddingLeft} padR={style.paddingRight} " +
            $"padT={style.paddingTop} padB={style.paddingBottom} " +
            $"bg={style.backgroundColor} color={style.color} " +
            $"borderW={style.borderLeftWidth} radius={style.borderTopLeftRadius} " +
            $"| short w={shortStyle.width} h={shortStyle.height}");

        // Стиль класса применился: подложка и цвет текста не дефолтные.
        Assert.That(
            style.backgroundColor,
            Is.EqualTo(new Color(7f / 255f, 13f / 255f, 20f / 255f, 0.9f)).Within(0.01f),
            "world-label-chat background is not the HUD panel surface.");
        Assert.That(
            style.color,
            Is.EqualTo(new Color(0xf0 / 255f, 0xf6 / 255f, 0xf8 / 255f, 1f)).Within(0.01f),
            "Chat text must be the HUD primary text colour, not black.");

        // Перенос обязателен, и облако обязано от него расти: без этого текст
        // выходит за подложку, как и было в исходной жалобе.
        Assert.That(style.whiteSpace, Is.EqualTo(WhiteSpace.Normal));
        Assert.That(
            style.height,
            Is.GreaterThan(shortStyle.height + 1f),
            "The bubble did not grow taller for wrapped text.");
        Assert.That(
            style.height,
            Is.GreaterThan(style.paddingTop + style.paddingBottom),
            "The bubble box is shorter than its own padding: the text cannot be inside it.");

        // Короткое сообщение обжимается по тексту, длинное — по max-width, и
        // обе рамки остаются уже экрана.
        Assert.That(
            shortStyle.width,
            Is.LessThan(style.width),
            "Short and wrapped messages render at the same width: the frame is not hugging text.");
        float cap = 360f + style.paddingLeft + style.paddingRight +
                    style.borderLeftWidth + style.borderRightWidth;
        Assert.That(
            style.width,
            Is.LessThanOrEqualTo(cap + 0.5f),
            "max-width: 360px does not clamp the bubble.");
    }

    private Label CreateLabel(string text)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore, enableRichText = false };
        label.AddToClassList("world-label-chat");
        _document.rootVisualElement.Add(label);
        return label;
    }

    private UIDocument FindDocument()
    {
        foreach (UIDocument candidate in
                 Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include))
        {
            if (candidate != null && candidate.isActiveAndEnabled &&
                candidate.rootVisualElement != null && candidate.rootVisualElement.panel != null)
            {
                return candidate;
            }
        }

        return null!;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        if (_host != null)
        {
            Object.Destroy(_host);
            _host = null!;
        }

        _document = null!;
        yield return PlayModeHarness.Shutdown();
    }
}
