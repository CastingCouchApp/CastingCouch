using System.Windows;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.App;

public partial class TwitchChannelEditorWindow : Window
{
    private readonly Func<string, Task<IReadOnlyList<TwitchCategory>>> _searchCategories;
    private readonly Func<string, string?, string, Task> _save;

    public TwitchChannelEditorWindow(
        string title,
        string category,
        string liveNotification,
        Func<string, Task<IReadOnlyList<TwitchCategory>>> searchCategories,
        Func<string, string?, string, Task> save)
    {
        InitializeComponent();
        _searchCategories = searchCategories;
        _save = save;
        TitleBox.Text = title;
        CategorySearchBox.Text = category;
        LiveNotificationBox.Text = liveNotification;
        SearchCategoryButton.Click += async (_, _) => await SearchAsync();
        SaveButton.Click += async (_, _) => await SaveAsync();
        CancelButton.Click += (_, _) => Close();
    }

    private async Task SearchAsync()
    {
        var query = CategorySearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            StatusText.Text = "Bitte einen Kategorienamen eingeben.";
            return;
        }

        try
        {
            SearchCategoryButton.IsEnabled = false;
            var categories = await _searchCategories(query);
            CategoryResultsBox.ItemsSource = categories;
            CategoryResultsBox.IsDropDownOpen = categories.Count > 0;
            StatusText.Text = categories.Count == 0
                ? "Keine passende Kategorie gefunden."
                : $"{categories.Count} Kategorie(n) gefunden.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Kategoriesuche fehlgeschlagen: " + exception.Message;
        }
        finally
        {
            SearchCategoryButton.IsEnabled = true;
        }
    }

    private async Task SaveAsync()
    {
        try
        {
            SaveButton.IsEnabled = false;
            var category = CategoryResultsBox.SelectedItem as TwitchCategory;
            await _save(
                TitleBox.Text.Trim(),
                category?.Id,
                LiveNotificationBox.Text.Trim());
            DialogResult = true;
        }
        catch (Exception exception)
        {
            StatusText.Text = "Speichern fehlgeschlagen: " + exception.Message;
            SaveButton.IsEnabled = true;
        }
    }
}
