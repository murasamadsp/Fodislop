#nullable enable

using System;
using UnityEngine;

namespace Kern.Networking;

/// <summary>
/// Единственный путь, которым сеть открывает ссылку в системе.
/// </summary>
///
/// Ссылки приходят от сервера (OpenURLPacket, адрес обновления) и от VK API,
/// а транспорт — TCP без шифрования, так что подменить их может и посредник.
/// Application.OpenURL отдаёт строку системе как есть: на Windows это
/// ShellExecute, и file:, UNC-пути и обработчики протоколов (ms-msdt:,
/// search-ms:) запускают программы. Поэтому пропускаются только веб-адреса.
public static class ExternalUrlOpener
{
    public static bool TryOpen(string? url, string source)
    {
        if (!IsAllowed(url))
        {
            Debug.LogWarning($"[{source}] Отклонена ссылка не http(s): '{url}'");
            return false;
        }

        Application.OpenURL(url);
        return true;
    }

    public static bool IsAllowed(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
        !string.IsNullOrEmpty(uri.Host) &&
        !uri.IsUnc;
}
