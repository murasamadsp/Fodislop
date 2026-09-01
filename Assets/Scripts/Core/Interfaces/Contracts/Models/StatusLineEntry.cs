#nullable enable

using UnityEngine;

namespace Fodinae.Core.Models
{
    /// <summary>
    /// Нейтральная запись статус-строки игрока (Core, не UI-модель), чтобы
    /// Networking/domain не зависели от presentation.
    /// </summary>
    public readonly record struct StatusLineEntry(string[] Text, Color Color, byte BlinkRate, long Expiry);
}
