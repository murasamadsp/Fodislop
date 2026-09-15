#nullable enable

using UnityEngine;

namespace Fodinae.Core.Models;
public readonly record struct StatusLineEntry(string[] Text, Color Color, byte BlinkRate, long Expiry);
