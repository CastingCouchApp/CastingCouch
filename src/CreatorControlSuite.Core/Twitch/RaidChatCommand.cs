namespace CreatorControlSuite.Core.Twitch;

/// <summary>
/// Formats the Twitch chat /raid command. Helix StartRaid is the API equivalent;
/// the chat form is kept for announcements and tooling that expect this string.
/// </summary>
public static class RaidChatCommand
{
    public static string Format(string targetLogin)
    {
        string login = NormalizeLogin(targetLogin);
        if (string.IsNullOrWhiteSpace(login))
        {
            throw new ArgumentException("Raid-Ziel darf nicht leer sein.", nameof(targetLogin));
        }

        return "/raid " + login;
    }

    public static string NormalizeLogin(string? targetLogin) =>
        (targetLogin ?? string.Empty).Trim().TrimStart('@');
}
