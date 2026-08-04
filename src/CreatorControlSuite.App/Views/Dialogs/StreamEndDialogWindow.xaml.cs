using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.App.Views.Dialogs;

public partial class StreamEndDialogWindow : Window
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(280);

    private bool _flowStarted;
    private bool _allowClose;
    private bool _suppressSearch;
    private readonly Action<string>? _openRaidChannel;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<TwitchChannelSuggestion>>>? _suggestRaidTargets;
    private readonly Action<string>? _raidTargetChanged;
    private readonly DispatcherTimer _searchTimer;
    private CancellationTokenSource? _searchCts;

    public StreamEndMode SelectedMode { get; private set; } = StreamEndMode.EndSceneThenStop;
    public string? SelectedRaidChannel { get; private set; }
    public int SelectedEndSceneSeconds { get; private set; }
    public bool Confirmed { get; private set; }

    public event Action<StreamEndMode, string?, int>? SelectionConfirmed;
    public event Action? StartRaidRequested;
    public event Action? SkipRaidRequested;
    public event Action? CancelRaidRequested;
    public event Action? CancelFlowRequested;

    public StreamEndDialogWindow(
        StreamEndMode initialMode,
        IReadOnlyList<string> raidChannels,
        string? selectedRaidChannel,
        int endSceneSeconds,
        Action<string>? openRaidChannel = null,
        Func<string, CancellationToken, Task<IReadOnlyList<TwitchChannelSuggestion>>>? suggestRaidTargets = null,
        Action<string>? raidTargetChanged = null)
    {
        InitializeComponent();
        _openRaidChannel = openRaidChannel;
        _suggestRaidTargets = suggestRaidTargets;
        _raidTargetChanged = raidTargetChanged;

        string initial = !string.IsNullOrWhiteSpace(selectedRaidChannel)
            ? selectedRaidChannel.Trim().TrimStart('@')
            : raidChannels.FirstOrDefault() ?? "";
        RaidChannelSearchBox.Text = initial;

        SelectedEndSceneSeconds = Math.Max(0, endSceneSeconds);
        EndSceneSecondsBox.Text = SelectedEndSceneSeconds.ToString();

        _searchTimer = new DispatcherTimer { Interval = SearchDebounce };
        _searchTimer.Tick += async (_, _) =>
        {
            _searchTimer.Stop();
            await SearchRaidTargetsAsync();
        };

        RaidChannelSearchBox.TextChanged += RaidChannelSearchBox_OnTextChanged;
        RaidChannelSearchBox.PreviewKeyDown += RaidChannelSearchBox_OnPreviewKeyDown;
        RaidChannelSearchBox.GotKeyboardFocus += async (_, _) =>
        {
            if (_suggestRaidTargets is not null)
            {
                await SearchRaidTargetsAsync();
            }
        };
        RaidChannelSearchBox.LostKeyboardFocus += RaidChannelSearchBox_OnLostKeyboardFocus;

        ApplyInitialMode(initialMode);
        ImmediateRadio.Checked += (_, _) => UpdateModeDependentPanels();
        EndSceneRadio.Checked += (_, _) => UpdateModeDependentPanels();
        EndSceneRaidRadio.Checked += (_, _) => UpdateModeDependentPanels();
        UpdateModeDependentPanels();

        ConfirmButton.Click += (_, _) => ConfirmSelection();
        CancelButton.Click += (_, _) => RequestCancel();
        CloseWhenDoneButton.Click += (_, _) =>
        {
            _allowClose = true;
            Close();
        };
        OpenRaidChannelButton.Click += (_, _) =>
        {
            string channel = GetRaidChannelText();
            if (!string.IsNullOrWhiteSpace(channel))
            {
                _openRaidChannel?.Invoke(channel);
            }
        };
        StartRaidButton.Click += (_, _) => StartRaidRequested?.Invoke();
        SkipRaidButton.Click += (_, _) => SkipRaidRequested?.Invoke();
        CancelRaidButton.Click += (_, _) => CancelRaidRequested?.Invoke();
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _searchTimer.Stop();
            _searchCts?.Cancel();
            _searchCts?.Dispose();
        };

        if (!string.IsNullOrWhiteSpace(initial))
        {
            _raidTargetChanged?.Invoke(initial);
        }
    }

    public void EnterRunningPhase(string phaseTitle, string status)
    {
        _flowStarted = true;
        SelectionPanel.Visibility = Visibility.Collapsed;
        RunningPanel.Visibility = Visibility.Visible;
        ConfirmButton.Visibility = Visibility.Collapsed;
        CancelButton.Content = "ABBRECHEN";
        CancelButton.Visibility = Visibility.Visible;
        CloseWhenDoneButton.Visibility = Visibility.Collapsed;
        PhaseTitleText.Text = phaseTitle;
        RunningStatusText.Text = status;
        SubtitleText.Text = "Der Streamende-Ablauf läuft.";
        CountdownText.Text = "—";
        CountdownProgress.Minimum = 0;
        CountdownProgress.Maximum = 1;
        CountdownProgress.Value = 0;
        SetRaidReady(false);
        SkipRaidButton.Visibility = Visibility.Collapsed;
        CancelRaidButton.Visibility = Visibility.Collapsed;
        CancelRaidButton.IsEnabled = false;
        CloseSuggestions();
    }

    public void ShowRaidActions(bool waitingForRaid)
    {
        SkipRaidButton.Visibility = waitingForRaid ? Visibility.Visible : Visibility.Collapsed;
        if (!waitingForRaid)
        {
            SetRaidReady(false);
        }
    }

    public void SetRaidReady(bool ready)
    {
        StartRaidButton.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        StartRaidButton.IsEnabled = ready;
    }

    public void SetCancelRaidEnabled(bool enabled)
    {
        CancelRaidButton.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        CancelRaidButton.IsEnabled = enabled;
    }

    public void UpdateCountdown(string label, string clock, double progressValue, double progressMaximum)
    {
        PhaseTitleText.Text = label;
        CountdownText.Text = clock;
        CountdownProgress.Minimum = 0;
        CountdownProgress.Maximum = Math.Max(1, progressMaximum);
        CountdownProgress.Value = Math.Clamp(progressValue, 0, CountdownProgress.Maximum);
    }

    public void SetStatus(string status)
    {
        RunningStatusText.Text = status;
    }

    public void SetRaidTargetStatus(string text)
    {
        RaidTargetStatusText.Text = text;
        RaidTargetRunningText.Text = text;
    }

    public void MarkCompleted(string status)
    {
        _allowClose = true;
        SetStatus(status);
        StartRaidButton.Visibility = Visibility.Collapsed;
        SkipRaidButton.Visibility = Visibility.Collapsed;
        CancelRaidButton.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed;
        CloseWhenDoneButton.Visibility = Visibility.Visible;
        ConfirmButton.Visibility = Visibility.Collapsed;
    }

    private void ApplyInitialMode(StreamEndMode mode)
    {
        switch (mode)
        {
            case StreamEndMode.Immediate:
                ImmediateRadio.IsChecked = true;
                break;
            case StreamEndMode.EndSceneRaidThenStop:
                EndSceneRaidRadio.IsChecked = true;
                break;
            default:
                EndSceneRadio.IsChecked = true;
                break;
        }
    }

    private void UpdateModeDependentPanels()
    {
        bool needsDuration = EndSceneRadio.IsChecked == true;
        EndSceneDurationPanel.Visibility = needsDuration ? Visibility.Visible : Visibility.Collapsed;
        RaidTargetPanel.Visibility = EndSceneRaidRadio.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (EndSceneRaidRadio.IsChecked != true)
        {
            CloseSuggestions();
        }
    }

    private void RaidChannelSearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSearch || _suggestRaidTargets is null)
        {
            return;
        }

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void RaidChannelSearchBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!RaidSuggestionsPopup.IsOpen || RaidSuggestionsBox.Items.Count == 0)
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
                if (RaidSuggestionsBox.SelectedItem is TwitchChannelSuggestion selected)
                {
                    ApplySuggestion(selected);
                    e.Handled = true;
                }

                break;
            case Key.Escape:
                CloseSuggestions();
                e.Handled = true;
                break;
        }
    }

    private void RaidChannelSearchBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is DependencyObject focus &&
            (ReferenceEquals(focus, RaidSuggestionsBox) || IsDescendantOf(focus, RaidSuggestionsBox)))
        {
            return;
        }

        CloseSuggestions();
    }

    private void RaidSuggestionsBox_OnPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(RaidSuggestionsBox, e.OriginalSource as DependencyObject)
            is ListBoxItem { DataContext: TwitchChannelSuggestion selected })
        {
            ApplySuggestion(selected);
            e.Handled = true;
        }
    }

    private async Task SearchRaidTargetsAsync()
    {
        if (_suggestRaidTargets is null)
        {
            return;
        }

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        CancellationToken token = _searchCts.Token;
        string query = GetRaidChannelText();

        try
        {
            IReadOnlyList<TwitchChannelSuggestion> suggestions = await _suggestRaidTargets(query, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            RaidSuggestionsBox.ItemsSource = suggestions;
            if (suggestions.Count == 0)
            {
                CloseSuggestions();
                return;
            }

            RaidSuggestionsBox.SelectedIndex = 0;
            RaidSuggestionsPopup.IsOpen = true;
        }
        catch (OperationCanceledException)
        {
            // superseded
        }
        catch
        {
            if (!token.IsCancellationRequested)
            {
                CloseSuggestions();
            }
        }
    }

    private void ApplySuggestion(TwitchChannelSuggestion suggestion)
    {
        _suppressSearch = true;
        try
        {
            RaidChannelSearchBox.Text = suggestion.Login;
            RaidChannelSearchBox.CaretIndex = RaidChannelSearchBox.Text.Length;
            CloseSuggestions();
        }
        finally
        {
            _suppressSearch = false;
        }

        _raidTargetChanged?.Invoke(suggestion.Login);
        RaidChannelSearchBox.Focus();
    }

    private void MoveSuggestion(int delta)
    {
        int count = RaidSuggestionsBox.Items.Count;
        if (count == 0)
        {
            return;
        }

        int next = RaidSuggestionsBox.SelectedIndex + delta;
        if (next < 0)
        {
            next = count - 1;
        }
        else if (next >= count)
        {
            next = 0;
        }

        RaidSuggestionsBox.SelectedIndex = next;
        RaidSuggestionsBox.ScrollIntoView(RaidSuggestionsBox.SelectedItem);
    }

    private void CloseSuggestions()
    {
        RaidSuggestionsPopup.IsOpen = false;
        RaidSuggestionsBox.ItemsSource = null;
    }

    private string GetRaidChannelText() =>
        RaidChannelSearchBox.Text.Trim().TrimStart('@');

    private void ConfirmSelection()
    {
        if (ImmediateRadio.IsChecked == true)
        {
            SelectedMode = StreamEndMode.Immediate;
            SelectedEndSceneSeconds = 0;
        }
        else if (EndSceneRaidRadio.IsChecked == true)
        {
            SelectedMode = StreamEndMode.EndSceneRaidThenStop;
            SelectedRaidChannel = GetRaidChannelText();
            if (string.IsNullOrWhiteSpace(SelectedRaidChannel))
            {
                RaidTargetStatusText.Text = "Bitte ein Raid-Ziel auswählen.";
                RaidChannelSearchBox.Focus();
                return;
            }

            // Im Raid-Modus bestimmt Twitch den Zeitpunkt. Die Endszene bleibt
            // sichtbar, bis das ausgehende Raid-Event eingetroffen ist.
            SelectedEndSceneSeconds = 0;
        }
        else
        {
            SelectedMode = StreamEndMode.EndSceneThenStop;
            if (!TryReadEndSceneSeconds(out int seconds))
            {
                return;
            }

            SelectedEndSceneSeconds = seconds;
        }

        Confirmed = true;
        CloseSuggestions();
        SelectionConfirmed?.Invoke(SelectedMode, SelectedRaidChannel, SelectedEndSceneSeconds);
    }

    private bool TryReadEndSceneSeconds(out int seconds)
    {
        if (!int.TryParse(EndSceneSecondsBox.Text.Trim(), out seconds) || seconds < 0)
        {
            EndSceneSecondsBox.ToolTip = "Bitte eine gültige Sekundenanzahl eingeben (0 oder größer).";
            EndSceneSecondsBox.Focus();
            EndSceneSecondsBox.SelectAll();
            return false;
        }

        seconds = Math.Max(0, seconds);
        return true;
    }

    private void RequestCancel()
    {
        if (!_flowStarted)
        {
            Confirmed = false;
            _allowClose = true;
            Close();
            return;
        }

        CancelFlowRequested?.Invoke();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose || !_flowStarted)
        {
            if (!_flowStarted && !Confirmed)
            {
                Confirmed = false;
            }

            return;
        }

        e.Cancel = true;
        CancelFlowRequested?.Invoke();
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
