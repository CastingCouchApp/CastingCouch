using System.Diagnostics;
using System.Windows;
using CreatorControlSuite.App.Twitch;
using Microsoft.Web.WebView2.Core;

namespace CreatorControlSuite.App;

public partial class TwitchWebLoginWindow : Window
{
    private const string LoginUrl = "https://www.twitch.tv/login";

    public TwitchWebLoginWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            StatusText.Text = "WebView2 wird geladen…";
            await TwitchWebViewProfile.EnsureAsync(LoginWebView);
            LoginWebView.Source = new Uri(LoginUrl);
            StatusText.Text = "Bitte bei Twitch anmelden. Danach kannst du dieses Fenster schließen.";
        }
        catch (WebView2RuntimeNotFoundException)
        {
            StatusText.Text =
                "WebView2 Runtime fehlt. Installiere die Evergreen Runtime und öffne das Fenster erneut.";
            OfferRuntimeDownload();
        }
        catch (Exception ex)
        {
            StatusText.Text = "WebView2 konnte nicht gestartet werden: " + ex.Message;
        }
    }

    private void OfferRuntimeDownload()
    {
        MessageBoxResult result = MessageBox.Show(
            this,
            "Die Microsoft Edge WebView2 Runtime ist nicht installiert.\n\nInstaller jetzt im Browser öffnen?",
            "WebView2 Runtime fehlt",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = TwitchWebViewProfile.RuntimeInstallerUrl,
            UseShellExecute = true,
        });
    }
}
