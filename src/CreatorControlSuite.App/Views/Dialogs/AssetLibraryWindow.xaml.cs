using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using CreatorControlSuite.Modules.Overlay.Assets;
using Microsoft.Win32;

namespace CreatorControlSuite.App.Views.Dialogs;

public partial class AssetLibraryWindow : Window
{
    private readonly IOverlayAssetStore _store;
    private readonly ObservableCollection<AssetRow> _rows = [];

    public OverlayAssetInfo? SelectedAsset { get; private set; }

    public AssetLibraryWindow(IOverlayAssetStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        InitializeComponent();
        AssetList.ItemsSource = _rows;
        ImportButton.Click += async (_, _) => await ImportAsync();
        DeleteButton.Click += async (_, _) => await DeleteSelectedAsync();
        CancelButton.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        SelectButton.Click += (_, _) => ConfirmSelection();
        AssetList.MouseDoubleClick += (_, _) => ConfirmSelection();
        Reload();
    }

    private void Reload()
    {
        _rows.Clear();
        foreach (OverlayAssetInfo asset in _store.List())
        {
            _rows.Add(new AssetRow(asset));
        }
    }

    private async Task ImportAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Bild in Asset-Bibliothek laden",
            Filter = "Bilder|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp;*.svg|Alle Dateien|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            await using FileStream stream = File.OpenRead(dialog.FileName);
            OverlayAssetInfo imported = await _store.ImportAsync(stream, Path.GetFileName(dialog.FileName));
            Reload();
            AssetRow? row = _rows.FirstOrDefault(r => r.Asset.Id == imported.Id);
            if (row is not null)
            {
                AssetList.SelectedItem = row;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "Import fehlgeschlagen:\n" + ex.Message,
                "Asset-Bibliothek",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task DeleteSelectedAsync()
    {
        if (AssetList.SelectedItem is not AssetRow row)
        {
            return;
        }

        await _store.DeleteAsync(row.Asset.Id);
        Reload();
    }

    private void ConfirmSelection()
    {
        if (AssetList.SelectedItem is not AssetRow row)
        {
            return;
        }

        SelectedAsset = row.Asset;
        DialogResult = true;
        Close();
    }

    private sealed class AssetRow
    {
        public AssetRow(OverlayAssetInfo asset)
        {
            Asset = asset;
            Name = asset.OriginalName;
            Preview = LoadPreview(asset.LocalPath);
        }

        public OverlayAssetInfo Asset { get; }
        public string Name { get; }
        public BitmapImage? Preview { get; }

        private static BitmapImage? LoadPreview(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(path, UriKind.Absolute);
                image.DecodePixelWidth = 160;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }
    }
}
