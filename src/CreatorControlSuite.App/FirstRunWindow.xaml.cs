using System.Windows;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Setup;
using CreatorControlSuite.Modules.Overlay;

namespace CreatorControlSuite.App;

public partial class FirstRunWindow : Window
{
    private readonly ISettingsStore _settingsStore;
    private readonly IFirstRunService _firstRunService;
    private readonly OverlayModule _overlayModule;
    private readonly FrameworkElement[] _steps;
    private int _stepIndex;

    public bool OpenSettingsAfterCompletion { get; private set; }

    public FirstRunWindow(
        ISettingsStore settingsStore,
        IFirstRunService firstRunService,
        OverlayModule overlayModule)
    {
        InitializeComponent();

        _settingsStore = settingsStore;
        _firstRunService = firstRunService;
        _overlayModule = overlayModule;

        _steps =
        [
            WelcomeStep,
            BrandingStep,
            ObsStep,
            OverlayStep,
            SummaryStep
        ];

        BackButton.Click += (_, _) => Move(-1);
        NextButton.Click += async (_, _) => await MoveNextAsync();
        CancelButton.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };

        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var settings = await _settingsStore.LoadAsync();

        DisplayNameBox.Text = settings.Branding.DisplayName;
        ChannelNameBox.Text = settings.Twitch.ChannelName;
        ObsHostBox.Text = settings.Obs.Host;
        ObsPortBox.Text = settings.Obs.Port.ToString();
        StartSceneBox.Text = settings.Obs.StartScene;
        LiveSceneBox.Text = settings.Obs.LiveScene;
        PauseSceneBox.Text = settings.Obs.PauseScene;
        EndSceneBox.Text = settings.Obs.EndScene;
        OverlayRootBox.Text = settings.Overlay.RootPath;
        InstallBundledOverlayBox.IsChecked =
            settings.Overlay.UseBundledOverlay;
    }

    private async Task MoveNextAsync()
    {
        if (_stepIndex < _steps.Length - 1)
        {
            if (!ValidateCurrentStep())
            {
                return;
            }

            _stepIndex++;
            ShowStep();

            if (_stepIndex == _steps.Length - 1)
            {
                UpdateSummary();
            }

            return;
        }

        await CompleteAsync();
    }

    private void Move(int direction)
    {
        _stepIndex = Math.Clamp(
            _stepIndex + direction,
            0,
            _steps.Length - 1);

        ShowStep();
    }

    private void ShowStep()
    {
        for (var index = 0; index < _steps.Length; index++)
        {
            _steps[index].Visibility =
                index == _stepIndex
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        StepTitleText.Text = _stepIndex switch
        {
            0 => "Willkommen",
            1 => "Branding",
            2 => "OBS",
            3 => "Overlay",
            _ => "Abschluss"
        };

        BackButton.IsEnabled = _stepIndex > 0;
        NextButton.Content =
            _stepIndex == _steps.Length - 1
                ? "Einrichtung abschließen"
                : "Weiter";
    }

    private bool ValidateCurrentStep()
    {
        if (_stepIndex == 1 &&
            string.IsNullOrWhiteSpace(DisplayNameBox.Text))
        {
            MessageBox.Show(
                "Bitte einen Anzeigenamen eintragen.",
                "Ersteinrichtung",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return false;
        }

        if (_stepIndex == 2)
        {
            if (!int.TryParse(
                    ObsPortBox.Text.Trim(),
                    out var port) ||
                port is < 1 or > 65535)
            {
                MessageBox.Show(
                    "Bitte einen gültigen OBS-Port eintragen.",
                    "Ersteinrichtung",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return false;
            }

            if (new[]
                {
                    StartSceneBox.Text,
                    LiveSceneBox.Text,
                    PauseSceneBox.Text,
                    EndSceneBox.Text
                }
                .Any(string.IsNullOrWhiteSpace))
            {
                MessageBox.Show(
                    "Bitte alle OBS-Szenennamen eintragen.",
                    "Ersteinrichtung",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return false;
            }
        }

        return true;
    }

    private void UpdateSummary()
    {
        SummaryText.Text =
            $"Anzeigename: {DisplayNameBox.Text.Trim()}\n" +
            $"Twitch-Kanal: {ChannelNameBox.Text.Trim()}\n\n" +
            $"OBS: {ObsHostBox.Text.Trim()}:{ObsPortBox.Text.Trim()}\n" +
            $"Start: {StartSceneBox.Text.Trim()}\n" +
            $"Live: {LiveSceneBox.Text.Trim()}\n" +
            $"Pause: {PauseSceneBox.Text.Trim()}\n" +
            $"Ende: {EndSceneBox.Text.Trim()}\n\n" +
            $"Overlay: " +
            (string.IsNullOrWhiteSpace(OverlayRootBox.Text)
                ? "Standardpfad"
                : OverlayRootBox.Text.Trim()) +
            "\nStandard-Overlay: " +
            (InstallBundledOverlayBox.IsChecked == true
                ? "Ja"
                : "Nein");
    }

    private async Task CompleteAsync()
    {
        var settings = await _settingsStore.LoadAsync();

        settings.Branding.DisplayName =
            DisplayNameBox.Text.Trim();

        settings.Branding.ChannelName =
            ChannelNameBox.Text.Trim();

        settings.Twitch.ChannelName =
            ChannelNameBox.Text.Trim();

        settings.Obs.Host =
            ObsHostBox.Text.Trim();

        settings.Obs.Port =
            int.Parse(ObsPortBox.Text.Trim());

        settings.Obs.StartScene =
            StartSceneBox.Text.Trim();

        settings.Obs.LiveScene =
            LiveSceneBox.Text.Trim();

        settings.Obs.PauseScene =
            PauseSceneBox.Text.Trim();

        settings.Obs.EndScene =
            EndSceneBox.Text.Trim();

        settings.Overlay.RootPath =
            OverlayRootBox.Text.Trim();

        settings.Overlay.UseBundledOverlay =
            InstallBundledOverlayBox.IsChecked == true;

        await _settingsStore.SaveAsync(settings);

        if (settings.Overlay.UseBundledOverlay)
        {
            await _overlayModule.Service.InstallBundledOverlayAsync();
            await _overlayModule.Service.InitializeAsync();
        }

        await _firstRunService.SaveStateAsync(
            new FirstRunState
            {
                Completed = true,
                CompletedVersion = 1,
                CompletedAt = DateTimeOffset.Now
            });

        OpenSettingsAfterCompletion =
            OpenSettingsAfterWizardBox.IsChecked == true;

        DialogResult = true;
        Close();
    }
}
