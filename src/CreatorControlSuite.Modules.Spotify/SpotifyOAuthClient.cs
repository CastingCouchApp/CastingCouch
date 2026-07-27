using System.Collections.Specialized;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CreatorControlSuite.Core.Logging;
using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.Modules.Spotify;

public sealed class SpotifyOAuthClient(HttpClient httpClient, IAppLogger logger) : ISpotifyOAuthClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient = httpClient;
    private readonly IAppLogger _logger = logger;
    private long _requestSequence;

    public async Task<SpotifyTokenSet> AuthorizeAsync(
        string clientId,
        string redirectUri,
        IReadOnlyCollection<string> scopes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(redirectUri);

        string verifier = CreateCodeVerifier();
        string challenge = CreateCodeChallenge(verifier);
        string state = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

        Uri authorizationUri = BuildAuthorizationUri(
            clientId,
            redirectUri,
            scopes,
            challenge,
            state);

        using var listener = new HttpListener();
        listener.Prefixes.Add(EnsureListenerPrefix(redirectUri));
        listener.Start();

        Process.Start(
            new ProcessStartInfo
            {
                FileName = authorizationUri.ToString(),
                UseShellExecute = true
            });

        using CancellationTokenRegistration cancellationRegistration =
            cancellationToken.Register(listener.Stop);

        HttpListenerContext context;

        try
        {
            context = await listener.GetContextAsync()
                .WaitAsync(cancellationToken);
        }
        catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        NameValueCollection query = context.Request.QueryString;
        string? returnedState = query["state"];
        string? error = query["error"];
        string? code = query["code"];

        if (!string.IsNullOrWhiteSpace(error))
        {
            await WriteBrowserResponseAsync(
                context,
                "Spotify-Autorisierung wurde abgelehnt.",
                isSuccess: false);

            throw new InvalidOperationException(
                "Spotify-Autorisierung wurde abgelehnt: " + error);
        }

        if (!string.Equals(
                state,
                returnedState,
                StringComparison.Ordinal))
        {
            await WriteBrowserResponseAsync(
                context,
                "Ungültige Spotify-Autorisierungsantwort.",
                isSuccess: false);

            throw new InvalidOperationException(
                "Der Spotify OAuth-State stimmt nicht überein.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            await WriteBrowserResponseAsync(
                context,
                "Spotify hat keinen Autorisierungscode geliefert.",
                isSuccess: false);

            throw new InvalidOperationException(
                "Spotify hat keinen Autorisierungscode geliefert.");
        }

        await WriteBrowserResponseAsync(
            context,
            "Spotify wurde verbunden. Dieses Browserfenster kann geschlossen werden.",
            isSuccess: true);

        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = verifier
            });

        using HttpResponseMessage response = await SendTokenRequestAsync(
            "authorization_code",
            content,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        TokenResponse token = await response.Content.ReadFromJsonAsync<TokenResponse>(
            JsonOptions,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Spotify Token-Antwort war leer.");

        return ToTokenSet(token);
    }

    public async Task<SpotifyTokenSet> RefreshAsync(
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken
            });

        using HttpResponseMessage response = await SendTokenRequestAsync(
            "refresh_token",
            content,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        TokenResponse token = await response.Content.ReadFromJsonAsync<TokenResponse>(
            JsonOptions,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Spotify Refresh-Antwort war leer.");

        return new SpotifyTokenSet(
            token.AccessToken,
            string.IsNullOrWhiteSpace(token.RefreshToken)
                ? refreshToken
                : token.RefreshToken,
            token.ExpiresIn,
            token.TokenType,
            token.Scope,
            DateTimeOffset.UtcNow);
    }

    internal static string CreateCodeChallenge(string verifier)
    {
        byte[] hash = SHA256.HashData(
            Encoding.ASCII.GetBytes(verifier));

        return Base64UrlEncode(hash);
    }

    private static string CreateCodeVerifier()
    {
        return Base64UrlEncode(
            RandomNumberGenerator.GetBytes(64));
    }

    private static Uri BuildAuthorizationUri(
        string clientId,
        string redirectUri,
        IReadOnlyCollection<string> scopes,
        string challenge,
        string state)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = challenge,
            ["scope"] = string.Join(' ', scopes),
            ["state"] = state,
            ["show_dialog"] = "true"
        };

        string query = string.Join(
            "&",
            parameters.Select(pair =>
                Uri.EscapeDataString(pair.Key) +
                "=" +
                Uri.EscapeDataString(pair.Value)));

        return new Uri(
            SpotifyConstants.AuthorizeUrl + "?" + query);
    }

    private static string EnsureListenerPrefix(string redirectUri)
    {
        var uri = new Uri(redirectUri);

        if (!string.Equals(
                uri.Host,
                "127.0.0.1",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Die Spotify Redirect-URI muss 127.0.0.1 verwenden.");
        }

        string path = uri.AbsolutePath.EndsWith(
            "/",
            StringComparison.Ordinal)
            ? uri.AbsolutePath
            : uri.AbsolutePath + "/";

        return $"{uri.Scheme}://{uri.Host}:{uri.Port}{path}";
    }

    private static async Task WriteBrowserResponseAsync(
        HttpListenerContext context,
        string message,
        bool isSuccess)
    {
        string color = isSuccess
            ? "#5CE06E"
            : "#E05C5C";

        string html =
            "<!doctype html><html><head><meta charset=\"utf-8\">" +
            "<title>Creator Control Suite</title></head>" +
            "<body style=\"font-family:Segoe UI;background:#101010;" +
            "color:white;display:grid;place-items:center;height:100vh\">" +
            "<div style=\"max-width:650px;padding:32px;border:1px solid #444;" +
            "border-radius:12px;background:#181818\">" +
            "<h1 style=\"color:" + color + "\">Creator Control Suite</h1>" +
            "<p style=\"font-size:18px\">" +
            WebUtility.HtmlEncode(message) +
            "</p></div></body></html>";

        byte[] bytes = Encoding.UTF8.GetBytes(html);

        context.Response.StatusCode = 200;
        context.Response.ContentType =
            "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;

        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }

    private async Task<HttpResponseMessage> SendTokenRequestAsync(
        string operation,
        HttpContent content,
        CancellationToken cancellationToken)
    {
        long requestNumber = Interlocked.Increment(ref _requestSequence);
        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;

        try
        {
            response = await _httpClient.PostAsync(
                SpotifyConstants.TokenUrl,
                content,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();
            _logger.Write(
                AppLogLevel.Error,
                "Spotify.OAuth",
                $"Spotify-OAuth-Anfrage #{requestNumber} ({operation}) ist vor einer HTTP-Antwort fehlgeschlagen.",
                exception,
                new Dictionary<string, string>
                {
                    ["requestNumber"] = requestNumber.ToString(),
                    ["operation"] = operation,
                    ["endpoint"] = "/api/token",
                    ["statusCode"] = "none",
                    ["durationMs"] = stopwatch.ElapsedMilliseconds.ToString(),
                    ["retryAfterSeconds"] = "none"
                });
            throw;
        }

        stopwatch.Stop();
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
        AppLogLevel level = response.StatusCode == HttpStatusCode.TooManyRequests
            ? AppLogLevel.Warning
            : response.IsSuccessStatusCode
                ? AppLogLevel.Information
                : AppLogLevel.Error;

        _logger.Write(
            level,
            "Spotify.OAuth",
            $"Spotify-OAuth-Anfrage #{requestNumber} ({operation}) -> {(int)response.StatusCode} {response.ReasonPhrase} in {stopwatch.ElapsedMilliseconds} ms.",
            properties: new Dictionary<string, string>
            {
                ["requestNumber"] = requestNumber.ToString(),
                ["operation"] = operation,
                ["endpoint"] = "/api/token",
                ["statusCode"] = ((int)response.StatusCode).ToString(),
                ["durationMs"] = stopwatch.ElapsedMilliseconds.ToString(),
                ["retryAfterSeconds"] = retryAfter is null
                    ? "none"
                    : Math.Max(0, (int)Math.Ceiling(retryAfter.Value.TotalSeconds)).ToString()
            });

        return response;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string text = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            TimeSpan retryAfter = response.Headers.RetryAfter?.Delta
                ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)
                ?? TimeSpan.FromSeconds(5);

            throw new SpotifyRateLimitException(
                retryAfter > TimeSpan.Zero ? retryAfter : TimeSpan.FromSeconds(5),
                text);
        }

        throw new InvalidOperationException(
            $"Spotify HTTP {(int)response.StatusCode}: {text}");
    }

    private static SpotifyTokenSet ToTokenSet(
        TokenResponse token)
    {
        return new SpotifyTokenSet(
            token.AccessToken,
            token.RefreshToken,
            token.ExpiresIn,
            token.TokenType,
            token.Scope,
            DateTimeOffset.UtcNow);
    }

    private static string Base64UrlEncode(byte[] value)
    {
        return Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed class SpotifyScopeConverter : JsonConverter<string[]>
    {
        public override string[] Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return [];
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                string? value = reader.GetString();

                return string.IsNullOrWhiteSpace(value)
                    ? []
                    : value.Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries |
                        StringSplitOptions.TrimEntries);
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                var values = new List<string>();

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndArray)
                    {
                        return [.. values];
                    }

                    if (reader.TokenType == JsonTokenType.String)
                    {
                        string? value = reader.GetString();

                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            values.Add(value);
                        }
                    }
                }
            }

            throw new JsonException(
                $"Spotify scope erwartet String, Array oder null; erhalten: {reader.TokenType}.");
        }

        public override void Write(
            Utf8JsonWriter writer,
            string[] value,
            JsonSerializerOptions options)
        {
            writer.WriteStringValue(
                string.Join(
                    ' ',
                    value ?? []));
        }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = "";

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "Bearer";

        [JsonPropertyName("scope")]
        [JsonConverter(typeof(SpotifyScopeConverter))]
        public string[] Scope { get; set; } = [];
    }
}
