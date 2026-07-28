using System.Collections.ObjectModel;
using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.App.Services;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.App.ViewModels.Pages;

public sealed record AlertLibraryItem(
    string Type,
    bool Enabled)
{
    public string DisplayName =>
        $"{(Enabled ? "●" : "○")} {Type}";
}

public sealed class AlertLibraryPageViewModel : ViewModelBase
{
    private readonly IAlertDefinitionApplicationService _service;
    private AppSettings? _settings;
    private bool _loading;

    public AlertLibraryPageViewModel(
        IAlertDefinitionApplicationService service)
    {
        _service = service;
        CreateCommand = new AsyncRelayCommand(_ => CreateAsync());
        DuplicateCommand = new AsyncRelayCommand(
            _ => DuplicateAsync(),
            _ => SelectedItem is not null);
        ToggleCommand = new AsyncRelayCommand(
            _ => ToggleAsync(),
            _ => SelectedItem is not null);
        DeleteCommand = new AsyncRelayCommand(
            _ => DeleteAsync(),
            _ => SelectedItem is not null);
    }

    public ObservableCollection<AlertLibraryItem> Items { get; } = [];

    public ObservableCollection<string> Types { get; } = [];

    public AlertLibraryItem? SelectedItem
    {
        get;
        set
        {
            if (!SetProperty(ref field, value))
            {
                return;
            }

            SelectedType = value?.Type;
            RaiseCommandStates();
            if (!_loading && value is not null)
            {
                _ = NotifySelectionSafelyAsync(value.Type);
            }
        }
    }

    public string? SelectedType
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string Status
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Bereit.";

    public AsyncRelayCommand CreateCommand { get; }
    public AsyncRelayCommand DuplicateCommand { get; }
    public AsyncRelayCommand ToggleCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }

    public Func<Task>? BeforeDuplicateRequestedAsync { get; set; }

    public Func<string, Task>? SelectionChangedAsync { get; set; }

    public Func<string, Task<bool>>? ConfirmDeleteRequestedAsync { get; set; }

    public Action<string>? ErrorRequested { get; set; }

    public void Load(
        AppSettings settings,
        string? preferredType = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        Refresh(
            preferredType ??
            (settings.Alerts.Definitions.ContainsKey("Follow")
                ? "Follow"
                : settings.Alerts.Definitions.Keys.FirstOrDefault()));
    }

    public void Select(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return;
        }

        SelectedItem = Items.FirstOrDefault(item =>
            string.Equals(
                item.Type,
                type,
                StringComparison.OrdinalIgnoreCase));
    }

    private async Task CreateAsync()
    {
        if (_settings is null)
        {
            return;
        }

        await RunSafelyAsync(
            async () =>
            {
                AlertDefinitionSettings definition =
                    await _service.CreateAsync(
                        _settings,
                        "Eigener Alert");
                Refresh(definition.Type);
                Status = $"{definition.Type} wurde angelegt.";
                await NotifySelectionSafelyAsync(definition.Type);
            });
    }

    private async Task DuplicateAsync()
    {
        if (_settings is null || SelectedItem is null)
        {
            return;
        }

        string sourceType = SelectedItem.Type;
        await RunSafelyAsync(
            async () =>
            {
                if (BeforeDuplicateRequestedAsync is not null)
                {
                    await BeforeDuplicateRequestedAsync();
                }

                AlertDefinitionSettings definition =
                    await _service.DuplicateAsync(
                        _settings,
                        sourceType);
                Refresh(definition.Type);
                Status = $"{definition.Type} wurde erstellt.";
                await NotifySelectionSafelyAsync(definition.Type);
            });
    }

    private async Task ToggleAsync()
    {
        if (_settings is null || SelectedItem is null)
        {
            return;
        }

        string type = SelectedItem.Type;
        await RunSafelyAsync(
            async () =>
            {
                AlertDefinitionSettings definition =
                    await _service.ToggleAsync(_settings, type);
                Refresh(type);
                Status = definition.Enabled
                    ? "Alert ist aktiv."
                    : "Alert ist deaktiviert.";
            });
    }

    private async Task DeleteAsync()
    {
        if (_settings is null || SelectedItem is null)
        {
            return;
        }

        string type = SelectedItem.Type;
        if (ConfirmDeleteRequestedAsync is null ||
            !await ConfirmDeleteRequestedAsync(type))
        {
            return;
        }

        await RunSafelyAsync(
            async () =>
            {
                await _service.DeleteAsync(_settings, type);
                Refresh();
                Status = "Alert wurde gelöscht.";
                if (SelectedType is not null)
                {
                    await NotifySelectionSafelyAsync(SelectedType);
                }
            });
    }

    private void Refresh(string? selectedType = null)
    {
        if (_settings is null)
        {
            return;
        }

        string? selection = selectedType ?? SelectedType;
        string[] types =
        [
            .. _settings.Alerts.Definitions.Keys.OrderBy(
                type => type,
                StringComparer.OrdinalIgnoreCase)
        ];

        _loading = true;
        try
        {
            Types.Clear();
            Items.Clear();
            foreach (string type in types)
            {
                Types.Add(type);
                Items.Add(new AlertLibraryItem(
                    type,
                    _settings.Alerts.Definitions[type].Enabled));
            }

            SelectedItem = Items.FirstOrDefault(item =>
                               string.Equals(
                                   item.Type,
                                   selection,
                                   StringComparison.OrdinalIgnoreCase))
                           ?? Items.FirstOrDefault();
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task NotifySelectionSafelyAsync(string type)
    {
        if (SelectionChangedAsync is null)
        {
            return;
        }

        try
        {
            await SelectionChangedAsync(type);
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
        }
    }

    private async Task RunSafelyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            SetError(exception.Message);
        }
    }

    private void SetError(string message)
    {
        Status = message;
        ErrorRequested?.Invoke(message);
    }

    private void RaiseCommandStates()
    {
        DuplicateCommand.RaiseCanExecuteChanged();
        ToggleCommand.RaiseCanExecuteChanged();
        DeleteCommand.RaiseCanExecuteChanged();
    }
}
