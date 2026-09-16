#nullable enable

using System;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using Kern.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace Kern.Networking.Auth;
public readonly struct VkSession
{
    public string AccessToken { get; init; }
    public long UserID { get; init; }
    public string FirstName { get; init; }
    public string LastName { get; init; }
    public string AvatarUrl { get; init; }
    public long ExpiresAtUnix { get; init; }

    public string DisplayName => string.IsNullOrEmpty(FirstName)
        ? $"id{UserID}"
        : string.IsNullOrEmpty(LastName) ? FirstName : $"{FirstName} {LastName}";

    public bool IsValid => UserID > 0 && ExpiresAtUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}

public readonly struct VkAuthResult
{
    public bool Success { get; init; }
    public VkSession Session { get; init; }
    public string GameToken { get; init; }
    public string Error { get; init; }
}

public readonly record struct AuthenticationResult(
    bool Success,
    string DisplayName,
    string Error);

public interface IAuthenticationService
{
    bool HasStoredCredentials { get; }

    bool HasVkSession { get; }

    string VkDisplayName { get; }

    UniTask<AuthenticationResult> LoginWithVkAsync();
}

public sealed class AuthenticationService : IAuthenticationService
{
    private readonly VkIdentityProvider _vk;
    private readonly IGameTokenStore _tokens;

    public AuthenticationService(VkIdentityProvider vk, IGameTokenStore tokens)
    {
        _vk = vk;
        _tokens = tokens;
    }

    public bool HasStoredCredentials => _tokens.HasToken;

    public bool HasVkSession => _vk.HasValidSession;

    public string VkDisplayName => _vk.LoadSession().DisplayName;

    public async UniTask<AuthenticationResult> LoginWithVkAsync()
    {
        VkAuthResult result = await _vk.LoginAsync();
        if (!result.Success)
        {
            return new AuthenticationResult(false, string.Empty, result.Error);
        }

        _tokens.Save(result.GameToken);
        return new AuthenticationResult(true, result.Session.DisplayName, string.Empty);
    }
}

public sealed class VkIdentityProvider
{
    public const string DefaultClientID = "";

    private const string DeviceIDKey = "Vk.DeviceId";
    private const string AccessTokenKey = "Vk.AccessToken";
    private const string UserIDKey = "Vk.UserID";
    private const string UserNameKey = "Vk.UserName";
    private const string AvatarKey = "Vk.AvatarUrl";
    private const string ExpiresAtKey = "Vk.ExpiresAt";

    private const string DeviceAuthorizeUrl = "https://id.vk.com/oauth2/device_authorize";
    private const string DeviceTokenUrl = "https://id.vk.com/oauth2/device_token";

    public bool HasValidSession => LoadSession().IsValid;

    public VkSession LoadSession()
    {
        long expiresAt = long.TryParse(PlayerPrefs.GetString(ExpiresAtKey, "0"), out long e) ? e : 0;
        return new VkSession
        {
            AccessToken = PlayerPrefs.GetString(AccessTokenKey, string.Empty),
            UserID = long.TryParse(PlayerPrefs.GetString(UserIDKey, "0"), out long userID) ? userID : 0,
            FirstName = PlayerPrefs.GetString(UserNameKey, string.Empty),
            LastName = string.Empty,
            AvatarUrl = PlayerPrefs.GetString(AvatarKey, string.Empty),
            ExpiresAtUnix = expiresAt,
        };
    }

    public async UniTask<VkAuthResult> LoginAsync()
    {
        string clientID = ResolveClientID();
        string backendUrl = ProjectRuntimeContracts.Authentication.VKBackendUrl;
        if (string.IsNullOrWhiteSpace(clientID) ||
            !Uri.TryCreate(backendUrl, UriKind.Absolute, out Uri backendUri) ||
            !string.Equals(backendUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return Error("gateway.auth.vk_not_configured");
        }

        string deviceID = LoadOrCreateDeviceID();
        string state = RandomToken(16);
        string codeVerifier = RandomToken(64);
        string codeChallenge = Base64Url(Sha256(codeVerifier));

        try
        {
            // Шаг 1: получить user_confirm_link + device_code.
            var authorizeForm = new WWWForm();
            authorizeForm.AddField("client_id", clientID);
            authorizeForm.AddField("device_id", deviceID);
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
                tokenForm.AddField("client_id", clientID);
                tokenForm.AddField("device_id", deviceID);
                tokenForm.AddField("device_code", authorize.device_code);
                tokenForm.AddField("state", state);
                tokenForm.AddField("code_verifier", codeVerifier);

                string tokenJson = await PostJsonAsync(DeviceTokenUrl, tokenForm);
                var token = JsonUtility.FromJson<DeviceTokenResponse>(tokenJson);
                if (!string.IsNullOrEmpty(token.access_token))
                {
                    return await ExchangeWithBackendAsync(token.access_token, clientID, deviceID, backendUrl);
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

    public static string ResolveClientID()
    {
        string configured = ProjectRuntimeContracts.Authentication.VKClientId;
        return string.IsNullOrWhiteSpace(configured) ? DefaultClientID : configured;
    }

    private static async UniTask<VkAuthResult> ExchangeWithBackendAsync(
        string accessToken,
        string clientID,
        string deviceID,
        string backendUrl)
    {
        var form = new WWWForm();
        form.AddField("access_token", accessToken);
        form.AddField("client_id", clientID);
        form.AddField("device_id", deviceID);
        string json = await PostJsonAsync(backendUrl, form);
        var response = JsonUtility.FromJson<BackendExchangeResponse>(json);
        if (response == null || string.IsNullOrWhiteSpace(response.game_token) || response.user_id <= 0)
        {
            return Error(response?.error ?? "VK backend returned an invalid session");
        }

        var session = new VkSession
        {
            AccessToken = string.Empty,
            UserID = response.user_id,
            FirstName = response.first_name ?? string.Empty,
            LastName = response.last_name ?? string.Empty,
            AvatarUrl = response.avatar_url ?? string.Empty,
            ExpiresAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Max(response.expires_in, 60),
        };

        PlayerPrefs.DeleteKey(AccessTokenKey);
        PlayerPrefs.SetString(UserIDKey, session.UserID.ToString());
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

    private static string LoadOrCreateDeviceID()
    {
        string existing = PlayerPrefs.GetString(DeviceIDKey, string.Empty);
        if (!string.IsNullOrEmpty(existing))
        {
            return existing;
        }

        string created = Guid.NewGuid().ToString("N");
        PlayerPrefs.SetString(DeviceIDKey, created);
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
