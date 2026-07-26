using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Modules.Twitch;

public sealed class TwitchOAuthClient : ITwitchOAuthClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public TwitchOAuthClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<TwitchDeviceCode> StartDeviceAuthorizationAsync(
        string clientId,
        IReadOnlyCollection<string> scopes,
        CancellationToken cancellationToken = default)
    {
        ValidateClientId(clientId);

        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scopes"] = string.Join(' ', scopes)
            });

        using var response = await _httpClient.PostAsync(
            TwitchConstants.OAuthDeviceUrl,
            content,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<DeviceCodeResponse>(
            JsonOptions,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Twitch Device-Code-Antwort war leer.");

        return new TwitchDeviceCode(
            result.DeviceCode,
            result.UserCode,
            result.VerificationUri,
            result.ExpiresIn,
            Math.Max(1, result.Interval));
    }

    public async Task<TwitchTokenSet> WaitForDeviceAuthorizationAsync(
        string clientId,
        TwitchDeviceCode deviceCode,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateClientId(clientId);

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(
            deviceCode.ExpiresInSeconds);

        var interval = TimeSpan.FromSeconds(
            Math.Max(1, deviceCode.PollIntervalSeconds));

        while (DateTimeOffset.UtcNow < expiresAt)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report(
                "Warte auf Twitch-Autorisierung ...");

            await Task.Delay(interval, cancellationToken);

            using var content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["scopes"] = "",
                    ["device_code"] = deviceCode.DeviceCode,
                    ["grant_type"] =
                        "urn:ietf:params:oauth:grant-type:device_code"
                });

            using var response = await _httpClient.PostAsync(
                TwitchConstants.OAuthTokenUrl,
                content,
                cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var token = await response.Content.ReadFromJsonAsync<TokenResponse>(
                    JsonOptions,
                    cancellationToken)
                    ?? throw new InvalidOperationException(
                        "Twitch Token-Antwort war leer.");

                return ToTokenSet(token);
            }

            var error = await ReadErrorAsync(response, cancellationToken);

            if (response.StatusCode == HttpStatusCode.BadRequest &&
                string.Equals(
                    error.Message,
                    "authorization_pending",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (response.StatusCode == HttpStatusCode.BadRequest &&
                string.Equals(
                    error.Message,
                    "slow_down",
                    StringComparison.OrdinalIgnoreCase))
            {
                interval += TimeSpan.FromSeconds(2);
                continue;
            }

            throw new InvalidOperationException(
                $"Twitch-Autorisierung fehlgeschlagen: {error.Message}");
        }

        throw new TimeoutException(
            "Der Twitch-Autorisierungscode ist abgelaufen.");
    }

    public async Task<TwitchTokenSet> RefreshAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        ValidateClientId(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            });

        using var response = await _httpClient.PostAsync(
            TwitchConstants.OAuthTokenUrl,
            content,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(
            JsonOptions,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Twitch Refresh-Antwort war leer.");

        return ToTokenSet(token);
    }

    public async Task<TwitchTokenValidation> ValidateAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            TwitchConstants.OAuthValidateUrl);

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "OAuth",
                accessToken);

        using var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var validation =
            await response.Content.ReadFromJsonAsync<ValidationResponse>(
                JsonOptions,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Twitch Tokenvalidierung war leer.");

        return new TwitchTokenValidation(
            validation.ClientId,
            validation.Login,
            validation.UserId,
            validation.Scopes,
            validation.ExpiresIn);
    }

    private static void ValidateClientId(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException(
                "Twitch Client-ID fehlt. Bitte unter Einstellungen → Twitch eine gültige Client-ID eintragen.");
        }

        var value = clientId.Trim();

        if (value.Length < 20 ||
            value.Contains("your_client_id", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("placeholder", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("changeme", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Twitch Client-ID ist ungültig. Bitte unter Einstellungen → Twitch eine gültige Client-ID deiner Twitch-Developer-App eintragen.");
        }
    }

    private static TwitchTokenSet ToTokenSet(TokenResponse token)
    {
        return new TwitchTokenSet(
            token.AccessToken,
            token.RefreshToken,
            token.ExpiresIn,
            token.Scope,
            DateTimeOffset.UtcNow);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var error = await ReadErrorAsync(
            response,
            cancellationToken);

        if (string.Equals(
                error.Message,
                "invalid client",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Twitch Client-ID ist ungültig. Bitte unter Einstellungen → Twitch eine gültige Client-ID deiner Twitch-Developer-App eintragen.");
        }

        throw new InvalidOperationException(
            $"Twitch HTTP {(int)response.StatusCode}: {error.Message}");
    }

    private static async Task<ErrorResponse> ReadErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorResponse>(
                       JsonOptions,
                       cancellationToken)
                   ?? new ErrorResponse
                   {
                       Message = response.ReasonPhrase ?? "Unbekannter Fehler"
                   };
        }
        catch
        {
            return new ErrorResponse
            {
                Message = response.ReasonPhrase ?? "Unbekannter Fehler"
            };
        }
    }

    private sealed class DeviceCodeResponse
    {
        [JsonPropertyName("device_code")]
        public string DeviceCode { get; set; } = "";

        [JsonPropertyName("user_code")]
        public string UserCode { get; set; } = "";

        [JsonPropertyName("verification_uri")]
        public string VerificationUri { get; set; } = "";

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = "";

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string[] Scope { get; set; } = [];
    }

    private sealed class ValidationResponse
    {
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = "";

        [JsonPropertyName("login")]
        public string Login { get; set; } = "";

        [JsonPropertyName("user_id")]
        public string UserId { get; set; } = "";

        [JsonPropertyName("scopes")]
        public string[] Scopes { get; set; } = [];

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = "";
    }
}
