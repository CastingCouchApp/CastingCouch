using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using CreatorControlSuite.Core.Configuration;
using Microsoft.Win32;

namespace CreatorControlSuite.App;

public partial class DashboardSceneButtonEditorWindow : Window
{
    private static readonly string[] EmojiChoices =
    [
        "🎬", "🎮", "🚀", "☕", "🏁", "📺", "🎤", "🎵",
        "⏸", "▶", "🔴", "🟢", "⭐", "🏠", "📷", "💡"
    ];

    private static readonly (string Label, string Glyph)[] GlyphChoices =
    [
        ("Play", "\uE768"),
        ("Pause", "\uE769"),
        ("Stop", "\uE71A"),
        ("Home", "\uE80F"),
        ("Camera", "\uE722"),
        ("Video", "\uE714"),
        ("Game", "\uE7FC"),
        ("Star", "\uE734"),
        ("Music", "\uE8D6"),
        ("Mic", "\uE720"),
        ("Live", "\uE93E"),
        ("Flag", "\uE7C1"),
    ];

    private readonly bool _seedTitleFromScene;

    public DashboardSceneButtonSettings Result { get; private set; }

    public DashboardSceneButtonEditorWindow(
        IReadOnlyList<string> availableScenes,
        DashboardSceneButtonSettings? existing = null)
    {
        InitializeComponent();

        Result = existing is null
            ? new DashboardSceneButtonSettings()
            : new DashboardSceneButtonSettings
            {
                Id = existing.Id,
                Title = existing.Title,
                SceneName = existing.SceneName,
                IconKind = existing.IconKind,
                IconValue = existing.IconValue
            };

        _seedTitleFromScene = existing is null ||
            string.IsNullOrWhiteSpace(existing.Title) ||
            string.Equals(existing.Title, existing.SceneName, StringComparison.OrdinalIgnoreCase);

        SceneBox.ItemsSource = availableScenes.ToList();
        if (!string.IsNullOrWhiteSpace(Result.SceneName) &&
            availableScenes.Any(scene => string.Equals(scene, Result.SceneName, StringComparison.OrdinalIgnoreCase)))
        {
            SceneBox.SelectedItem = availableScenes.First(scene =>
                string.Equals(scene, Result.SceneName, StringComparison.OrdinalIgnoreCase));
        }
        else if (availableScenes.Count > 0)
        {
            SceneBox.SelectedIndex = 0;
        }

        TitleBox.Text = Result.Title;
        if (string.IsNullOrWhiteSpace(TitleBox.Text) && SceneBox.SelectedItem is string selectedScene)
        {
            TitleBox.Text = selectedScene;
        }

        BuildEmojiPicker();
        GlyphBox.ItemsSource = GlyphChoices.Select(item => item.Label).ToList();

        string kind = NormalizeKind(Result.IconKind);
        switch (kind)
        {
            case "Glyph":
                GlyphKindRadio.IsChecked = true;
                (string Label, string Glyph) glyphMatch = GlyphChoices.FirstOrDefault(item => item.Glyph == Result.IconValue);
                GlyphBox.SelectedItem = glyphMatch.Label ?? GlyphChoices[0].Label;
                break;
            case "Image":
                ImageKindRadio.IsChecked = true;
                ImagePathBox.Text = Result.IconValue;
                UpdateImagePreview(Result.IconValue);
                break;
            default:
                EmojiKindRadio.IsChecked = true;
                EmojiValueBox.Text = string.IsNullOrWhiteSpace(Result.IconValue) ? "🎬" : Result.IconValue;
                break;
        }

        UpdateIconPanels();

        EmojiKindRadio.Checked += (_, _) => UpdateIconPanels();
        GlyphKindRadio.Checked += (_, _) => UpdateIconPanels();
        ImageKindRadio.Checked += (_, _) => UpdateIconPanels();
        SceneBox.SelectionChanged += OnSceneSelectionChanged;
        BrowseImageButton.Click += (_, _) => BrowseImage();
        CancelButton.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        SaveButton.Click += (_, _) => Save();
    }

    private void BuildEmojiPicker()
    {
        EmojiPickerPanel.Children.Clear();
        foreach (string emoji in EmojiChoices)
        {
            var button = new Button
            {
                Content = emoji,
                Width = 36,
                Height = 36,
                Margin = new Thickness(0, 0, 6, 6),
                FontSize = 16,
                Tag = emoji
            };
            button.Click += (_, _) =>
            {
                EmojiValueBox.Text = emoji;
            };
            EmojiPickerPanel.Children.Add(button);
        }
    }

    private void OnSceneSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_seedTitleFromScene)
        {
            return;
        }

        if (SceneBox.SelectedItem is string sceneName)
        {
            TitleBox.Text = sceneName;
        }
    }

    private void UpdateIconPanels()
    {
        EmojiPanel.Visibility = EmojiKindRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        GlyphPanel.Visibility = GlyphKindRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ImagePanel.Visibility = ImageKindRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseImage()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Bilder|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|Alle Dateien|*.*"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ImagePathBox.Text = dialog.FileName;
        UpdateImagePreview(dialog.FileName);
    }

    private void UpdateImagePreview(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                ImagePreview.Source = null;
                return;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            ImagePreview.Source = bitmap;
        }
        catch
        {
            ImagePreview.Source = null;
        }
    }

    private void Save()
    {
        if (SceneBox.SelectedItem is not string sceneName || string.IsNullOrWhiteSpace(sceneName))
        {
            MessageBox.Show(
                "Bitte eine OBS-Szene auswählen.",
                "Szenen-Button",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string? title = TitleBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            title = sceneName;
        }

        string iconKind;
        string iconValue;
        if (GlyphKindRadio.IsChecked == true)
        {
            iconKind = "Glyph";
            string label = GlyphBox.SelectedItem as string ?? GlyphChoices[0].Label;
            iconValue = GlyphChoices.FirstOrDefault(item => item.Label == label).Glyph ?? GlyphChoices[0].Glyph;
        }
        else if (ImageKindRadio.IsChecked == true)
        {
            iconKind = "Image";
            iconValue = ImagePathBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(iconValue) || !File.Exists(iconValue))
            {
                MessageBox.Show(
                    "Bitte eine gültige Bilddatei wählen.",
                    "Szenen-Button",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
        }
        else
        {
            iconKind = "Emoji";
            iconValue = string.IsNullOrWhiteSpace(EmojiValueBox.Text) ? "🎬" : EmojiValueBox.Text.Trim();
        }

        Result.SceneName = sceneName;
        Result.Title = title;
        Result.IconKind = iconKind;
        Result.IconValue = iconValue;
        if (string.IsNullOrWhiteSpace(Result.Id))
        {
            Result.Id = Guid.NewGuid().ToString("N");
        }

        DialogResult = true;
        Close();
    }

    private static string NormalizeKind(string? kind)
    {
        if (string.Equals(kind, "Glyph", StringComparison.OrdinalIgnoreCase))
        {
            return "Glyph";
        }

        if (string.Equals(kind, "Image", StringComparison.OrdinalIgnoreCase))
        {
            return "Image";
        }

        return "Emoji";
    }
}
