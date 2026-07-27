using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.App;

public partial class TwitchChannelEditorWindow : Window
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(280);

    private readonly Func<string, Task<IReadOnlyList<TwitchCategory>>> _searchCategories;
    private readonly Func<string, string?, string, Task> _save;
    private readonly DispatcherTimer _searchTimer;
    private CancellationTokenSource? _searchCts;
    private TwitchCategory? _selectedCategory;
    private bool _suppressSearch;

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

        _searchTimer = new DispatcherTimer { Interval = SearchDebounce };
        _searchTimer.Tick += async (_, _) =>
        {
            _searchTimer.Stop();
            await SearchAsync();
        };

        CategorySearchBox.TextChanged += CategorySearchBox_OnTextChanged;
        CategorySearchBox.PreviewKeyDown += CategorySearchBox_OnPreviewKeyDown;
        CategorySearchBox.LostKeyboardFocus += CategorySearchBox_OnLostKeyboardFocus;
        SaveButton.Click += async (_, _) => await SaveAsync();
        CancelButton.Click += (_, _) => Close();
        Closed += (_, _) =>
        {
            _searchTimer.Stop();
            _searchCts?.Cancel();
            _searchCts?.Dispose();
        };
    }

    private void CategorySearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSearch)
        {
            return;
        }

        if (_selectedCategory is not null &&
            !string.Equals(_selectedCategory.Name, CategorySearchBox.Text, StringComparison.Ordinal))
        {
            _selectedCategory = null;
        }

        _searchTimer.Stop();
        string query = CategorySearchBox.Text.Trim();
        if (query.Length < 2)
        {
            CloseSuggestions();
            StatusText.Text = query.Length == 0
                ? ""
                : "Mindestens 2 Zeichen für die Suche.";
            return;
        }

        StatusText.Text = "Suche…";
        _searchTimer.Start();
    }

    private void CategorySearchBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!CategorySuggestionsPopup.IsOpen || CategorySuggestionsBox.Items.Count == 0)
        {
            if (e.Key == Key.Escape)
            {
                CloseSuggestions();
            }

            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                MoveSuggestion(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSuggestion(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                if (CategorySuggestionsBox.SelectedItem is TwitchCategory selected)
                {
                    ApplySelection(selected);
                    e.Handled = true;
                }

                break;
            case Key.Escape:
                CloseSuggestions();
                e.Handled = true;
                break;
        }
    }

    private void CategorySearchBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is DependencyObject focus &&
            (ReferenceEquals(focus, CategorySuggestionsBox) || IsDescendantOf(focus, CategorySuggestionsBox)))
        {
            return;
        }

        CloseSuggestions();
    }

    private void CategorySuggestionsBox_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(CategorySuggestionsBox, e.OriginalSource as DependencyObject)
            is ListBoxItem { DataContext: TwitchCategory selected })
        {
            ApplySelection(selected);
            e.Handled = true;
        }
    }

    private async Task SearchAsync()
    {
        string query = CategorySearchBox.Text.Trim();
        if (query.Length < 2)
        {
            return;
        }

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        CancellationToken token = _searchCts.Token;

        try
        {
            IReadOnlyList<TwitchCategory> categories = await _searchCategories(query);
            if (token.IsCancellationRequested)
            {
                return;
            }

            CategorySuggestionsBox.ItemsSource = categories;
            if (categories.Count == 0)
            {
                CloseSuggestions();
                StatusText.Text = "Keine passende Kategorie gefunden.";
                return;
            }

            CategorySuggestionsBox.SelectedIndex = 0;
            CategorySuggestionsPopup.IsOpen = true;
            StatusText.Text = categories.Count == 1
                ? "1 Vorschlag"
                : $"{categories.Count} Vorschläge";
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer query
        }
        catch (Exception exception)
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            CloseSuggestions();
            StatusText.Text = "Kategoriesuche fehlgeschlagen: " + exception.Message;
        }
    }

    private void ApplySelection(TwitchCategory category)
    {
        _suppressSearch = true;
        try
        {
            _selectedCategory = category;
            CategorySearchBox.Text = category.Name;
            CategorySearchBox.CaretIndex = CategorySearchBox.Text.Length;
            CloseSuggestions();
            StatusText.Text = $"Ausgewählt: {category.Name}";
        }
        finally
        {
            _suppressSearch = false;
        }

        CategorySearchBox.Focus();
    }

    private void MoveSuggestion(int delta)
    {
        int count = CategorySuggestionsBox.Items.Count;
        if (count == 0)
        {
            return;
        }

        int next = CategorySuggestionsBox.SelectedIndex + delta;
        if (next < 0)
        {
            next = count - 1;
        }
        else if (next >= count)
        {
            next = 0;
        }

        CategorySuggestionsBox.SelectedIndex = next;
        CategorySuggestionsBox.ScrollIntoView(CategorySuggestionsBox.SelectedItem);
    }

    private void CloseSuggestions()
    {
        CategorySuggestionsPopup.IsOpen = false;
        CategorySuggestionsBox.ItemsSource = null;
    }

    private async Task SaveAsync()
    {
        try
        {
            SaveButton.IsEnabled = false;
            await _save(
                TitleBox.Text.Trim(),
                _selectedCategory?.Id,
                LiveNotificationBox.Text.Trim());
            DialogResult = true;
        }
        catch (Exception exception)
        {
            StatusText.Text = "Speichern fehlgeschlagen: " + exception.Message;
            SaveButton.IsEnabled = true;
        }
    }

    private static bool IsDescendantOf(DependencyObject? node, DependencyObject ancestor)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, ancestor))
            {
                return true;
            }

            node = System.Windows.Media.VisualTreeHelper.GetParent(node);
        }

        return false;
    }
}
