using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.App.Services.CreatorIntelligence;

namespace CreatorControlSuite.App.ViewModels.Pages;

/// <summary>Remote control for Creator Intelligence section on the Services page.</summary>
public sealed class CreatorIntelligenceSectionViewModel : ViewModelBase
{
    private readonly CreatorIntelligenceService _service;

    public CreatorIntelligenceSectionViewModel(CreatorIntelligenceService service)
    {
        _service = service;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        OpenFolderCommand = new RelayCommand(OpenFolder);
        WeeklyReportCommand = new AsyncRelayCommand(_ => CreateWeeklyReportAsync());
    }

    public string StatusMessage
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Bereit.";

    public string ScoreText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "–";

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public AsyncRelayCommand WeeklyReportCommand { get; }

    public async Task RefreshAsync()
    {
        CreatorIntelligenceSummary? summary = await _service.AnalyzeLatestSessionAsync();
        if (summary is null)
        {
            StatusMessage = _service.IsRecording
                ? "Session läuft – noch keine abgeschlossene Analyse."
                : "Keine Sessiondaten vorhanden.";
            ScoreText = "–";
            return;
        }

        ScoreText = summary.CreatorScore.ToString();
        StatusMessage =
            $"Letzte Session: {summary.StartedAt:dd.MM.yyyy HH:mm} · Ø {summary.AverageViewers:0.0} · Peak {summary.PeakViewers}";
    }

    private void OpenFolder()
    {
        if (!Directory.Exists(_service.RootDirectory))
        {
            Directory.CreateDirectory(_service.RootDirectory);
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = _service.RootDirectory,
            UseShellExecute = true
        });
    }

    private async Task CreateWeeklyReportAsync()
    {
        string path = await _service.GenerateWeeklyReportAsync();
        StatusMessage = "Wochenbericht: " + path;
    }
}
