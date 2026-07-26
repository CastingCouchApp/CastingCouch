using System.Windows;
using System.Windows.Media;

namespace CreatorControlSuite.App.Themes;

public sealed class ThemeService : IThemeService
{
    private ResourceDictionary? _currentDictionary;

    public string CurrentThemeId => CurrentTheme.Id;
    public ThemeDefinition CurrentTheme { get; private set; } = ThemeCatalog.Classic;
    public IReadOnlyList<ThemeDefinition> Themes => ThemeCatalog.All;
    public event EventHandler? ThemeChanged;

    public ThemeDefinition Apply(string? themeId)
    {
        var theme = ThemeCatalog.Resolve(themeId);
        var app = Application.Current;
        if (app is null)
        {
            CurrentTheme = theme;
            return theme;
        }

        var dictionary = LoadDictionary(theme);
        var merged = app.Resources.MergedDictionaries;

        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var candidate = merged[i];
            if (ReferenceEquals(candidate, _currentDictionary)
                || IsThemeDictionary(candidate))
            {
                merged.RemoveAt(i);
            }
        }

        merged.Insert(0, dictionary);
        _currentDictionary = dictionary;
        CurrentTheme = theme;

        ApplyFontFamily(app, dictionary);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        return theme;
    }

    public Brush? GetBrush(string key)
    {
        if (Application.Current?.TryFindResource(key) is Brush brush)
        {
            return brush;
        }

        return null;
    }

    private static ResourceDictionary LoadDictionary(ThemeDefinition theme)
    {
        var uri = new Uri(theme.ResourcePath, UriKind.Relative);
        return new ResourceDictionary { Source = uri };
    }

    private static bool IsThemeDictionary(ResourceDictionary dictionary)
    {
        var source = dictionary.Source?.OriginalString ?? string.Empty;
        return source.Contains("/Themes/", StringComparison.OrdinalIgnoreCase)
               || source.StartsWith("Themes/", StringComparison.OrdinalIgnoreCase)
               || source.Contains("Themes\\", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyFontFamily(Application app, ResourceDictionary dictionary)
    {
        if (dictionary["AppFontFamily"] is FontFamily fontFamily)
        {
            if (app.MainWindow is not null)
            {
                app.MainWindow.FontFamily = fontFamily;
            }

            foreach (Window window in app.Windows)
            {
                window.FontFamily = fontFamily;
            }
        }
    }
}
