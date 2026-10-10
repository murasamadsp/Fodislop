#nullable enable

using System;
using System.Text;
using System.Text.RegularExpressions;
using MinesServer.Networking.Server.Packets.Chat;
using UnityEngine;

namespace Kern.UI;

internal static class ChatMessageFormatter
{
    private static readonly Regex s_noparseClose = new("</noparse", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool s_invalidMuteExpiryLogged;

    // Ник и текст пишет другой игрок, а строка уходит в метку с разметкой:
    // без экранирования <size>, <color>, <sprite> и подделка системного
    // сообщения доступны любому. Текст оборачивается в noparse, а закрывающий
    // тег внутри разрывается невидимым пробелом, чтобы из обёртки не выйти.
    public static string PlainText(string? text)
    {
        var sb = new StringBuilder();
        AppendPlainText(sb, text);
        return sb.ToString();
    }

    private static void AppendPlainText(StringBuilder sb, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        sb.Append("<noparse>");
        sb.Append(s_noparseClose.Replace(text, "<\u200B/noparse"));
        sb.Append("</noparse>");
    }

    public static string FormatGlobal(ChatMessagePacket msg, DateTime now)
    {
        var sb = new StringBuilder(128);
        sb.Append("<color=#888888>[");
        sb.Append(now.Hour.ToString("D2"));
        sb.Append(':');
        sb.Append(now.Minute.ToString("D2"));
        sb.Append("]</color> <color=#");
        sb.Append(msg.NicknameColor.R.ToString("X2"));
        sb.Append(msg.NicknameColor.G.ToString("X2"));
        sb.Append(msg.NicknameColor.B.ToString("X2"));
        sb.Append('>');
        AppendPlainText(sb, msg.PlayerName);
        sb.Append("</color>: <color=#");
        sb.Append(msg.MessageColor.R.ToString("X2"));
        sb.Append(msg.MessageColor.G.ToString("X2"));
        sb.Append(msg.MessageColor.B.ToString("X2"));
        sb.Append('>');
        AppendPlainText(sb, msg.Message);
        sb.Append("</color>");
        return sb.ToString();
    }

    public static string FormatMuteEnd(long unixMilliseconds)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds)
                .ToLocalTime()
                .ToString("g");
        }
        catch (ArgumentOutOfRangeException)
        {
            // Серверный ввод не должен ронять клиент: битый timestamp в пакете
            // мута — это данные, а не контрактная ошибка. Отображаем как есть.
            if (s_invalidMuteExpiryLogged)
            {
                return unixMilliseconds.ToString();
            }

            s_invalidMuteExpiryLogged = true;
            Debug.LogWarning(
                $"[GlobalChat] Mute packet contains invalid expiry timestamp: {unixMilliseconds}");
            return unixMilliseconds.ToString();
        }
    }
}
