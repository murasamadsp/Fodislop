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
/// перенос текста, обжимать текст по ширине и не вылезать за max-width; рамку
/// рисует спрайт, поэтому в USS у облака не должно остаться ни подложки-цвета,
/// ни контура, а текст на белой заливке обязан быть тёмным.
///
/// Метки здесь созданы напрямую, без WorldLabels, и спрайта на них нет: сам
/// спрайт, его границы 9-slice и масштаб по зуму проверяет
/// WorldLabelZoomScalePlayModeTests, где облако приходит обычным пакетом
/// локального чата.
/// </summary>
[TestFixture]
public sealed class WorldLabelChatLayoutPlayModeTests
{
    private const string LongMessage =
        "Очень длинное сообщение локального чата, которое обязано перенестись на несколько строк "
        + "и поэтому обязано сделать облако заметно выше";

    // Поля облака из WorldLabels.uss. Панель работает в ScaleWithScreenSize
    // (1920x1080, match), поэтому USS-пиксели приходят в resolvedStyle уже
    // пересчитанными в пункты панели, и сравнивать их с этими числами напрямую
    // нельзя: на нестандартном разрешении придёт 14.94 вместо 15. Значения
    // используются только в отношениях, которые коэффициент панели сокращает.
    private const float TailPaddingPx = 15f;
    private const float SidePaddingPx = 3f;

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
            $"color={style.color} sliceL={style.unitySliceLeft} sliceR={style.unitySliceRight} " +
            $"sliceT={style.unitySliceTop} sliceB={style.unitySliceBottom} " +
            $"bgSize={style.backgroundSize.sizeType} " +
            $"| short w={shortStyle.width} h={shortStyle.height}");

        // Рамку рисует спрайт, а не подложка-цвет: подложка встала бы поверх
        // спрайта, и рамка снова разошлась бы с текстом. Здесь метка создана
        // напрямую, без WorldLabels, поэтому спрайта на ней нет — проверяется
        // именно отсутствие подложки и контура из темы.
        Assert.That(
            style.backgroundColor,
            Is.EqualTo(new Color(0f, 0f, 0f, 0f)),
            "world-label-chat must not paint a CSS background over the bubble sprite.");
        Assert.That(style.borderLeftWidth, Is.EqualTo(0f), "The bubble frame is the sprite, not a CSS border.");
        Assert.That(style.borderTopWidth, Is.EqualTo(0f));
        Assert.That(style.borderRightWidth, Is.EqualTo(0f));
        Assert.That(style.borderBottomWidth, Is.EqualTo(0f));

        // Заливка спрайта белая, поэтому текст обязан быть тёмным: светлый
        // --text-primary на ней не читался.
        Assert.That(
            style.color,
            Is.EqualTo(new Color(0x04 / 255f, 0x08 / 255f, 0x0e / 255f, 1f)).Within(0.01f),
            "Chat text must be dark: the bubble sprite is filled with white.");

        // Нижнее поле держит текст над хвостом спрайта, который занимает
        // последние 14 px исходника снизу: USS задаёт 3px по бокам и сверху и
        // 15px снизу.
        //
        // Проверяется отношением, а не самим числом: панель в ScaleWithScreenSize
        // (1920x1080, match) пересчитывает пиксели USS в свои пункты, поэтому
        // resolvedStyle отдаёт 14.94 вместо 15 на любом разрешении, и не 1920.
        // Отношение полей к коэффициенту панели инвариантно.
        Assert.That(
            style.paddingBottom / style.paddingTop,
            Is.EqualTo(TailPaddingPx / SidePaddingPx).Within(0.01f),
            $"The bubble padding does not reserve room for the sprite tail: " +
            $"T={style.paddingTop} R={style.paddingRight} B={style.paddingBottom} L={style.paddingLeft}");
        Assert.That(
            style.paddingLeft,
            Is.EqualTo(style.paddingRight).Within(0.01f),
            "The bubble side padding is asymmetric.");

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
        // Ни border-*, ни scale в раскладке не участвуют: боксу отведено ровно
        // столько, сколько занимает текст с полями.
        float cap = 360f + style.paddingLeft + style.paddingRight;
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
