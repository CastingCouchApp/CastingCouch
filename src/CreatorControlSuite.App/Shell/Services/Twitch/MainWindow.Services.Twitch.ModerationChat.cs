#nullable enable

using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using CreatorControlSuite.App.Twitch;

namespace CreatorControlSuite.App.Shell;

public partial class MainWindow
{
    private static void CopySelectedModerationUser(
        ListBox list,
        TextBox target)
    {
        if (list.SelectedItem is not null)
        {
            target.Text =
                list.SelectedItem.ToString()?.TrimStart('@') ??
                string.Empty;
        }
    }

    private async Task ModerateTwitchUserAsync(
        string userName,
        bool ban,
        string? durationMinutesText,
        string? reason)
    {
        string cleanName =
            (userName ?? string.Empty).Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            MessageBox.Show(
                "Bitte zuerst einen Twitch-User auswählen oder eingeben.",
                "Twitch-Moderation",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        int? durationSeconds = null;
        if (!ban)
        {
            if (!int.TryParse(durationMinutesText, out int minutes) ||
                minutes < 1)
            {
                MessageBox.Show(
                    "Bitte eine Timeout-Dauer von mindestens einer Minute " +
                    "eingeben.",
                    "Twitch-Moderation",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            durationSeconds = Math.Clamp(minutes * 60, 1, 1_209_600);
        }

        try
        {
            await _twitchModule.ModerateUserAsync(
                cleanName,
                durationSeconds,
                reason);
            string resultText = ban
                ? $"{cleanName} wurde gebannt."
                : $"{cleanName} erhielt einen Timeout von " +
                  $"{durationSeconds / 60} Minuten.";
            AddDashboardNotification(resultText, "Info");
            await AddTwitchModerationLogAsync(
                ban ? "BAN" : "TIMEOUT",
                cleanName,
                reason,
                resultText);
        }
        catch (Exception exception)
        {
            await AddTwitchModerationLogAsync(
                ban ? "BAN FEHLER" : "TIMEOUT FEHLER",
                cleanName,
                reason,
                exception.Message);
            MessageBox.Show(
                exception.Message,
                "Twitch-Moderation fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task UnbanTwitchUserAsync(string userName)
    {
        string cleanName =
            (userName ?? string.Empty).Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(cleanName))
        {
            MessageBox.Show(
                "Bitte zuerst einen Twitch-User auswählen oder eingeben.",
                "Twitch-Moderation",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            await _twitchModule.UnbanUserAsync(cleanName);
            string resultText =
                $"Ban oder Timeout für {cleanName} wurde aufgehoben.";
            AddDashboardNotification(resultText, "Info");
            await AddTwitchModerationLogAsync(
                "AUFHEBEN",
                cleanName,
                null,
                resultText);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Twitch-Moderation fehlgeschlagen",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private string GetTwitchModerationLogPath()
    {
        string folder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "CreatorControlSuite",
            "Logs");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "twitch-moderation.log");
    }

    private async Task AddTwitchModerationLogAsync(
        string action,
        string userName,
        string? reason,
        string result)
    {
        string line =
            $"{DateTimeOffset.Now:dd.MM.yyyy HH:mm:ss} · " +
            $"{action} · @{userName}" +
            (string.IsNullOrWhiteSpace(reason)
                ? string.Empty
                : $" · Grund: {reason.Trim()}") +
            $" · {result}";
        _twitchModerationLogItems.Insert(0, line);
        while (_twitchModerationLogItems.Count > 100)
        {
            _twitchModerationLogItems.RemoveAt(
                _twitchModerationLogItems.Count - 1);
        }

        await File.AppendAllTextAsync(
            GetTwitchModerationLogPath(),
            line + Environment.NewLine,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private async Task ExportTwitchModerationLogAsync()
    {
        string source = GetTwitchModerationLogPath();
        if (!File.Exists(source))
        {
            MessageBox.Show(
                "Es sind noch keine Moderationsaktionen gespeichert.",
                "Twitch-Moderation",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string target = Path.Combine(
            Path.GetDirectoryName(source)!,
            $"twitch-moderation-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        await Task.Run(() => File.Copy(source, target, overwrite: true));
        Process.Start(new ProcessStartInfo(target)
        {
            UseShellExecute = true
        });
    }

    private async Task SendTwitchChatAsync()
    {
        string message =
            SettingsPageViewHost.TwitchChatMessageBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            await _twitchModule.SendChatMessageAsync(message);
            SettingsPageViewHost.TwitchChatMessageBox.Clear();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Chatnachricht konnte nicht gesendet werden",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ScrollTwitchChatToLatest()
    {
        if (_twitchChatItems.Count == 0)
        {
            return;
        }

        TwitchChatDisplayItem latest = _twitchChatItems[^1];
        SettingsPageViewHost.TwitchChatList.ScrollIntoView(latest);
        DashboardPageViewHost.DashboardTwitchChatList
            .ScrollIntoView(latest);
        ServicesPageViewHost.TwitchServiceViewHost
            .ServicesTwitchChatList.ScrollIntoView(latest);
    }
}
