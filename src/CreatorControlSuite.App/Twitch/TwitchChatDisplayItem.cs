using CreatorControlSuite.Modules.Twitch;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.App.Twitch;

public sealed record TwitchChatDisplayPart(string Text, string? ImageUrl = null)
{
    public bool IsEmote => !string.IsNullOrWhiteSpace(ImageUrl);
}

public sealed record TwitchChatDisplayItem(string Prefix, IReadOnlyList<TwitchChatDisplayPart> Parts)
{
    public static TwitchChatDisplayItem FromMessage(TwitchChatMessage message, string role)
    {
        IReadOnlyList<TwitchChatDisplayPart> parts = message.Fragments.Count == 0
            ? [new TwitchChatDisplayPart(message.MessageText)]
            : [.. message.Fragments.Select(fragment => new TwitchChatDisplayPart(
                fragment.Text,
                fragment.Type == TwitchChatFragmentType.Emote && !string.IsNullOrWhiteSpace(fragment.EmoteId)
                    ? TwitchChatMessageParser.GetTwitchEmoteUrl(fragment.EmoteId)
                    : null))];

        return new TwitchChatDisplayItem(
            $"{message.ReceivedAt:HH:mm:ss} · {role}{message.ChatterName}: ",
            parts);
    }

    public override string ToString() => Prefix + string.Concat(Parts.Select(part => part.Text));
}
