using System.Diagnostics;
using System.Windows;
using CreatorControlSuite.App.Overlay;
using CreatorControlSuite.App.Twitch;

namespace CreatorControlSuite.App.Views.Dialogs;

public partial class OverlayEditorWindow : Window
{
    private readonly string _editorUrl;
    private bool _applyingWindowSize;

    private sealed record WindowSizeOption(string Label, double Width, double Height, bool Maximized = false, bool Fixed = true)
    {
        public override string ToString() => Label;
    }

    public OverlayEditorWindow(string editorUrl, string instanceName)
    {
        InitializeComponent();
        _editorUrl = (editorUrl ?? "").Trim();
        TitleText.Text = string.IsNullOrWhiteSpace(instanceName)
            ? "Overlay Editor"
            : "Overlay Editor · " + instanceName.Trim();
        UrlText.Text = _editorUrl;
        Title = TitleText.Text;

        WindowSizeCombo.ItemsSource = new WindowSizeOption[]
        {
            new("1280 × 800 (fest)", 1280, 800),
            new("1440 × 900 (fest)", 1440, 900),
            new("1600 × 1000 (fest)", 1600, 1000),
            new("1920 × 1080 (fest)", 1920, 1080),
            new("Frei skalierbar", 1280, 800, Fixed: false),
            new("Maximiert", 1280, 800, Maximized: true, Fixed: false)
        };
        WindowSizeCombo.SelectionChanged += (_, _) => ApplySelectedWindowSize();
        // Default: frei skalierbar — vermeidet DPI/MaxWidth-Probleme beim Öffnen
        WindowSizeCombo.SelectedIndex = 4;

        CloseButton.Click += (_, _) => Close();
        ReloadButton.Click += async (_, _) => await NavigateAsync();
        OpenInBrowserButton.Click += (_, _) => OpenInBrowser();

        Loaded += async (_, _) =>
        {
            try
            {
                await NavigateAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = "Laden fehlgeschlagen: " + ex.Message;
            }
        };
    }

    private void OpenInBrowser()
    {
        if (string.IsNullOrWhiteSpace(_editorUrl))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _editorUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = "Browser konnte nicht geöffnet werden: " + ex.Message;
        }
    }

    private void ApplySelectedWindowSize()
    {
        if (_applyingWindowSize || WindowSizeCombo.SelectedItem is not WindowSizeOption option)
        {
            return;
        }

        _applyingWindowSize = true;
        try
        {
            // MaxWidth/MaxHeight bewusst nicht auf Width/Height setzen:
            // Unter DPI-Skalierung inkl. Fensterrahmen kann das Fenster sonst nicht erscheinen.
            MinWidth = 960;
            MinHeight = 600;
            MaxWidth = double.PositiveInfinity;
            MaxHeight = double.PositiveInfinity;

            if (option.Maximized)
            {
                ResizeMode = ResizeMode.CanResize;
                WindowState = WindowState.Maximized;
                StatusText.Text = "Fenster maximiert.";
                return;
            }

            WindowState = WindowState.Normal;
            ResizeMode = option.Fixed ? ResizeMode.NoResize : ResizeMode.CanResize;
            Width = option.Width;
            Height = option.Height;
            StatusText.Text = option.Fixed
                ? $"Feste Fenstergröße: {(int)option.Width} × {(int)option.Height}"
                : "Fenster frei skalierbar.";
        }
        finally
        {
            _applyingWindowSize = false;
        }
    }

    private async Task NavigateAsync()
    {
        if (string.IsNullOrWhiteSpace(_editorUrl))
        {
            StatusText.Text = "Keine Editor-URL.";
            return;
        }

        try
        {
            StatusText.Text = "WebView wird geladen…";
            await OverlayEditorWebViewProfile.EnsureAsync(EditorWebView);
            if (EditorWebView.CoreWebView2 is null)
            {
                throw new InvalidOperationException("WebView2 Core ist nicht initialisiert.");
            }

            EditorWebView.CoreWebView2.Settings.IsZoomControlEnabled = true;
            EditorWebView.CoreWebView2.Navigate(_editorUrl);
            StatusText.Text = "Editor geladen · " + _editorUrl;
        }
        catch (Exception ex)
        {
            StatusText.Text =
                "WebView2 fehlgeschlagen – öffne im Browser. " + ex.Message;
            OpenInBrowser();
        }
    }
}
