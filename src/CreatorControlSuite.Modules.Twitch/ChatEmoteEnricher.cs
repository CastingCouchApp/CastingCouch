using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Modules.Twitch;

public sealed record ChatEmoteDefinition(
    string Code,
    string ImageUrl,
    string Provider);

public sealed record OverlayChatPart(
    string Type,
    string Text,
    string? Url = null,
    string? Provider = null);

public static class ChatEmoteEnricher
{
    public static IReadOnlyList<OverlayChatPart> Enrich(
        IReadOnlyList<TwitchChatFragment> fragments,
        IReadOnlyDictionary<string, ChatEmoteDefinition> catalog)
    {
        var parts = new List<OverlayChatPart>();

        foreach (TwitchChatFragment fragment in fragments)
        {
            switch (fragment.Type)
            {
                case TwitchChatFragmentType.Emote:
                    parts.Add(new OverlayChatPart(
                        "emote",
                        fragment.Text,
                        string.IsNullOrWhiteSpace(fragment.EmoteId)
                            ? null
                            : TwitchChatMessageParser.GetTwitchEmoteUrl(fragment.EmoteId),
                        "twitch"));
                    break;

                case TwitchChatFragmentType.Mention:
                case TwitchChatFragmentType.Cheermote:
                    AppendText(parts, fragment.Text);
                    break;

                default:
                    AppendTokenized(parts, fragment.Text, catalog);
                    break;
            }
        }

        return parts;
    }

    private static void AppendTokenized(
        List<OverlayChatPart> parts,
        string text,
        IReadOnlyDictionary<string, ChatEmoteDefinition> catalog)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (catalog.Count == 0)
        {
            AppendText(parts, text);
            return;
        }

        int index = 0;
        while (index < text.Length)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                int start = index;
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                {
                    index++;
                }

                AppendText(parts, text[start..index]);
                continue;
            }

            int tokenStart = index;
            while (index < text.Length && !char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            string token = text[tokenStart..index];
            if (catalog.TryGetValue(token, out ChatEmoteDefinition? emote))
            {
                parts.Add(new OverlayChatPart(
                    "emote",
                    emote.Code,
                    emote.ImageUrl,
                    emote.Provider));
            }
            else
            {
                AppendText(parts, token);
            }
        }
    }

    private static void AppendText(List<OverlayChatPart> parts, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (parts.Count > 0 &&
            parts[^1].Type == "text" &&
            parts[^1].Url is null)
        {
            parts[^1] = parts[^1] with { Text = parts[^1].Text + text };
            return;
        }

        parts.Add(new OverlayChatPart("text", text));
    }
}
