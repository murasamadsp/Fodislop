#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Fodinae.Core.Interfaces;
using Fodinae.UI.HUD.Player.Model;
using MinesServer.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Fodinae.UI.HUD.Player.View;

/// <summary>
/// Manages crystal textures and basket rows in the player HUD.
/// </summary>
public sealed class PlayerHUDBasketView
{
    private readonly List<Texture2D> _crystalTextures = new();
    private readonly List<Label> _basketCrystalLabels = new();

    private VisualElement? _basketContainer;

    public void Initialize(VisualElement basketContainer)
    {
        _basketContainer = basketContainer;
    }

    public async UniTask LoadCrystalTextures(IAssetLoader assetLoader, CancellationToken cancellationToken)
    {
        _crystalTextures.Clear();
        foreach (CrystalType ct in Enum.GetValues(typeof(CrystalType)))
        {
            if (ct == CrystalType.Unknown)
            {
                continue;
            }

            string name = ct.ToString().ToLowerInvariant();
            Texture2D? tex;
            try
            {
                tex = await assetLoader.GetTextureAsync(
                    "Crystals/" + name,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[PlayerHUD] Optional crystal texture '{name}' was skipped: " +
                    exception.Message);
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (tex != null)
            {
                _crystalTextures.Add(tex);
            }
        }
    }

    public void RebuildRows()
    {
        if (_basketContainer == null)
        {
            return;
        }

        _basketContainer.Clear();
        _basketCrystalLabels.Clear();

        for (int i = 0; i < _crystalTextures.Count; i++)
        {
            var row = new VisualElement();
            row.AddToClassList("hud-crystal-row");

            var dot = new Image();
            dot.AddToClassList("hud-crystal-dot");
            if (_crystalTextures[i] != null)
            {
                dot.style.backgroundImage = new StyleBackground(_crystalTextures[i]);
            }

            row.Add(dot);

            var label = new Label("0/0");
            label.AddToClassList("hud-crystal-label");
            row.Add(label);

            _basketCrystalLabels.Add(label);
            _basketContainer.Add(row);
        }
    }

    public void Refresh(PlayerStatsModel stats)
    {
        for (int i = 0; i < _basketCrystalLabels.Count && i < stats.BasketContents.Length; i++)
        {
            _basketCrystalLabels[i].text = $"{FormatCompact(stats.BasketContents[i])}/{FormatCompact(stats.BasketCapacity)}";
        }
    }

    private static string FormatCompact(long val)
    {
        if (val >= 1_000_000)
        {
            return $"{val / 1_000_000f:F1}M";
        }

        if (val >= 10_000)
        {
            return $"{val / 1_000}K";
        }

        return val.ToString("N0");
    }
}
