using System.Windows;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.App;

public partial class StreamEndDialogWindow : Window
{
    private bool _flowStarted;
    private bool _allowClose;
    private readonly Action<string>? _openRaidChannel;

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
        Action<string>? openRaidChannel = null)
    {
        InitializeComponent();
        _openRaidChannel = openRaidChannel;

        RaidChannelBox.ItemsSource = raidChannels;
        if (!string.IsNullOrWhiteSpace(selectedRaidChannel))
        {
            RaidChannelBox.SelectedItem = raidChannels.FirstOrDefault(channel =>
                string.Equals(channel, selectedRaidChannel, StringComparison.OrdinalIgnoreCase));
        }

        RaidChannelBox.SelectedItem ??= raidChannels.FirstOrDefault();
        SelectedEndSceneSeconds = Math.Max(0, endSceneSeconds);
        EndSceneSecondsBox.Text = SelectedEndSceneSeconds.ToString();

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
            if (RaidChannelBox.SelectedItem is string channel)
            {
                _openRaidChannel?.Invoke(channel);
            }
        };
        StartRaidButton.Click += (_, _) => StartRaidRequested?.Invoke();
        SkipRaidButton.Click += (_, _) => SkipRaidRequested?.Invoke();
        CancelRaidButton.Click += (_, _) => CancelRaidRequested?.Invoke();
        Closing += OnClosing;
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
        var needsDuration = EndSceneRadio.IsChecked == true || EndSceneRaidRadio.IsChecked == true;
        EndSceneDurationPanel.Visibility = needsDuration ? Visibility.Visible : Visibility.Collapsed;
        RaidTargetPanel.Visibility = EndSceneRaidRadio.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

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
            SelectedRaidChannel = RaidChannelBox.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(SelectedRaidChannel))
            {
                RaidTargetStatusText.Text = "Bitte ein Raid-Ziel auswählen.";
                return;
            }

            if (!TryReadEndSceneSeconds(out var seconds))
            {
                return;
            }

            SelectedEndSceneSeconds = seconds;
        }
        else
        {
            SelectedMode = StreamEndMode.EndSceneThenStop;
            if (!TryReadEndSceneSeconds(out var seconds))
            {
                return;
            }

            SelectedEndSceneSeconds = seconds;
        }

        Confirmed = true;
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
}
