using System.Text.Json;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Modules.Twitch;

public interface IChatEmoteCatalog
{
    IReadOnlyDictionary<string, ChatEmoteDefinition> GetActiveMap(OverlayChatSettings settings);

    Task RefreshAsync(
        string broadcasterUserId,
        OverlayChatSettings settings,
        CancellationToken cancellationToken = default);
}

public sealed class ChatEmoteCatalog(HttpClient httpClient) : IChatEmoteCatalog
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly object _gate = new();

    private Dictionary<string, ChatEmoteDefinition> _bttv = new(StringComparer.Ordinal);
    private Dictionary<string, ChatEmoteDefinition> _ffz = new(StringComparer.Ordinal);
    private Dictionary<string, ChatEmoteDefinition> _sevenTv = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ChatEmoteDefinition> GetActiveMap(OverlayChatSettings settings)
    {
        var map = new Dictionary<string, ChatEmoteDefinition>(StringComparer.Ordinal);
        lock (_gate)
        {
            if (settings.EnableBttv)
            {
                Merge(map, _bttv);
            }

            if (settings.EnableFfz)
            {
                Merge(map, _ffz);
            }

            if (settings.EnableSevenTv)
            {
                Merge(map, _sevenTv);
            }
        }

        return map;
    }

    public async Task RefreshAsync(
        string broadcasterUserId,
        OverlayChatSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(broadcasterUserId))
        {
            return;
        }

        Dictionary<string, ChatEmoteDefinition> bttv = settings.EnableBttv
            ? await LoadBttvAsync(broadcasterUserId, cancellationToken)
            : new(StringComparer.Ordinal);
        Dictionary<string, ChatEmoteDefinition> ffz = settings.EnableFfz
            ? await LoadFfzAsync(broadcasterUserId, cancellationToken)
            : new(StringComparer.Ordinal);
        Dictionary<string, ChatEmoteDefinition> sevenTv = settings.EnableSevenTv
            ? await LoadSevenTvAsync(broadcasterUserId, cancellationToken)
            : new(StringComparer.Ordinal);

        lock (_gate)
        {
            _bttv = bttv;
            _ffz = ffz;
            _sevenTv = sevenTv;
        }
    }

    private async Task<Dictionary<string, ChatEmoteDefinition>> LoadBttvAsync(
        string broadcasterUserId,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, ChatEmoteDefinition>(StringComparer.Ordinal);
        try
        {
            await AddBttvSetAsync(
                map,
                "https://api.betterttv.net/3/cached/emotes/global",
                cancellationToken);

            using HttpResponseMessage response = await _httpClient.GetAsync(
                $"https://api.betterttv.net/3/cached/users/twitch/{Uri.EscapeDataString(broadcasterUserId)}",
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return map;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("channelEmotes", out JsonElement channelEmotes))
            {
                AddBttvEmotes(map, channelEmotes);
            }

            if (root.TryGetProperty("sharedEmotes", out JsonElement sharedEmotes))
            {
                AddBttvEmotes(map, sharedEmotes);
            }
        }
        catch
        {
            // Third-party catalogs are best-effort.
        }

        return map;
    }

    private async Task AddBttvSetAsync(
        Dictionary<string, ChatEmoteDefinition> map,
        string url,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        AddBttvEmotes(map, doc.RootElement);
    }

    private static void AddBttvEmotes(
        Dictionary<string, ChatEmoteDefinition> map,
        JsonElement emotes)
    {
        if (emotes.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement emote in emotes.EnumerateArray())
        {
            string id = emote.TryGetProperty("id", out JsonElement idElement)
                ? idElement.GetString() ?? ""
                : "";
            string code = emote.TryGetProperty("code", out JsonElement codeElement)
                ? codeElement.GetString() ?? ""
                : "";
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            map[code] = new ChatEmoteDefinition(
                code,
                $"https://cdn.betterttv.net/emote/{id}/2x.webp",
                "bttv");
        }
    }

    private async Task<Dictionary<string, ChatEmoteDefinition>> LoadFfzAsync(
        string broadcasterUserId,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, ChatEmoteDefinition>(StringComparer.Ordinal);
        try
        {
            using HttpResponseMessage globalResponse = await _httpClient.GetAsync(
                "https://api.frankerfacez.com/v1/set/global",
                cancellationToken);
            if (globalResponse.IsSuccessStatusCode)
            {
                await using Stream stream = await globalResponse.Content.ReadAsStreamAsync(cancellationToken);
                using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                AddFfzSets(map, doc.RootElement);
            }

            using HttpResponseMessage roomResponse = await _httpClient.GetAsync(
                $"https://api.frankerfacez.com/v1/room/id/{Uri.EscapeDataString(broadcasterUserId)}",
                cancellationToken);
            if (roomResponse.IsSuccessStatusCode)
            {
                await using Stream stream = await roomResponse.Content.ReadAsStreamAsync(cancellationToken);
                using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                AddFfzSets(map, doc.RootElement);
            }
        }
        catch
        {
            // best-effort
        }

        return map;
    }

    private static void AddFfzSets(
        Dictionary<string, ChatEmoteDefinition> map,
        JsonElement root)
    {
        if (!root.TryGetProperty("sets", out JsonElement sets) ||
            sets.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (JsonProperty set in sets.EnumerateObject())
        {
            if (!set.Value.TryGetProperty("emoticons", out JsonElement emoticons) ||
                emoticons.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement emote in emoticons.EnumerateArray())
            {
                string code = emote.TryGetProperty("name", out JsonElement nameElement)
                    ? nameElement.GetString() ?? ""
                    : "";
                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                string? url = null;
                if (emote.TryGetProperty("urls", out JsonElement urls) &&
                    urls.ValueKind == JsonValueKind.Object)
                {
                    if (urls.TryGetProperty("2", out JsonElement two))
                    {
                        url = NormalizeFfzUrl(two.GetString());
                    }
                    else if (urls.TryGetProperty("1", out JsonElement one))
                    {
                        url = NormalizeFfzUrl(one.GetString());
                    }
                }

                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                map[code] = new ChatEmoteDefinition(code, url, "ffz");
            }
        }
    }

    private static string? NormalizeFfzUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return url.StartsWith("//", StringComparison.Ordinal)
            ? "https:" + url
            : url;
    }

    private async Task<Dictionary<string, ChatEmoteDefinition>> LoadSevenTvAsync(
        string broadcasterUserId,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, ChatEmoteDefinition>(StringComparer.Ordinal);
        try
        {
            await AddSevenTvEmoteSetAsync(
                map,
                "https://7tv.io/v3/emote-sets/global",
                cancellationToken);

            using HttpResponseMessage userResponse = await _httpClient.GetAsync(
                $"https://7tv.io/v3/users/twitch/{Uri.EscapeDataString(broadcasterUserId)}",
                cancellationToken);
            if (!userResponse.IsSuccessStatusCode)
            {
                return map;
            }

            await using Stream stream = await userResponse.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (doc.RootElement.TryGetProperty("emote_set", out JsonElement emoteSet))
            {
                AddSevenTvEmotes(map, emoteSet);
            }
        }
        catch
        {
            // best-effort
        }

        return map;
    }

    private async Task AddSevenTvEmoteSetAsync(
        Dictionary<string, ChatEmoteDefinition> map,
        string url,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return;
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        AddSevenTvEmotes(map, doc.RootElement);
    }

    private static void AddSevenTvEmotes(
        Dictionary<string, ChatEmoteDefinition> map,
        JsonElement emoteSet)
    {
        if (!emoteSet.TryGetProperty("emotes", out JsonElement emotes) ||
            emotes.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement entry in emotes.EnumerateArray())
        {
            string code = entry.TryGetProperty("name", out JsonElement nameElement)
                ? nameElement.GetString() ?? ""
                : "";
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }

            string? hostUrl = null;
            string? fileName = null;
            if (entry.TryGetProperty("data", out JsonElement data) &&
                data.TryGetProperty("host", out JsonElement host))
            {
                string baseUrl = host.TryGetProperty("url", out JsonElement urlElement)
                    ? urlElement.GetString() ?? ""
                    : "";
                if (host.TryGetProperty("files", out JsonElement files) &&
                    files.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement file in files.EnumerateArray())
                    {
                        string name = file.TryGetProperty("name", out JsonElement fileNameElement)
                            ? fileNameElement.GetString() ?? ""
                            : "";
                        if (name.Contains("2x", StringComparison.OrdinalIgnoreCase) &&
                            name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                        {
                            fileName = name;
                            break;
                        }

                        fileName ??= name;
                    }
                }

                if (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(fileName))
                {
                    hostUrl = baseUrl.StartsWith("//", StringComparison.Ordinal)
                        ? "https:" + baseUrl.TrimEnd('/') + "/" + fileName
                        : baseUrl.TrimEnd('/') + "/" + fileName;
                }
            }

            if (string.IsNullOrWhiteSpace(hostUrl))
            {
                continue;
            }

            map[code] = new ChatEmoteDefinition(code, hostUrl, "7tv");
        }
    }

    private static void Merge(
        Dictionary<string, ChatEmoteDefinition> target,
        Dictionary<string, ChatEmoteDefinition> source)
    {
        foreach (KeyValuePair<string, ChatEmoteDefinition> entry in source)
        {
            target[entry.Key] = entry.Value;
        }
    }
}
