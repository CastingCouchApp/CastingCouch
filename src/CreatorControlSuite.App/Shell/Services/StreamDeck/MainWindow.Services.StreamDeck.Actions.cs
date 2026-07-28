#nullable enable

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;
using CreatorControlSuite.Modules.StreamDeck.Models;

namespace CreatorControlSuite.App.Shell;

public partial class MainWindow
{
    private async Task CreateStreamDeckActionAsync()
    {
        try
        {
            string title = string.IsNullOrWhiteSpace(
                ServicesPageViewHost.StreamDeckServiceViewHost
                    .StreamDeckActionTitleBox.Text)
                ? "Neue Aktion"
                : ServicesPageViewHost.StreamDeckServiceViewHost
                    .StreamDeckActionTitleBox.Text.Trim();
            var item = ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckActionCommandBox.SelectedItem as ComboBoxItem;
            string command =
                item?.Tag?.ToString() ?? "workflow.prepare";
            string parameter = ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckActionParameterBox.Text.Trim();
            string profile = string.IsNullOrWhiteSpace(
                ServicesPageViewHost.StreamDeckServiceViewHost
                    .StreamDeckProfileNameBox.Text)
                ? "Standard"
                : ServicesPageViewHost.StreamDeckServiceViewHost
                    .StreamDeckProfileNameBox.Text.Trim();
            string page = string.IsNullOrWhiteSpace(
                ServicesPageViewHost.StreamDeckServiceViewHost
                    .StreamDeckPageNameBox.Text)
                ? "Hauptseite"
                : ServicesPageViewHost.StreamDeckServiceViewHost
                    .StreamDeckPageNameBox.Text.Trim();
            string condition =
                (ServicesPageViewHost.StreamDeckServiceViewHost
                    .StreamDeckStateConditionBox.SelectedItem
                    as ComboBoxItem)?.Tag?.ToString() ??
                string.Empty;
            string trueLabel =
                ServicesPageViewHost.StreamDeckServiceViewHost
                    .StreamDeckTrueLabelBox.Text.Trim();
            string falseLabel =
                ServicesPageViewHost.StreamDeckServiceViewHost
                    .StreamDeckFalseLabelBox.Text.Trim();
            bool toggleMode =
                ServicesPageViewHost.StreamDeckServiceViewHost
                    .StreamDeckToggleModeBox.IsChecked == true;
            string alternateCommand =
                (ServicesPageViewHost.StreamDeckServiceViewHost
                    .StreamDeckAlternateCommandBox.SelectedItem
                    as ComboBoxItem)?.Tag?.ToString() ??
                string.Empty;
            string alternateParameter =
                ServicesPageViewHost.StreamDeckServiceViewHost
                    .StreamDeckAlternateParameterBox.Text.Trim();

            if (!int.TryParse(
                    ServicesPageViewHost.StreamDeckServiceViewHost
                        .StreamDeckStepDelayBox.Text,
                    out int stepDelayMs) ||
                stepDelayMs is < 0 or > 10000)
            {
                throw new InvalidOperationException(
                    "Die Schrittverzögerung muss zwischen 0 und " +
                    "10000 ms liegen.");
            }

            if (!int.TryParse(
                    ServicesPageViewHost.StreamDeckServiceViewHost
                        .StreamDeckRetryCountBox.Text,
                    out int retryCount) ||
                retryCount is < 0 or > 5)
            {
                throw new InvalidOperationException(
                    "Die Wiederholungszahl muss zwischen 0 und 5 liegen.");
            }

            if (!int.TryParse(
                    ServicesPageViewHost.StreamDeckServiceViewHost
                        .StreamDeckCooldownBox.Text,
                    out int cooldownMs) ||
                cooldownMs is < 0 or > 60000)
            {
                throw new InvalidOperationException(
                    "Die Tastensperre muss zwischen 0 und 60000 ms liegen.");
            }

            if (toggleMode && string.IsNullOrWhiteSpace(condition))
            {
                throw new InvalidOperationException(
                    "Für eine Toggle-Taste muss eine Zustandsbindung " +
                    "ausgewählt werden.");
            }

            if (!int.TryParse(
                    ServicesPageViewHost.StreamDeckServiceViewHost
                        .StreamDeckSlotBox.Text,
                    out int slot) ||
                slot is < 1 or > 32)
            {
                throw new InvalidOperationException(
                    "Die Position muss zwischen 1 und 32 liegen.");
            }

            var steps = new List<(string Command, string Parameter)>();
            foreach (string line in ServicesPageViewHost
                         .StreamDeckServiceViewHost.StreamDeckMultiActionBox
                         .Text.Split(
                             ['\r', '\n'],
                             StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split('|', 2);
                string stepCommand = parts[0].Trim();
                if (string.IsNullOrWhiteSpace(stepCommand))
                {
                    continue;
                }

                steps.Add((
                    stepCommand,
                    parts.Length > 1
                        ? parts[1].Trim()
                        : string.Empty));
            }

            if (steps.Count == 0)
            {
                steps.Add((command, parameter));
            }

            if (steps.Count > 20)
            {
                throw new InvalidOperationException(
                    "Eine Mehrfachaktion darf höchstens 20 Schritte " +
                    "enthalten.");
            }

            string safeName = string.Concat(title.Select(
                character =>
                    Path.GetInvalidFileNameChars().Contains(character)
                        ? '_'
                        : character)).Trim();
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "Neue Aktion";
            }

            Directory.CreateDirectory(StreamDeckActionsDirectory);
            string clientPath = Path.Combine(
                AppContext.BaseDirectory,
                "CreatorControlSuite.CommandClient.exe");
            string cmdPath = Path.Combine(
                StreamDeckActionsDirectory,
                safeName + ".cmd");
            var content = new StringBuilder("@echo off\r\n");
            if (toggleMode)
            {
                string stateExpression = condition switch
                {
                    "stream.live" => "$s.stream.isLive",
                    "obs.connected" => "$s.obs.connected",
                    "spotify.playing" => "$s.spotify.isPlaying",
                    _ => "$false"
                };
                content.AppendLine(
                    $"powershell -NoProfile -ExecutionPolicy Bypass " +
                    $"-Command \"$s=Get-Content -Raw " +
                    $"'{StreamDeckRuntimeStateFile.Replace("'", "''")}'|" +
                    $"ConvertFrom-Json; if({stateExpression})" +
                    "{exit 0}else{exit 1}\"");
                content.AppendLine("if errorlevel 1 goto stateoff");
                string alternateArgs =
                    string.IsNullOrWhiteSpace(alternateParameter)
                        ? alternateCommand
                        : FormatStreamDeckCommandArgs(
                            alternateCommand,
                            alternateParameter);
                content.AppendLine(
                    $"start \"\" /wait /min \"{clientPath}\" " +
                    $"{alternateArgs}");
                content.AppendLine("goto end");
                content.AppendLine(":stateoff");
            }

            int stepNumber = 0;
            foreach ((string Command, string Parameter) in steps)
            {
                stepNumber++;
                string args = FormatStreamDeckCommandArgs(
                    Command,
                    Parameter);
                string successLabel = $"step_{stepNumber}_ok";
                for (int attempt = 0; attempt <= retryCount; attempt++)
                {
                    content.AppendLine(
                        $"start \"\" /wait /min \"{clientPath}\" {args}");
                    content.AppendLine(
                        $"if not errorlevel 1 goto {successLabel}");
                }

                content.AppendLine($":{successLabel}");
                if (stepDelayMs > 0)
                {
                    content.AppendLine(
                        "powershell -NoProfile -Command " +
                        $"\"Start-Sleep -Milliseconds {stepDelayMs}\"");
                }
            }

            if (toggleMode)
            {
                content.AppendLine(":end");
            }

            if (cooldownMs > 0)
            {
                content.AppendLine(
                    "powershell -NoProfile -Command " +
                    $"\"Start-Sleep -Milliseconds {cooldownMs}\"");
            }

            await File.WriteAllTextAsync(cmdPath, content.ToString());
            var metadata = new
            {
                title,
                command = steps[0].Command,
                parameter = steps[0].Parameter,
                profile,
                page,
                slot,
                steps = steps.Select(step => new
                {
                    command = step.Command,
                    parameter = step.Parameter
                }).ToArray(),
                locked = false,
                condition,
                trueLabel,
                falseLabel,
                toggleMode,
                alternateCommand,
                alternateParameter,
                stepDelayMs,
                retryCount,
                cooldownMs,
                createdAt = DateTimeOffset.Now
            };
            await File.WriteAllTextAsync(
                Path.ChangeExtension(cmdPath, ".json"),
                JsonSerializer.Serialize(
                    metadata,
                    new JsonSerializerOptions { WriteIndented = true }));
            ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckActionCreateStatusText.Text =
                $"Aktionstaste erstellt: {cmdPath}";
            ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckActionCreateStatusText.Foreground =
                new SolidColorBrush(Color.FromRgb(92, 184, 92));
            RefreshStreamDeckActionsList();
        }
        catch (Exception exception)
        {
            ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckActionCreateStatusText.Text =
                exception.Message;
            ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckActionCreateStatusText.Foreground =
                new SolidColorBrush(Color.FromRgb(220, 90, 90));
        }
    }

