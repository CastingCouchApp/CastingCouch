using System.Text.Json;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Modules.Twitch;

public static class TwitchChatMessageParser
{
    public static string GetTwitchEmoteUrl(string emoteId) =>
        $"https://static-cdn.jtvnw.net/emoticons/v2/{emoteId}/default/dark/2.0";

    public static TwitchChatMessage Parse(
        JsonElement eventData,
        DateTimeOffset receivedAt)
    {
        JsonElement messageElement = eventData.GetProperty("message");
        string text = messageElement.TryGetProperty("text", out JsonElement textElement)
            ? textElement.GetString() ?? ""
            : "";

        TwitchChatBadge[] badges =
            eventData.TryGetProperty("badges", out JsonElement badgesElement) &&
            badgesElement.ValueKind == JsonValueKind.Array
                ? [.. badgesElement
                    .EnumerateArray()
                    .Select(badge => new TwitchChatBadge(
                        GetString(badge, "set_id"),
                        GetString(badge, "id"),
                        GetString(badge, "info")))
                    .Where(badge => !string.IsNullOrWhiteSpace(badge.SetId))]
                : [];

        IReadOnlyList<TwitchChatFragment> fragments = ParseFragments(messageElement, text);

        return new TwitchChatMessage(
            GetString(eventData, "message_id"),
            GetString(eventData, "broadcaster_user_id"),
            GetString(eventData, "chatter_user_id"),
            GetString(eventData, "chatter_user_login"),
            GetString(eventData, "chatter_user_name"),
            text,
            GetString(eventData, "color"),
            receivedAt,
            badges,
            fragments);
    }

    private static IReadOnlyList<TwitchChatFragment> ParseFragments(
        JsonElement messageElement,
        string fallbackText)
    {
        if (!messageElement.TryGetProperty("fragments", out JsonElement fragmentsElement) ||
            fragmentsElement.ValueKind != JsonValueKind.Array)
        {
            return string.IsNullOrEmpty(fallbackText)
                ? []
                : [new TwitchChatFragment(TwitchChatFragmentType.Text, fallbackText)];
        }

        var fragments = new List<TwitchChatFragment>();
        foreach (JsonElement fragment in fragmentsElement.EnumerateArray())
        {
            string type = GetString(fragment, "type");
            string fragmentText = GetString(fragment, "text");
            if (string.IsNullOrEmpty(fragmentText) &&
                !string.Equals(type, "emote", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(type, "emote", StringComparison.OrdinalIgnoreCase))
            {
                string? emoteId = null;
                if (fragment.TryGetProperty("emote", out JsonElement emoteElement))
                {
                    emoteId = GetString(emoteElement, "id");
                    if (string.IsNullOrWhiteSpace(emoteId))
                    {
                        emoteId = null;
                    }
                }

                fragments.Add(new TwitchChatFragment(
                    TwitchChatFragmentType.Emote,
                    fragmentText,
                    emoteId));
                continue;
            }

            TwitchChatFragmentType fragmentType = type.ToLowerInvariant() switch
            {
                "mention" => TwitchChatFragmentType.Mention,
                "cheermote" => TwitchChatFragmentType.Cheermote,
                _ => TwitchChatFragmentType.Text
            };

            fragments.Add(new TwitchChatFragment(fragmentType, fragmentText));
        }

        if (fragments.Count == 0 && !string.IsNullOrEmpty(fallbackText))
        {
            fragments.Add(new TwitchChatFragment(TwitchChatFragmentType.Text, fallbackText));
        }

        return fragments;
    }

    private static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement property)
            ? property.GetString() ?? ""
            : "";
}
