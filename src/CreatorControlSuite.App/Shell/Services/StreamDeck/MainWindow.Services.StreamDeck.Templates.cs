#nullable enable

using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Media;

namespace CreatorControlSuite.App.Shell;

public partial class MainWindow
{
    private string StreamDeckTemplatesDirectory => Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "CreatorControlSuite",
        "StreamDeck",
        "Templates");

    private sealed record StreamDeckTemplateItem(
        string Name,
        string Path);

    private void RefreshStreamDeckTemplates()
    {
        Directory.CreateDirectory(StreamDeckTemplatesDirectory);
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckTemplateBox.ItemsSource =
            Directory
                .EnumerateFiles(
                    StreamDeckTemplatesDirectory,
                    "*.json")
                .OrderBy(Path.GetFileNameWithoutExtension)
                .Select(path => new StreamDeckTemplateItem(
                    Path.GetFileNameWithoutExtension(path),
                    path))
                .ToList();
    }

    private async Task SaveStreamDeckTemplateAsync()
    {
        string name = string.IsNullOrWhiteSpace(
            ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckTemplateNameBox.Text)
            ? ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckActionTitleBox.Text.Trim()
            : ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckTemplateNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckActionCreateStatusText.Text =
                "Bitte einen Vorlagennamen eingeben.";
            return;
        }

        string safe = string.Concat(name.Select(
            character => Path.GetInvalidFileNameChars().Contains(character)
                ? '_'
                : character));
        Directory.CreateDirectory(StreamDeckTemplatesDirectory);
        var data = new
        {
            name,
            title = ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckActionTitleBox.Text,
            command =
                (ServicesPageViewHost.StreamDeckServiceViewHost
                    .StreamDeckActionCommandBox.SelectedItem
                    as ComboBoxItem)?.Tag?.ToString() ??
                "workflow.prepare",
            parameter = ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckActionParameterBox.Text,
            multiAction = ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckMultiActionBox.Text,
            condition =
                (ServicesPageViewHost.StreamDeckServiceViewHost
                    .StreamDeckStateConditionBox.SelectedItem
                    as ComboBoxItem)?.Tag?.ToString() ??
                string.Empty,
            trueLabel = ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckTrueLabelBox.Text,
            falseLabel = ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckFalseLabelBox.Text,
            toggleMode = ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckToggleModeBox.IsChecked == true,
            alternateCommand =
                (ServicesPageViewHost.StreamDeckServiceViewHost
                    .StreamDeckAlternateCommandBox.SelectedItem
                    as ComboBoxItem)?.Tag?.ToString() ??
                string.Empty,
            alternateParameter =
                ServicesPageViewHost.StreamDeckServiceViewHost
                    .StreamDeckAlternateParameterBox.Text,
            stepDelayMs = ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckStepDelayBox.Text,
            retryCount = ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckRetryCountBox.Text,
            cooldownMs = ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckCooldownBox.Text
        };
        await File.WriteAllTextAsync(
            Path.Combine(StreamDeckTemplatesDirectory, safe + ".json"),
            JsonSerializer.Serialize(
                data,
                new JsonSerializerOptions { WriteIndented = true }));
        RefreshStreamDeckTemplates();
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionCreateStatusText.Text =
            $"Vorlage gespeichert: {name}";
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionCreateStatusText.Foreground =
            Brushes.LightGreen;
    }

    private async Task LoadSelectedStreamDeckTemplateAsync()
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckTemplateBox.SelectedItem
            is not StreamDeckTemplateItem item ||
            !File.Exists(item.Path))
        {
            ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckActionCreateStatusText.Text =
                "Bitte eine Vorlage auswählen.";
            return;
        }

        using JsonDocument document = JsonDocument.Parse(
            await File.ReadAllTextAsync(item.Path));
        JsonElement root = document.RootElement;
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionTitleBox.Text =
            root.TryGetProperty("title", out JsonElement value)
                ? value.GetString() ?? item.Name
                : item.Name;
        SelectComboBoxByTag(
            ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckActionCommandBox,
            root.TryGetProperty("command", out value)
                ? value.GetString()
                : null);
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionParameterBox.Text =
            root.TryGetProperty("parameter", out value)
                ? value.GetString() ?? ""
                : "";
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckMultiActionBox.Text =
            root.TryGetProperty("multiAction", out value)
                ? value.GetString() ?? ""
                : "";
        SelectComboBoxByTag(
            ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckStateConditionBox,
            root.TryGetProperty("condition", out value)
                ? value.GetString()
                : null);
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckTrueLabelBox.Text =
            root.TryGetProperty("trueLabel", out value)
                ? value.GetString() ?? ""
                : "";
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckFalseLabelBox.Text =
            root.TryGetProperty("falseLabel", out value)
                ? value.GetString() ?? ""
                : "";
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckToggleModeBox.IsChecked =
            root.TryGetProperty("toggleMode", out value) &&
            value.ValueKind == JsonValueKind.True;
        SelectComboBoxByTag(
            ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckAlternateCommandBox,
            root.TryGetProperty("alternateCommand", out value)
                ? value.GetString()
                : null);
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckAlternateParameterBox.Text =
            root.TryGetProperty("alternateParameter", out value)
                ? value.GetString() ?? ""
                : "";
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckStepDelayBox.Text =
            root.TryGetProperty("stepDelayMs", out value)
                ? value.ToString()
                : "250";
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckRetryCountBox.Text =
            root.TryGetProperty("retryCount", out value)
                ? value.ToString()
                : "1";
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckCooldownBox.Text =
            root.TryGetProperty("cooldownMs", out value)
                ? value.ToString()
                : "1000";
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionCreateStatusText.Text =
            $"Vorlage geladen: {item.Name}";
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionCreateStatusText.Foreground =
            Brushes.LightGreen;
    }

    private static void SelectComboBoxByTag(
        ComboBox box,
        string? tag)
    {
        foreach (ComboBoxItem entry in box.Items.OfType<ComboBoxItem>())
        {
            if (!string.Equals(
                    entry.Tag?.ToString(),
                    tag ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            box.SelectedItem = entry;
            return;
        }
    }

    private void DeleteSelectedStreamDeckTemplate()
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckTemplateBox.SelectedItem
            is not StreamDeckTemplateItem item)
        {
            return;
        }

        if (File.Exists(item.Path))
        {
            File.Delete(item.Path);
        }

        RefreshStreamDeckTemplates();
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionCreateStatusText.Text =
            $"Vorlage gelöscht: {item.Name}";
    }
}
