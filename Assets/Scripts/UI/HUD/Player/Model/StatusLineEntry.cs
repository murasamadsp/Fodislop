#nullable enable

using UnityEngine;

namespace Fodinae.Scripts.UI.HUD.Player.Model
{
    public readonly record struct StatusLineEntry(string[] Text, Color Color, byte BlinkRate, long Expiry);
}