    private static string FormatStreamDeckCommandArgs(
        string command,
        string parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter))
        {
            return command;
        }

        string key = command switch
        {
            "spotify.volume" => "volume",
            "spotify.playlist" => "uri",
            "obs.scene" => "scene",
            "obs.mute" => "input",
            "alert.test" or "alerts.test" => "type",
            _ => "value"
        };

        return $"{command} {key}=" +
               $"\"{parameter.Replace("\"", "\"\"")}\"";
    }

    private void OpenStreamDeckActionsFolder()
    {
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        Process.Start(new ProcessStartInfo(
            "explorer.exe",
            StreamDeckActionsDirectory)
        {
            UseShellExecute = true
        });
    }

    private void DeleteSelectedStreamDeckAction()
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckCreatedActionsList.SelectedItem
            is not ListBoxItem item ||
            item.Tag is not string file)
        {
            return;
        }

        if (File.Exists(file))
        {
            File.Delete(file);
        }

        string json = Path.ChangeExtension(file, ".json");
        if (File.Exists(json))
        {
            File.Delete(json);
        }

        RefreshStreamDeckActionsList();
    }

    private async Task ExportStreamDeckProfileAsync()
    {
        try
        {
            StreamDeckProfilePackage package =
                await _streamDeckModule.BuildDefaultProfileAsync();

            SettingsPageViewHost.StreamDeckStatusText.Text =
                "Profil exportiert: " + package.Path;
            SettingsPageViewHost.StreamDeckStatusText.Foreground =
                Brushes.LightGreen;
        }
        catch (Exception exception)
        {
            SettingsPageViewHost.StreamDeckStatusText.Text =
                exception.Message;
            SettingsPageViewHost.StreamDeckStatusText.Foreground =
                Brushes.IndianRed;
        }
    }
}
