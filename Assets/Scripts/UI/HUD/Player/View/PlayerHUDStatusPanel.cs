#nullable enable

using System;
using System.Collections.Generic;
using Fodinae.UI.HUD.Player.Model;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI.HUD.Player.View;

/// <summary>
/// Manages live status lines, temporary buffs and expiration timers in the HUD.
/// </summary>
public sealed class PlayerHUDStatusPanel
{
    private readonly Dictionary<string, VisualElement> _statusLineElements = new();
    private readonly Dictionary<string, IVisualElementScheduledItem> _statusSchedules = new();
    private VisualElement? _statusPanel;

    public void Initialize(VisualElement root)
    {
        _statusPanel = root.Q<VisualElement>("StatusPanel");
    }

    public void Rebuild(PlayerStatsModel? stats)
    {
        if (_statusPanel == null || stats == null)
        {
            return;
        }

        var currentLines = stats.StatusLines;
        if (currentLines.Count == 0)
        {
            _statusPanel.style.display = DisplayStyle.None;
            ClearSchedules();
            _statusLineElements.Clear();
            _statusPanel.Clear();
            return;
        }

        _statusPanel.style.display = DisplayStyle.Flex;
        var toRemove = new List<string>();
        foreach (var kvp in _statusLineElements)
        {
            if (!currentLines.ContainsKey(kvp.Key))
            {
                toRemove.Add(kvp.Key);
            }
        }

        foreach (var key in toRemove)
        {
            _statusPanel.Remove(_statusLineElements[key]);
            if (_statusSchedules.TryGetValue(key, out var schedule))
            {
                schedule.Pause();
                _statusSchedules.Remove(key);
            }

            _statusLineElements.Remove(key);
        }

        foreach (var kvp in currentLines)
        {
            if (_statusLineElements.TryGetValue(kvp.Key, out var existing))
            {
                if (existing is Label label)
                {
                    UpdateStatusLabel(label, kvp.Value);
                    label.style.color = kvp.Value.Color;
                }
            }
            else
            {
                var row = new Label();
                row.AddToClassList("hud-status-line");
                row.style.color = kvp.Value.Color;
                UpdateStatusLabel(row, kvp.Value);
                _statusPanel.Add(row);

                if (kvp.Value.Expiry > 0)
                {
                    var schedule = row.schedule.Execute(() =>
                    {
                        if (_statusPanel == null || !_statusLineElements.ContainsKey(kvp.Key))
                        {
                            return;
                        }

                        var entry = stats.StatusLines.GetValueOrDefault(kvp.Key);
                        if (entry.Text == null)
                        {
                            return;
                        }

                        UpdateStatusLabel(row, entry);
                    }).Every(1000);
                    _statusSchedules[kvp.Key] = schedule;
                }

                _statusLineElements[kvp.Key] = row;
            }
        }
    }

    private static void UpdateStatusLabel(Label label, StatusLineEntry entry)
    {
        if (entry.Text == null || entry.Text.Length == 0)
        {
            label.text = string.Empty;
            return;
        }

        var name = entry.Text[0];
        if (entry.Expiry > 0)
        {
            var remaining = Math.Max(0, entry.Expiry - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            label.text = $"{name}: {FormatTime(remaining)}";
        }
        else if (entry.Text.Length > 1)
        {
            label.text = $"{name}: {entry.Text[1]}";
        }
        else
        {
            label.text = name;
        }
    }

    private static string FormatTime(long seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    public void ClearSchedules()
    {
        foreach (var schedule in _statusSchedules.Values)
        {
            schedule.Pause();
        }

        _statusSchedules.Clear();
    }
}
