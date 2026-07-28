using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.App.Services;

namespace CreatorControlSuite.App.ViewModels.Pages;

public sealed record StatisticsMetricOption(
    string Name,
    string Value);

public sealed class StatisticsPageViewModel : ViewModelBase
{
    private readonly StreamStatisticsApplicationService _service;
    private bool _loadingMetric;
    private string _totalStreams = "0";
    private string _totalDuration = "00:00";
    private string _averageViewers = "0.0";
    private string _peakViewers = "0";
    private string _followers = "0";
    private string _averageDuration = "00:00";

    public StatisticsPageViewModel(
        StreamStatisticsApplicationService service)
    {
        _service = service;
        RefreshCommand = new AsyncRelayCommand(
            _ => RefreshRequestedAsync?.Invoke() ?? Task.CompletedTask);
        OpenFolderCommand = new RelayCommand(
            () => OpenFolderRequested?.Invoke());
    }

    public IReadOnlyList<StatisticsMetricOption> Metrics { get; } =
    [
        new("Zuschauerzahl", "ViewerCount"),
        new("Followerzahl", "FollowerCount"),
        new("Sub-Anzahl", "SubscriberCount"),
        new("Neue Follower", "NewFollowers"),
        new("Neue Subs", "NewSubscribers")
    ];

    public string SelectedMetric
    {
        get;
        set
        {
            if (!SetProperty(ref field, value) || _loadingMetric)
            {
                return;
            }

            _ = MetricChangedAsync?.Invoke(value);
        }
    } = "ViewerCount";

    public string TotalStreams => _totalStreams;
    public string TotalDuration => _totalDuration;
    public string AverageViewers => _averageViewers;
    public string PeakViewers => _peakViewers;
    public string Followers => _followers;
    public string AverageDuration => _averageDuration;

    public IReadOnlyList<StreamStatisticsRow> Rows
    {
        get;
        private set => SetProperty(ref field, value);
    } = [];

    public IReadOnlyList<string> Categories
    {
        get;
        private set => SetProperty(ref field, value);
    } = [];

    public IReadOnlyList<string> Development
    {
        get;
        private set => SetProperty(ref field, value);
    } = [];

    public AsyncRelayCommand RefreshCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public Func<Task>? RefreshRequestedAsync { get; set; }
    public Action? OpenFolderRequested { get; set; }
    public Func<string, Task>? MetricChangedAsync { get; set; }

    public void LoadMetric(string? metric)
    {
        _loadingMetric = true;
        SelectedMetric = Metrics.Any(option =>
            string.Equals(
                option.Value,
                metric,
                StringComparison.OrdinalIgnoreCase))
            ? metric!
            : "ViewerCount";
        _loadingMetric = false;
    }

    public async Task LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        StreamStatisticsSnapshot snapshot =
            await _service.LoadAsync(path, cancellationToken);
        SetText(
            ref _totalStreams,
            snapshot.TotalStreams,
            nameof(TotalStreams));
        SetText(
            ref _totalDuration,
            snapshot.TotalDuration,
            nameof(TotalDuration));
        SetText(
            ref _averageViewers,
            snapshot.AverageViewers,
            nameof(AverageViewers));
        SetText(
            ref _peakViewers,
            snapshot.PeakViewers,
            nameof(PeakViewers));
        SetText(
            ref _followers,
            snapshot.Followers,
            nameof(Followers));
        SetText(
            ref _averageDuration,
            snapshot.AverageDuration,
            nameof(AverageDuration));
        Rows = snapshot.Rows;
        Categories = snapshot.Categories;
        Development = snapshot.Development;
    }

    private void SetText(
        ref string field,
        string value,
        string propertyName)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
    }
}
