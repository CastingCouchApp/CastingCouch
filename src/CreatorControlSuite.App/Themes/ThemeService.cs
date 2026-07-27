using System.Collections.ObjectModel;
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
        ThemeDefinition theme = ThemeCatalog.Resolve(themeId);
        Application? app = Application.Current;
        if (app is null)
        {
            CurrentTheme = theme;
            return theme;
        }

        ResourceDictionary dictionary = LoadDictionary(theme);
        Collection<ResourceDictionary> merged = app.Resources.MergedDictionaries;

        for (int i = merged.Count - 1; i >= 0; i--)
        {
            ResourceDictionary candidate = merged[i];
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
        string source = dictionary.Source?.OriginalString ?? string.Empty;
        return source.Contains("/Themes/", StringComparison.OrdinalIgnoreCase)
               || source.StartsWith("Themes/", StringComparison.OrdinalIgnoreCase)
               || source.Contains("Themes\\", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyFontFamily(Application app, ResourceDictionary dictionary)
    {
        if (dictionary["AppFontFamily"] is FontFamily fontFamily)
        {
            app.MainWindow?.FontFamily = fontFamily;

            foreach (Window window in app.Windows)
            {
                window.FontFamily = fontFamily;
            }
        }
    }
}
