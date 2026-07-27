using System.Collections.ObjectModel;
using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.Core.Diagnostics;
using CreatorControlSuite.Core.Modules;

namespace CreatorControlSuite.App.ViewModels;

public sealed class DiagnosticsPageViewModel : ViewModelBase, IPageViewModel
{
    private readonly DiagnosticService _diagnostics;
    private readonly IEnumerable<IStreamingModule> _modules;
    private bool _isLoading;
    private string _statusMessage = "Bereit.";

    public DiagnosticsPageViewModel(
        DiagnosticService diagnostics,
        IEnumerable<IStreamingModule> modules)
    {
        _diagnostics = diagnostics;
        _modules = modules;
        RefreshCommand = new AsyncRelayCommand(_ => LoadStatusesAsync());
    }

    public string Key => "diagnostics";
    public string Title => "Protokolle / Diagnose";

    public ObservableCollection<ModuleStatus> ModuleStatuses { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public Task OnNavigatedToAsync(CancellationToken cancellationToken = default)
        => LoadStatusesAsync(cancellationToken);

    public async Task LoadStatusesAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        StatusMessage = "Lade Modulstatus…";
        try
        {
            var statuses = await _diagnostics.RunAsync(cancellationToken);
            ModuleStatuses.Clear();
            foreach (var status in statuses)
                ModuleStatuses.Add(status);

            if (ModuleStatuses.Count == 0)
            {
                foreach (var module in _modules)
                {
                    ModuleStatuses.Add(new ModuleStatus(
                        module.Id,
                        module.DisplayName,
                        ModuleHealth.Unknown,
                        "Noch nicht geprüft",
                        DateTimeOffset.Now));
                }
            }

            StatusMessage = $"{ModuleStatuses.Count} Module geladen.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Diagnose fehlgeschlagen: " + ex.Message;
            throw;
        }
        finally
        {
            IsLoading = false;
        }
    }
}
