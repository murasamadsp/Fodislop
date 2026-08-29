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

        if (_nicknameText.font == null)
        {
            var font = Resources.Load<TMP_FontAsset>("Fonts/JetBrainsMono_SDF") ??
                       Resources.Load<TMP_FontAsset>("Fonts/Exo2_SDF") ??
                       TMP_Settings.defaultFontAsset;
            if (font != null)
            {
                _nicknameText.font = font;
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
        if (_hasUpdatedLabels && (robotPosition - _lastLabelsPosition).sqrMagnitude <= 1e-8f)
        {
            return;
        }

        if (_nicknameText != null)
        {
            Bounds spriteBounds = skinSprite != null
                ? TransformSpriteBounds(robotTransform, skinSprite)
                : new Bounds(robotPosition, Vector3.one);
            Vector3 topRight = new(spriteBounds.max.x, spriteBounds.max.y + 0.5f, robotPosition.z);

            _nicknameText.transform.SetPositionAndRotation(topRight, Quaternion.identity);
        }

        if (clanTransform != null)
        {
            clanTransform.SetPositionAndRotation(robotPosition + new Vector3(0.6f, -0.5f, 0), Quaternion.identity);
        }

        _lastLabelsPosition = robotPosition;
        _hasUpdatedLabels = true;
    }

    public void InvalidatePosition()
    {
        _hasUpdatedLabels = false;
    }

    private static Bounds TransformSpriteBounds(Transform spriteTransform, Sprite sprite)
    {
        Bounds local = sprite.bounds;
        Vector3 minimum = spriteTransform.TransformPoint(local.min);
        Vector3 maximum = spriteTransform.TransformPoint(local.max);
        return new Bounds((minimum + maximum) * 0.5f, maximum - minimum);
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
