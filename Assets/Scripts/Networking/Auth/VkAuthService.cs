#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using Fodinae.Core;
using Fodinae.Core.Interfaces;
using UnityEngine;
using UnityEngine.Networking;

namespace Fodinae.Networking.Auth
{
    /// <summary>
    /// Сессия VK ID: access-токен, идентификатор пользователя и профиль.
    ///
    /// Настоящий вход через VK завершается только после обмена VK access token
    /// на случайный игровой токен у доверенного backend. Клиент не выводит
    /// игровую идентичность из публичного VK user id.
    /// </summary>
    public readonly struct VkSession
    {
        public string AccessToken { get; init; }
        public long UserId { get; init; }
        public string FirstName { get; init; }
        public string LastName { get; init; }
        public string AvatarUrl { get; init; }
        public long ExpiresAtUnix { get; init; }

        public string DisplayName => string.IsNullOrEmpty(FirstName)
            ? $"id{UserId}"
            : string.IsNullOrEmpty(LastName) ? FirstName : $"{FirstName} {LastName}";

        public bool IsValid => UserId > 0 && ExpiresAtUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public readonly struct VkAuthResult
    {
        public bool Success { get; init; }
        public VkSession Session { get; init; }
        public string GameToken { get; init; }
        public string Error { get; init; }
    }

    /// <summary>
    /// Провайдер симуляции VK-входа для офлайн-режима (DummyConnection).
    /// Заменяет реальный device-флоу, чтобы «Войти через VK» работало без сети
    /// и без client_id. В бутстрапе подключается как IVkOfflineProvider;
    /// когда появятся другие офлайн-провайдеры, они реализуют тот же интерфейс.
    /// </summary>
    public interface IVkOfflineProvider
    {
        VkAuthResult SimulateVkLogin();
    }

    /// <summary>
    /// Авторизация через VK ID по device-флоу (id.vk.com/oauth2/device_authorize).
    /// Выбран именно он, а не authorization code: для десктопного клиента не
    /// нужен ни редирект на loopback-порт, ни локальный HTTP-слушатель —
    /// браузер открывает ссылку подтверждения, клиент опрашивает
    /// device_token. PKCE (code_verifier) позволяет работать без client_secret.
    /// </summary>
    public static class VkAuthService
    {
        /// <summary>
        /// Заглушка. Настоящий client_id VK-приложения задаётся в
        /// ClientConfig.VkClientId (профиль проекта), иначе вход через VK
        /// честно сообщает об ошибке.
        /// </summary>
        public const string DefaultClientId = "";

        private const string DeviceIdKey = "Vk.DeviceId";
        private const string AccessTokenKey = "Vk.AccessToken";
        private const string UserIdKey = "Vk.UserId";
        private const string UserNameKey = "Vk.UserName";
        private const string AvatarKey = "Vk.AvatarUrl";
        private const string ExpiresAtKey = "Vk.ExpiresAt";

        private const string DeviceAuthorizeUrl = "https://id.vk.com/oauth2/device_authorize";
        private const string DeviceTokenUrl = "https://id.vk.com/oauth2/device_token";

        /// <summary>
        /// Офлайн-провайдер VK-входа. Ставится бутстрапом из DI (сейчас это
        /// DummyConnection); при UseDummyConnection=true LoginAsync идёт через
        /// него и не трогает сеть VK.
        /// </summary>
        public static IVkOfflineProvider? OfflineProvider { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForDomainReload()
        {
            OfflineProvider = null;
        }

        public static bool HasValidSession => LoadSession().IsValid;

        public static VkSession LoadSession()
        {
            long expiresAt = long.TryParse(PlayerPrefs.GetString(ExpiresAtKey, "0"), out long e) ? e : 0;
            return new VkSession
            {
                AccessToken = PlayerPrefs.GetString(AccessTokenKey, string.Empty),
                UserId = long.TryParse(PlayerPrefs.GetString(UserIdKey, "0"), out long uid) ? uid : 0,
                FirstName = PlayerPrefs.GetString(UserNameKey, string.Empty),
                LastName = string.Empty,
                AvatarUrl = PlayerPrefs.GetString(AvatarKey, string.Empty),
                ExpiresAtUnix = expiresAt,
            };
        }

        /// <summary>
        /// Запускает device-флоу: получает ссылку подтверждения, открывает её в
        /// браузере, опрашивает сервер до выдачи токена (или ошибки).
        /// </summary>
        public static async UniTask<VkAuthResult> LoginAsync(IClientConfigManager clientConfig)
        {
            // Офлайн-режим: не ходим в VK, а просим dummy-провайдера сыграть
            // вход. Возврат происходит синхронно, без браузера.
            if (clientConfig?.Config is { UseDummyConnection: true } && OfflineProvider != null)
            {
                OpenDummyAuthPage();
                await UniTask.Delay(1200);
                return OfflineProvider.SimulateVkLogin();
            }

            string clientId = ResolveClientId(clientConfig);
            string backendUrl = clientConfig?.Config?.VkAuthBackendUrl ?? string.Empty;
            if (string.IsNullOrWhiteSpace(clientId) ||
                !Uri.TryCreate(backendUrl, UriKind.Absolute, out Uri backendUri) ||
                !string.Equals(backendUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return Error("gateway.auth.vk_not_configured");
            }

            string deviceId = LoadOrCreateDeviceId();
            string state = RandomToken(16);
            string codeVerifier = RandomToken(64);
            string codeChallenge = Base64Url(Sha256(codeVerifier));

            try
            {
                // Шаг 1: получить user_confirm_link + device_code.
                var authorizeForm = new WWWForm();
                authorizeForm.AddField("client_id", clientId);
                authorizeForm.AddField("device_id", deviceId);
                authorizeForm.AddField("scope", "phone");
                authorizeForm.AddField("state", state);
                authorizeForm.AddField("code_challenge", codeChallenge);
                authorizeForm.AddField("code_challenge_method", "S256");

                string authorizeJson = await PostJsonAsync(DeviceAuthorizeUrl, authorizeForm);
                var authorize = JsonUtility.FromJson<DeviceAuthorizeResponse>(authorizeJson);
                if (!string.IsNullOrEmpty(authorize.error))
                {
                    return Error(authorize.error_description);
                }

                if (string.IsNullOrEmpty(authorize.user_confirm_link) || string.IsNullOrEmpty(authorize.device_code))
                {
                    return Error("device_authorize: empty response");
                }

                Application.OpenURL(authorize.user_confirm_link);

                // Шаг 2: опрашивать device_token, пока пользователь подтвердит.
                int interval = Mathf.Max(3, authorize.interval);
                long deadline = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Min(authorize.expires_in > 0 ? authorize.expires_in : 180, 180);
                while (true)
                {
                    await UniTask.Delay(interval * 1000);

                    var tokenForm = new WWWForm();
                    tokenForm.AddField("client_id", clientId);
                    tokenForm.AddField("device_id", deviceId);
                    tokenForm.AddField("device_code", authorize.device_code);
                    tokenForm.AddField("state", state);
                    tokenForm.AddField("code_verifier", codeVerifier);

                    string tokenJson = await PostJsonAsync(DeviceTokenUrl, tokenForm);
                    var token = JsonUtility.FromJson<DeviceTokenResponse>(tokenJson);
                    if (!string.IsNullOrEmpty(token.access_token))
                    {
                        return await ExchangeWithBackendAsync(token.access_token, clientId, deviceId, backendUrl);
                    }

                    switch (token.error)
                    {
                        case "authorization_pending":
                            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= deadline)
                            {
                                return Error("gateway.auth.vk_expired");
                            }

                            continue;
                        case "authorization_declined":
                        case "authorization_expired":
                            return Error("gateway.auth.vk_declined");
                        default:
                            return Error(token.error_description);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[VkAuth] flow failed: {e.Message}");
                return Error("gateway.auth.vk_network");
            }
        }

        public static string ResolveClientId(IClientConfigManager? clientConfig)
        {
            string? configured = clientConfig?.Config?.VkClientId;
            return string.IsNullOrWhiteSpace(configured) ? DefaultClientId : configured;
        }

        private static async UniTask<VkAuthResult> ExchangeWithBackendAsync(
            string accessToken,
            string clientId,
            string deviceId,
            string backendUrl)
        {
            var form = new WWWForm();
            form.AddField("access_token", accessToken);
            form.AddField("client_id", clientId);
            form.AddField("device_id", deviceId);
            string json = await PostJsonAsync(backendUrl, form);
            var response = JsonUtility.FromJson<BackendExchangeResponse>(json);
            if (response == null || string.IsNullOrWhiteSpace(response.game_token) || response.user_id <= 0)
            {
                return Error(response?.error ?? "VK backend returned an invalid session");
            }

            var session = new VkSession
            {
                AccessToken = string.Empty,
                UserId = response.user_id,
                FirstName = response.first_name ?? string.Empty,
                LastName = response.last_name ?? string.Empty,
                AvatarUrl = response.avatar_url ?? string.Empty,
                ExpiresAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Max(response.expires_in, 60),
            };

            PlayerPrefs.DeleteKey(AccessTokenKey);
            PlayerPrefs.SetString(UserIdKey, session.UserId.ToString());
            PlayerPrefs.SetString(UserNameKey, session.FirstName);
            PlayerPrefs.SetString(AvatarKey, session.AvatarUrl);
            PlayerPrefs.SetString(ExpiresAtKey, session.ExpiresAtUnix.ToString());
            PlayerPrefs.Save();
            return new VkAuthResult
            {
                Success = true,
                Session = session,
                GameToken = response.game_token,
            };
        }

        private static void OpenDummyAuthPage()
        {
            const string html = """
                <!doctype html><html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width">
                <title>VK ID — тестовый вход</title><style>body{margin:0;background:#f2f3f5;font:16px system-ui;color:#19191a;display:grid;place-items:center;height:100vh}.card{width:min(420px,calc(100% - 48px));background:white;border-radius:20px;padding:32px;box-shadow:0 12px 48px #0002;text-align:center}.logo{margin:auto;width:56px;height:56px;border-radius:16px;background:#07f;color:white;font-weight:800;font-size:24px;display:grid;place-items:center}h1{font-size:22px}.note{color:#818c99;line-height:1.5}.ok{margin-top:22px;padding:13px;border-radius:12px;background:#07f;color:white;font-weight:650}</style>
                <body><main class="card"><div class="logo">VK</div><h1>Тестовая авторизация</h1><p class="note">Это локальная имитация VK ID для Dummy-режима. Данные в VK не отправляются.</p><div class="ok">Вход подтверждён — вернитесь в игру</div></main></body></html>
                """;

            try
            {
                string path = Path.Combine(Application.temporaryCachePath, "fodinae-vk-auth-mock.html");
                File.WriteAllText(path, html, Encoding.UTF8);
                Application.OpenURL(new Uri(path).AbsoluteUri);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[VkAuth] Could not open dummy browser page: {exception.Message}");
            }
        }

        private static async UniTask<string> PostJsonAsync(string url, WWWForm form)
        {
            using var request = UnityWebRequest.Post(url, form);
            request.timeout = 15;
            await request.SendWebRequest().ToUniTask();
            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new InvalidOperationException($"{url}: {request.error}");
            }

            return request.downloadHandler.text;
        }

        private static string LoadOrCreateDeviceId()
        {
            string existing = PlayerPrefs.GetString(DeviceIdKey, string.Empty);
            if (!string.IsNullOrEmpty(existing))
            {
                return existing;
            }

            string created = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(DeviceIdKey, created);
            PlayerPrefs.Save();
            return created;
        }

        private static string RandomToken(int length)
        {
            const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~";
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }

            var sb = new StringBuilder(length);
            foreach (byte b in bytes)
            {
                sb.Append(alphabet[b % alphabet.Length]);
            }

            return sb.ToString();
        }

        private static byte[] Sha256(string value)
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(value));
        }

        private static string Base64Url(byte[] data) =>
            Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>
        /// Ошибка — либо ключ локализации (gateway.auth.vk_*), который UI
        /// переведёт через ILocalizationService, либо сырое сообщение.
        /// </summary>
        private static VkAuthResult Error(string message)
        {
            return new VkAuthResult { Success = false, Error = message };
        }

        [Serializable]
        private sealed class DeviceAuthorizeResponse
        {
            public string user_confirm_link = string.Empty;
            public string device_code = string.Empty;
            public int interval;
            public int expires_in;
            public string error = string.Empty;
            public string error_description = string.Empty;
        }

        [Serializable]
        private sealed class DeviceTokenResponse
        {
            public string access_token = string.Empty;
            public string refresh_token = string.Empty;
            public int expires_in;
            public long user_id;
            public string error = string.Empty;
            public string error_description = string.Empty;
        }

        [Serializable]
        private sealed class BackendExchangeResponse
        {
            public string game_token = string.Empty;
            public long user_id;
            public string? first_name;
            public string? last_name;
            public string? avatar_url;
            public int expires_in;
            public string? error;
        }
    }
}
