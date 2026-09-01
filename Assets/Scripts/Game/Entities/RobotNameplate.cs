#nullable enable

using System;
using Fodinae.Core;
using Fodinae.Core.Lifecycle;
using TMPro;
using UnityEngine;

namespace Fodinae.Game;

/// <summary>
/// Manages the floating world-space nickname plate for a Robot entity.
/// </summary>
public sealed class RobotNameplate
{
    private static TMP_FontAsset? s_nameplateFont;
    private TextMeshPro? _nicknameText;
    private Vector3 _lastLabelsPosition;
    private bool _hasUpdatedLabels;

    public void Initialize(
        Transform robotTransform,
        uint botId,
        string nickname,
        bool isLocalPlayer,
        ISceneObjectFactory sceneObjects)
    {
        Transform? existingNickname = robotTransform.Find("Nickname");
        if (isLocalPlayer)
        {
            if (existingNickname != null)
            {
                existingNickname.gameObject.SetActive(false);
            }

            return;
        }

        GameObject textGo;
        if (existingNickname != null)
        {
            textGo = existingNickname.gameObject;
        }
        else if (sceneObjects != null)
        {
            textGo = sceneObjects.Create("Nickname", RuntimeOwner.FloatingUI);
        }
        else
        {
            throw new InvalidOperationException(
                $"[RobotNameplate] ISceneObjectFactory was not injected before creating nickname for bot {botId}.");
        }

        Transform? floatingOwner = sceneObjects.GetOwner(RuntimeOwner.FloatingUI);
        if (floatingOwner != null)
        {
            textGo.transform.SetParent(floatingOwner, worldPositionStays: true);
        }

        _nicknameText = textGo.GetComponent<TextMeshPro>() ?? textGo.AddComponent<TextMeshPro>();
        textGo.SetActive(true);
        _nicknameText.alignment = TextAlignmentOptions.TopLeft;
        _nicknameText.rectTransform.pivot = new Vector2(0f, 1f);
        _nicknameText.fontSize = 6.4f;
        _nicknameText.textWrappingMode = TextWrappingModes.NoWrap;
        _nicknameText.overflowMode = TextOverflowModes.Overflow;
        _nicknameText.color = Color.white;

        // The project's UI fonts (Exo2/Unbounded/JetBrainsMono) are TextCore
        // FontAssets for UI Toolkit and cannot feed a TMPro text object (their
        // runtime type is FontAsset, not TMP_FontAsset; TMP_Text.font needs the
        // latter). World-space text uses a dedicated mono TMP font built from
        // JetBrainsMono (keeps the intended mono look) which has the Noto Sans
        // SC/TC TMP fonts as its CJK fallback chain.
        if (_nicknameText.font == null)
        {
            s_nameplateFont ??= Resources.Load<TMP_FontAsset>("Fonts/JetBrainsMono_TMP") ??
                                Resources.Load<TMP_FontAsset>("Fonts/NotoSansSC_TMP") ??
                                TMP_Settings.defaultFontAsset;
            if (s_nameplateFont != null)
            {
                _nicknameText.font = s_nameplateFont;
            }
        }

        _nicknameText.text = !string.IsNullOrEmpty(nickname) ? nickname : string.Empty;

        MeshRenderer textRenderer = _nicknameText.GetComponent<MeshRenderer>() ??
            throw new InvalidOperationException($"[RobotNameplate] Nickname MeshRenderer is missing for bot {botId}.");
        UnityRenderLayerContracts.ApplyWorldUI(textRenderer, 100);
    }

    public void SetText(string text, bool isLocalPlayer)
    {
        if (_nicknameText != null)
        {
            _nicknameText.text = isLocalPlayer ? string.Empty : text;
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (_nicknameText != null)
        {
            _nicknameText.enabled = enabled;
        }
    }

    public void ApplyLayer()
    {
        if (_nicknameText != null)
        {
            MeshRenderer? nicknameRenderer = _nicknameText.GetComponent<MeshRenderer>();
            if (nicknameRenderer != null)
            {
                UnityRenderLayerContracts.ApplyWorldUI(nicknameRenderer, 100);
            }
        }
    }

    public void UpdatePosition(Vector3 robotPosition, Sprite? skinSprite, Transform robotTransform, Transform? clanTransform)
    {
        if (_hasUpdatedLabels &&
            (robotPosition - _lastLabelsPosition).sqrMagnitude <= 1e-8f)
        {
            return;
        }

        if (_nicknameText != null)
        {
            Vector3 topRight = new(robotPosition.x + 0.5f, robotPosition.y + 0.5f, robotPosition.z);
            _nicknameText.transform.SetPositionAndRotation(topRight, Quaternion.identity);
        }

        if (clanTransform != null)
        {
            clanTransform.SetPositionAndRotation(robotPosition + new Vector3(0.6f, -0.5f, 0f), Quaternion.identity);
        }

        _lastLabelsPosition = robotPosition;
        _hasUpdatedLabels = true;
    }

    public void InvalidatePosition()
    {
        _hasUpdatedLabels = false;
    }

    public void Destroy()
    {
        if (_nicknameText != null)
        {
            UnityEngine.Object.Destroy(_nicknameText.gameObject);
            _nicknameText = null;
        }
    }
}
