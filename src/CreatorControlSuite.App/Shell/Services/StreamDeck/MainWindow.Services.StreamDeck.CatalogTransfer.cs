#nullable enable

using System.IO.Compression;
using System.Windows.Controls;
using System.Windows.Media;

namespace CreatorControlSuite.App.Shell;

public partial class MainWindow
{
    private void BackupStreamDeckConfiguration()
    {
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Stream-Deck-Komplettbackup (*.zip)|*.zip",
            FileName =
                $"CreatorControlSuite-StreamDeck-Backup-" +
                $"{DateTime.Now:yyyyMMdd-HHmm}.zip"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (File.Exists(dialog.FileName))
        {
            File.Delete(dialog.FileName);
        }

        ZipFile.CreateFromDirectory(
            StreamDeckActionsDirectory,
            dialog.FileName);
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionCreateStatusText.Text =
            "Komplettbackup erstellt: " + dialog.FileName;
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionCreateStatusText.Foreground =
            Brushes.LightGreen;
    }

    private void RestoreStreamDeckConfiguration()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Stream-Deck-Komplettbackup (*.zip)|*.zip"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        Directory.CreateDirectory(StreamDeckActionsDirectory);
        foreach (string file in
                 Directory.EnumerateFiles(StreamDeckActionsDirectory))
        {
            File.Delete(file);
        }

        ZipFile.ExtractToDirectory(
            dialog.FileName,
            StreamDeckActionsDirectory,
            overwriteFiles: true);
        RefreshStreamDeckActionsList();
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionCreateStatusText.Text =
            "Stream-Deck-Konfiguration wiederhergestellt.";
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionCreateStatusText.Foreground =
            Brushes.LightGreen;
    }

    private void ExportStreamDeckActionCatalog()
    {
        Directory.CreateDirectory(StreamDeckActionsDirectory);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Stream-Deck-Aktionskatalog (*.zip)|*.zip",
            FileName =
                $"CreatorControlSuite-StreamDeck-Actions-" +
                $"{DateTime.Now:yyyyMMdd-HHmm}.zip"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (File.Exists(dialog.FileName))
        {
            File.Delete(dialog.FileName);
        }

        ZipFile.CreateFromDirectory(
            StreamDeckActionsDirectory,
            dialog.FileName);
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionCreateStatusText.Text =
            "Aktionskatalog exportiert: " + dialog.FileName;
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionCreateStatusText.Foreground =
            Brushes.LightGreen;
    }

    private void ImportStreamDeckActionCatalog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Stream-Deck-Aktionskatalog (*.zip)|*.zip"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        Directory.CreateDirectory(StreamDeckActionsDirectory);
        ZipFile.ExtractToDirectory(
            dialog.FileName,
            StreamDeckActionsDirectory,
            overwriteFiles: true);
        RefreshStreamDeckActionsList();
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionCreateStatusText.Text =
            "Aktionskatalog importiert.";
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionCreateStatusText.Foreground =
            Brushes.LightGreen;
    }

    private void ExportSelectedStreamDeckAction()
    {
        if (ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckCreatedActionsList.SelectedItem
            is not ListBoxItem item ||
            item.Tag is not string file)
        {
            ServicesPageViewHost.StreamDeckServiceViewHost
                .StreamDeckActionCreateStatusText.Text =
                "Bitte zuerst eine Taste auswählen.";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Stream-Deck-Taste (*.sdaction)|*.sdaction",
            FileName =
                Path.GetFileNameWithoutExtension(file) + ".sdaction"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        using ZipArchive archive = ZipFile.Open(
            dialog.FileName,
            ZipArchiveMode.Create);
        archive.CreateEntryFromFile(file, Path.GetFileName(file));
        string metadata = Path.ChangeExtension(file, ".json");
        if (File.Exists(metadata))
        {
            archive.CreateEntryFromFile(
                metadata,
                Path.GetFileName(metadata));
        }

        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionCreateStatusText.Text =
            "Taste exportiert: " + dialog.FileName;
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionCreateStatusText.Foreground =
            Brushes.LightGreen;
    }

    private void ImportSingleStreamDeckAction()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Stream-Deck-Taste (*.sdaction)|*.sdaction"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        Directory.CreateDirectory(StreamDeckActionsDirectory);
        ZipFile.ExtractToDirectory(
            dialog.FileName,
            StreamDeckActionsDirectory,
            overwriteFiles: true);
        RefreshStreamDeckActionsList();
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionCreateStatusText.Text =
            "Einzelne Taste importiert.";
        ServicesPageViewHost.StreamDeckServiceViewHost
            .StreamDeckActionCreateStatusText.Foreground =
            Brushes.LightGreen;
    }
}
