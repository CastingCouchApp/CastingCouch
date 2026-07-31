#nullable enable

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CreatorControlSuite.App.Views.Dialogs;
using CreatorControlSuite.Core.Logging;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.App.Shell;

public partial class MainWindow
{
    private async Task AuthorizeTwitchAsync()
    {
        try
        {
            await SaveSettingsAsync();
            SettingsPageViewHost.TwitchConnectionStatusText.Text =
                "Gerätecode wird angefordert ...";

            TwitchDeviceCode deviceCode =
                await _twitchModule.StartAuthorizationAsync();
            Clipboard.SetText(deviceCode.UserCode);
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = deviceCode.VerificationUri,
                    UseShellExecute = true
                });

            MessageBoxResult result = MessageBox.Show(
                "Twitch wurde im Browser geöffnet.\n\n" +
                "Code: " + deviceCode.UserCode + "\n\n" +
                "Der Code wurde in die Zwischenablage kopiert.\n" +
                "Nach der Bestätigung auf Twitch hier auf OK klicken. " +
                "Die Suite wartet danach automatisch auf den Token.",
                "Twitch autorisieren",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);
            if (result != MessageBoxResult.OK)
            {
                SettingsPageViewHost.TwitchConnectionStatusText.Text =
                    "Autorisierung abgebrochen.";
                return;
            }

            var progress = new Progress<string>(
                text =>
                    SettingsPageViewHost.TwitchConnectionStatusText.Text =
                        text);
            await _twitchModule.CompleteAuthorizationAsync(
                deviceCode,
                progress);
            RefreshTwitchUi();
        }
        catch (Exception exception)
        {
            SettingsPageViewHost.TwitchConnectionStatusText.Text =
                exception.Message;
            SettingsPageViewHost.TwitchConnectionStatusText.Foreground =
                Brushes.IndianRed;
            MessageBox.Show(
                exception.Message,
                "Twitch-Autorisierung fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ConnectTwitchAsync(
        bool showErrorDialog = true)
    {
        try
        {
            SettingsPageViewHost.TwitchConnectionStatusText.Text =
                "Twitch wird verbunden ...";
            await _twitchModule.ConnectAsync(CancellationToken.None);

            RefreshTwitchUi();
            await RefreshTwitchUsersAsync(force: true);
            await RefreshLiveViewerSampleAsync();
            await RefreshTwitchFollowerCountAsync();
            await RefreshChatEmoteCatalogFromSettingsAsync();
        }
        catch (Exception exception)
        {
            SettingsPageViewHost.TwitchConnectionStatusText.Text =
                exception.Message;
            SettingsPageViewHost.TwitchConnectionStatusText.Foreground =
                Brushes.IndianRed;
            if (showErrorDialog)
            {
                MessageBox.Show(
                    exception.Message,
                    "Twitch-Verbindung fehlgeschlagen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private async Task DisconnectTwitchAsync()
    {
        await _twitchModule.DisconnectAsync(CancellationToken.None);
        TwitchDashboardStatus.Text = "NICHT VERBUNDEN";
        SettingsPageViewHost.TwitchConnectionStatusText.Text =
            "Nicht verbunden";
        SettingsPageViewHost.TwitchConnectionStatusText.Foreground =
            Brushes.Gray;
        RefreshDashboardServiceActionButtons();
    }

    private async Task SearchTwitchCategoriesAsync(
        TextBox searchBox,
        ComboBox resultsBox)
    {
        try
        {
            string query = searchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            resultsBox.ItemsSource =
                await _twitchModule.SearchCategoriesAsync(query);
            resultsBox.IsDropDownOpen = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Kategoriesuche fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task SaveTwitchChannelAsync(
        TextBox titleBox,
        ComboBox categoryBox)
    {
        try
        {
            var category = categoryBox.SelectedItem as TwitchCategory;
            await _twitchModule.UpdateChannelAsync(
                titleBox.Text.Trim(),
                category?.Id);
            if (ReferenceEquals(
                    titleBox,
                    ServicesPageViewHost.TwitchServiceViewHost.ServicesTwitchTitleBox))
            {
                ServicesPageViewHost.TwitchServiceViewHost.MarkChannelEditorSaved();
            }
            RefreshTwitchUi();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Twitch-Kanal konnte nicht aktualisiert werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task OpenTwitchChannelEditorAsync()
    {
        TwitchConnectionSnapshot snapshot = _twitchModule.GetSnapshot();
        var editor = new TwitchChannelEditorWindow(
            snapshot.ChannelTitle,
            snapshot.CategoryName,
            _settings.Twitch.LiveNotificationText,
            query => _twitchModule.SearchCategoriesAsync(query),
            async (title, categoryId, liveNotification) =>
            {
                await _twitchModule.UpdateChannelAsync(title, categoryId);
                _settings.Twitch.LiveNotificationText = liveNotification;
                await _settingsStore.SaveAsync(_settings);
            })
        {
            Owner = this
        };

        if (editor.ShowDialog() != true)
        {
            return;
        }

        TwitchConnectionSnapshot updated = _twitchModule.GetSnapshot();
        DashboardPageViewHost.DashboardTwitchTitleBox.Text =
            updated.ChannelTitle;
        DashboardPageViewHost.DashboardTwitchCategorySearchBox.Text =
            updated.CategoryName;
        RefreshTwitchUi();
        AddDashboardNotification(
            "Twitch-Kanaldaten wurden aktualisiert.",
            "Info");
    }

    private async Task SearchTwitchCategoriesAsync()
    {
        try
        {
            string query =
                SettingsPageViewHost.TwitchCategorySearchBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                return;
            }

            SettingsPageViewHost.TwitchCategoryResultsBox.ItemsSource =
                await _twitchModule.SearchCategoriesAsync(query);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Kategoriesuche fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task SaveTwitchChannelAsync()
    {
        try
        {
            var category =
                SettingsPageViewHost.TwitchCategoryResultsBox.SelectedItem
                    as TwitchCategory;
            await _twitchModule.UpdateChannelAsync(
                SettingsPageViewHost.TwitchTitleBox.Text.Trim(),
                category?.Id);
            RefreshTwitchUi();
            MessageBox.Show(
                "Streamtitel und Kategorie wurden gespeichert.",
                "Twitch",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Twitch-Kanal konnte nicht aktualisiert werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task RunStartupStepSafelyAsync(
        string stepName,
        Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _appLogger.Write(
                AppLogLevel.Error,
                "Startup",
                $"Startschritt '{stepName}' ist fehlgeschlagen. " +
                "Die Suite wird im eingeschränkten Modus fortgesetzt.",
                exception);
        }
    }
}
