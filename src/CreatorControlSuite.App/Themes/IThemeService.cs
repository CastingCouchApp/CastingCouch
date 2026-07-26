namespace CreatorControlSuite.App.Themes;

public interface IThemeService
{
    string CurrentThemeId { get; }
    ThemeDefinition CurrentTheme { get; }
    IReadOnlyList<ThemeDefinition> Themes { get; }
    event EventHandler? ThemeChanged;
    ThemeDefinition Apply(string? themeId);
    System.Windows.Media.Brush? GetBrush(string key);
}
