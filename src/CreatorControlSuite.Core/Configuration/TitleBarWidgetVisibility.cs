namespace CreatorControlSuite.Core.Configuration;

public static class TitleBarWidgetVisibility
{
    public const string Stream = "Stream";
    public const string Quality = "Quality";
    public const string Music = "Music";
    public const string Community = "Community";
    public const string Session = "Session";
    public const string Countdown = "Countdown";
    public const string Connections = "Connections";

    public static IReadOnlyList<(string Key, string Label)> All { get; } =
    [
        (Stream, "Stream"),
        (Quality, "Qualität"),
        (Music, "Music Player"),
        (Community, "Community"),
        (Session, "Session"),
        (Countdown, "Countdown"),
        (Connections, "Verbindungen")
    ];

    public static bool IsVisible(IEnumerable<string>? hiddenWidgets, string key)
    {
        if (hiddenWidgets is null)
        {
            return true;
        }

        foreach (string hidden in hiddenWidgets)
        {
            if (string.Equals(hidden, key, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static bool ShouldShowDividerBefore(
        IEnumerable<string>? hiddenWidgets,
        string widgetKey) =>
        IsVisible(hiddenWidgets, widgetKey);

    public static void SetHidden(IList<string> hiddenWidgets, string key, bool hide)
    {
        ArgumentNullException.ThrowIfNull(hiddenWidgets);

        for (int i = hiddenWidgets.Count - 1; i >= 0; i--)
        {
            if (string.Equals(hiddenWidgets[i], key, StringComparison.OrdinalIgnoreCase))
            {
                hiddenWidgets.RemoveAt(i);
            }
        }

        if (hide)
        {
            hiddenWidgets.Add(key);
        }
    }
}
