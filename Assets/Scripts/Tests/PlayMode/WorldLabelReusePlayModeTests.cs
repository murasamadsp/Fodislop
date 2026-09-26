#nullable enable

using System.Collections;
using Kern.Core.Interfaces;
using Kern.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Kern.Tests.PlayMode;

/// <summary>
/// Повторный показ мировой метки.
///
/// Метка уходит с экрана через visibility:hidden, а не через display:none, и
/// это не стилистический выбор. При display:none элемент выпадает из раскладки,
/// ширина и высота приходят NaN, и снять скрытие мог только ApplyVisible — а он
/// выходил раньше, по неизвестному размеру. Метка застревала скрытой навсегда:
/// пузырь, переиспользованный из пула после истечения трёх секунд, не показывал
/// сообщение больше никогда, и после первого сообщения с интервалом больше трёх
/// секунд локальный чат замолкал.
///
/// Тест гоняет тот же цикл напрямую через IWorldLabels и отдельно проверяет
/// зависимость от длины текста, потому что исходное наблюдение её утверждало, а
/// дедлок длины не объясняет.
///
/// Метка читается из DOM, а не через IWorldLabel: контракт намеренно не
/// отдаёт наружу Label, и расширять его ради теста не нужно.
/// </summary>
[TestFixture]
public sealed class WorldLabelReusePlayModeTests
{
    private const string LongMessage =
        "Очень длинное сообщение локального чата, которое обязано перенестись на несколько строк "
        + "и поэтому обязано сделать облако заметно выше короткого";

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

    // Метка отсекается LateTick'ом, если её мировая точка вне кадра, поэтому
    // каждый кадр ставим её перед камерой: проверяем показ, а не отсечение.
    private IEnumerator Frames(IWorldLabel label, int count)
    {
        Vector3 inView = _camera.transform.position + (_camera.transform.forward * 5f);
        for (int i = 0; i < count; i++)
        {
            label.SetPosition(inView);
            yield return null;
        }
    }

    [UnityTest]
    public IEnumerator ReusedLabel_ComesBackAfterHiding()
    {
        IWorldLabel label = _labels.Create(WorldLabelKind.ChatBubble);
        _label = label;
        Label element = FindLabel();

        label.SetText("привет");
        label.SetVisible(true);
        yield return Frames(label, 3);

        float firstWidth = element.resolvedStyle.width;
        Assert.That(element.resolvedStyle.display, Is.Not.EqualTo(DisplayStyle.None));
        Assert.That(firstWidth, Is.GreaterThan(0f));

        // Гасим так же, как гаснет истёкший пузырь, и показываем заново: из
        // пула приходит именно такая метка.
        label.SetVisible(false);
        yield return Frames(label, 3);
        Assert.That(
            element.resolvedStyle.visibility,
            Is.EqualTo(Visibility.Hidden),
            "The label must leave the screen through visibility, not display:none.");

        label.SetText("снова");
        label.SetVisible(true);
        yield return Frames(label, 3);

        IResolvedStyle style = element.resolvedStyle;
        Debug.Log(
            $"[LabelReuse] after reuse w={style.width} h={style.height} " +
            $"display={style.display} visibility={style.visibility}");

        Assert.That(
            style.display,
            Is.Not.EqualTo(DisplayStyle.None),
            "A reused label stayed out of layout and can never be shown again.");
        Assert.That(style.visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(style.width, Is.GreaterThan(0f));
        Assert.That(style.height, Is.GreaterThan(0f));
    }

    [UnityTest]
    public IEnumerator ReusedLabel_HeightFollowsTextLengthInBothDirections()
    {
        IWorldLabel label = _labels.Create(WorldLabelKind.ChatBubble);
        _label = label;
        Label element = FindLabel();

        label.SetText(LongMessage);
        label.SetVisible(true);
        yield return Frames(label, 3);
        float longHeight = element.resolvedStyle.height;
        float longWidth = element.resolvedStyle.width;

        // Гасим и поднимаем заново уже с коротким текстом: переиспользованная
        // метка обязана честно пересчитать бокс под новое содержимое.
        label.SetVisible(false);
        yield return Frames(label, 2);
        label.SetText("да");
        label.SetVisible(true);
        yield return Frames(label, 3);

        IResolvedStyle style = element.resolvedStyle;
        Debug.Log(
            $"[LabelReuse] long h={longHeight} w={longWidth} -> short h={style.height} w={style.width}");

        Assert.That(
            longHeight,
            Is.GreaterThan(style.height + 1f),
            "The long message must render taller than the short one.");
        Assert.That(style.display, Is.Not.EqualTo(DisplayStyle.None));
        Assert.That(style.visibility, Is.EqualTo(Visibility.Visible));
    }

    private Label FindLabel() =>
        _document.rootVisualElement.Q<Label>(className: "world-label-chat")
        ?? throw new AssertionException("The chat bubble label is not in the panel.");

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        _label?.Dispose();
        _label = null;
        yield return PlayModeHarness.Shutdown();
    }
}
