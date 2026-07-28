#nullable enable
using System.Windows;
using System.Windows.Controls;
using CreatorControlSuite.App.Services;
using CreatorControlSuite.App.Twitch;
using CreatorControlSuite.App.Views.Dialogs;
using CreatorControlSuite.Core.Configuration;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CreatorControlSuite.App.Shell;

public partial class MainWindow
{
    private void OpenDashboardTwitchChat()
    {
        string? channel = ResolveTwitchChatChannel();
        if (string.IsNullOrWhiteSpace(channel))
        {
            AddDashboardNotification(
                "Kein Twitch-Kanal für den Chat konfiguriert.",
                "Warnung");
            return;
        }

        OpenConfiguredTarget(
            TwitchWebViewProfile.BuildPopoutChatUrl(channel),
            "Twitch Chat");
    }

    private string? ResolveTwitchChatChannel()
        => TwitchDashboardApplicationService.ResolveChatChannel(
            _twitchModule.GetSnapshot(),
            _settings.Twitch.ChannelName);

    private async Task OnTwitchChatUiModeChangedAsync()
    {
        if (_loadingSettingsIntoUi)
        {
            return;
        }

        _settings.Twitch.ChatUiMode =
            SettingsPageViewHost.TwitchChatUiEmbeddedWebRadio.IsChecked == true
                ? TwitchChatUiMode.EmbeddedWeb
                : TwitchChatUiMode.BuiltIn;
        await ApplyTwitchChatUiModeAsync();
    }

    private void OpenTwitchWebLoginWindow()
    {
        var window = new TwitchWebLoginWindow
        {
            Owner = this,
        };
        window.ShowDialog();
        _ = RefreshTwitchWebChatViewsAsync(forceReload: true);
    }

    private async Task ApplyTwitchChatUiModeAsync()
    {
        bool web =
            _settings.Twitch.ChatUiMode == TwitchChatUiMode.EmbeddedWeb;

        DashboardPageViewHost.DashboardTwitchChatList.Visibility =
            web ? Visibility.Collapsed : Visibility.Visible;
        DashboardPageViewHost.DashboardTwitchWebChat.Visibility =
            web ? Visibility.Visible : Visibility.Collapsed;
        DashboardPageViewHost.DashboardTwitchChatHeader.Visibility =
            web ? Visibility.Collapsed : Visibility.Visible;
        DashboardPageViewHost.DashboardTwitchChatControls.Visibility =
            web ? Visibility.Collapsed : Visibility.Visible;
        DashboardPageViewHost.DashboardTwitchChatHeaderRow.Height =
            web ? new GridLength(0) : GridLength.Auto;
        DashboardPageViewHost.DashboardTwitchChatControlsRow.Height =
            web ? new GridLength(0) : GridLength.Auto;
        DashboardPageViewHost.DashboardTwitchChatContentHost.Margin =
            web ? new Thickness(0) : new Thickness(0, 8, 0, 8);
        DashboardPageViewHost.DashboardTwitchChatModule.Padding =
            web ? new Thickness(0) : new Thickness(10);

        ServicesPageViewHost.TwitchServiceViewHost
            .ServicesTwitchChatList.Visibility =
            web ? Visibility.Collapsed : Visibility.Visible;
        ServicesPageViewHost.TwitchServiceViewHost
            .ServicesTwitchWebChat.Visibility =
            web ? Visibility.Visible : Visibility.Collapsed;

        SettingsPageViewHost.TwitchBuiltInChatPanel.Visibility =
            web ? Visibility.Collapsed : Visibility.Visible;
        SettingsPageViewHost.TwitchWebChatSettingsHint.Visibility =
            web ? Visibility.Visible : Visibility.Collapsed;

        if (!web)
        {
            return;
        }

        await RefreshTwitchWebChatViewsAsync(forceReload: false);
    }

    private async Task RefreshTwitchWebChatViewsAsync(bool forceReload)
    {
        if (_settings.Twitch.ChatUiMode != TwitchChatUiMode.EmbeddedWeb)
        {
            return;
        }

        string? channel = ResolveTwitchChatChannel();
        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        string url = TwitchWebViewProfile.BuildPopoutChatUrl(channel);
        if (!forceReload &&
            string.Equals(
                _lastTwitchWebChatUrl,
                url,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await EnsureAndNavigateTwitchWebChatAsync(
                DashboardPageViewHost.DashboardTwitchWebChat,
                url,
                forceReload);
            await EnsureAndNavigateTwitchWebChatAsync(
                ServicesPageViewHost.TwitchServiceViewHost
                    .ServicesTwitchWebChat,
                url,
                forceReload);
            _lastTwitchWebChatUrl = url;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            AddDashboardNotification(
                "WebView2 Runtime fehlt. Bitte Evergreen Runtime installieren " +
                "oder den Systembrowser nutzen.",
                "Warnung");
            MessageBoxResult result = MessageBox.Show(
                this,
                "Die Microsoft Edge WebView2 Runtime ist nicht installiert." +
                "\n\nInstaller jetzt im Browser öffnen?",
                "WebView2 Runtime fehlt",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                OpenConfiguredTarget(
                    TwitchWebViewProfile.RuntimeInstallerUrl,
                    "WebView2 Runtime");
            }
        }
        catch (Exception ex)
        {
            AddDashboardNotification(
                "Web-Chat konnte nicht geladen werden: " + ex.Message,
                "Warnung");
        }
    }

    private static async Task EnsureAndNavigateTwitchWebChatAsync(
        WebView2 webView,
        string url,
        bool forceReload = false)
    {
        await TwitchWebViewProfile.EnsureAsync(webView);
        if (!forceReload &&
            webView.Source?.AbsoluteUri is string current &&
            string.Equals(
                current,
                url,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (forceReload && webView.CoreWebView2 is not null)
        {
            webView.CoreWebView2.Navigate(url);
            return;
        }

        webView.Source = new Uri(url);
    }
}
