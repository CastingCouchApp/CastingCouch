namespace CreatorControlSuite.App.Themes;

public interface IThemeSelectionService
{
    IReadOnlyList<ThemeDefinition> Themes { get; }
    ThemeDefinition Apply(string? themeId);
}

public interface IThemeService : IThemeSelectionService
{
    string CurrentThemeId { get; }
    ThemeDefinition CurrentTheme { get; }
    event EventHandler? ThemeChanged;
    System.Windows.Media.Brush? GetBrush(string key);
}
