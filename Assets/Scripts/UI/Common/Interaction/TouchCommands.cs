#nullable enable

using System;

namespace Kern.UI;

// Команды, у которых на клавиатуре есть клавиша, а на сенсорном экране —
// только кнопка HUD: пауза (Escape, на Android — системная «Назад»),
// глобальный чат (Tab), локальный чат (T). Кнопка HUD поднимает событие,
// владелец окна делает то же, что по своей клавише. Владельцы не знают
// о HUD, HUD не знает о владельцах.
public sealed class TouchCommands
{
    public event Action? PauseRequested;

    public event Action? GlobalChatRequested;

    public event Action? LocalChatRequested;

    public void RequestPause() => PauseRequested?.Invoke();

    public void RequestGlobalChat() => GlobalChatRequested?.Invoke();

    public void RequestLocalChat() => LocalChatRequested?.Invoke();
}
