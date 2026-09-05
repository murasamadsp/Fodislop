#nullable enable

using UnityEngine.UIElements;

namespace Fodinae.UI;
/// <summary>
/// Плавное появление и скрытие поверх <see cref="UIState"/>.
/// </summary>
/// <remarks>
/// ЗАЧЕМ ОТДЕЛЬНЫЙ ШАГ, А НЕ ПЕРЕХОД НА <c>is-hidden</c>. Класс скрытия —
/// это <c>display: none</c>, а <c>display</c> не анимируется в принципе:
/// элемент выпадает из раскладки мгновенно, и анимировать нечего. Поэтому
/// в <c>Animations.uss</c> заведена вторая, анимируемая пара состояний —
/// прозрачность и масштаб, — а этот класс сшивает её с видимостью:
///
///   показать — снять <c>is-hidden</c>, и ТОЛЬКО СЛЕДУЮЩИМ КАДРОМ включить
///              видимое состояние: переход обязан стартовать из начального,
///              а элемент, вернувшийся в раскладку в этом же кадре, ещё не
///              успел его получить, и анимация просто не сыграет;
///   скрыть   — снять видимое состояние и дождаться конца перехода, иначе
///              <c>display: none</c> срежет анимацию на первом кадре.
///
/// Длительность живёт в USS и сюда не дублируется: числа перехода — часть
/// вида, а вид объявлен в таблицах стилей. Отсюда страховка по расписанию:
/// если у элемента нет класса <c>sci-fi-window-anim</c>, перехода не будет
/// и <see cref="TransitionEndEvent"/> не придёт никогда — без страховки
/// такой элемент остался бы навсегда видимым.
/// </remarks>
public static class UIVisibilityAnimator
{
    /// <summary>Начальное состояние перехода: прозрачно и чуть уменьшено.</summary>
    public const string HiddenState = "sci-fi-window-anim--hidden";

    /// <summary>Конечное состояние перехода.</summary>
    public const string ShownState = "sci-fi-window-anim--shown";

    /// <summary>
    /// Потолок ожидания конца перехода. Не длительность анимации, а срок,
    /// после которого мы считаем, что перехода не было вовсе.
    /// </summary>
    private const long FallbackMilliseconds = 1000;

    /// <summary>Показать элемент с анимацией. <c>null</c> игнорируется.</summary>
    public static void Show(VisualElement? element)
    {
        if (element == null)
        {
            return;
        }

        element.AddToClassList(HiddenState);
        element.RemoveFromClassList(ShownState);
        UIState.Show(element);

        element.schedule.Execute(() =>
        {
            element.RemoveFromClassList(HiddenState);
            element.AddToClassList(ShownState);
        });
    }

    /// <summary>Скрыть элемент с анимацией. <c>null</c> игнорируется.</summary>
    public static void Hide(VisualElement? element)
    {
        if (element == null)
        {
            return;
        }

        if (UIState.IsHidden(element))
        {
            return;
        }

        element.RemoveFromClassList(ShownState);
        element.AddToClassList(HiddenState);

        bool finished = false;
        void Finish()
        {
            if (finished)
            {
                return;
            }

            finished = true;
            UIState.Hide(element);
        }

        element.RegisterCallbackOnce<TransitionEndEvent>(_ => Finish());
        element.schedule.Execute(Finish).StartingIn(FallbackMilliseconds);
    }

    /// <summary>Показать или скрыть с анимацией. <c>null</c> игнорируется.</summary>
    public static void SetHidden(VisualElement? element, bool hidden)
    {
        if (hidden)
        {
            Hide(element);
        }
        else
        {
            Show(element);
        }
    }
}
