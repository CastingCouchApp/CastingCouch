using System.Collections.ObjectModel;
using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.App.Services;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Updates;

namespace CreatorControlSuite.App.ViewModels.Pages;

public sealed class UpdatePageViewModel : ViewModelBase
{
    private readonly UpdateWorkflowService _workflow;
    private readonly IUpdateService _updates;
    private readonly Func<string> _currentVersionProvider;
    private UpdatePackage? _pendingPackage;

    public UpdatePageViewModel(
        UpdateWorkflowService workflow,
        IUpdateService updates,
        Func<string> currentVersionProvider)
    {
        _workflow = workflow;
        _updates = updates;
        _currentVersionProvider = currentVersionProvider;
        CheckCommand = new AsyncRelayCommand(_ => CheckAsync());
        InstallCommand = new AsyncRelayCommand(
            _ => InstallAsync(),
            _ => CanInstall);
        CreateBackupCommand =
            new AsyncRelayCommand(_ => CreateBackupAsync());
        RestoreBackupCommand = new AsyncRelayCommand(
            _ => RestoreSelectedBackupAsync(),
            _ => SelectedBackup is not null);
    }

    public IReadOnlyList<string> Channels { get; } =
        ["Stable", "Beta", "Alpha"];

    public ObservableCollection<UpdateBackup> Backups { get; } = [];

    public bool AutoCheck
    {
        get;
        set => SetProperty(ref field, value);
    }

    public bool BackupBeforeUpdate
    {
        get;
        set => SetProperty(ref field, value);
    }

    public string UpdateChannel
    {
        get;
        set => SetProperty(ref field, NormalizeChannel(value));
    } = "Alpha";

    public UpdateBackup? SelectedBackup
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                RestoreBackupCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Bereit.";

    public bool StatusIsError
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool StatusIsSuccess
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool CanInstall
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                InstallCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand CheckCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }
    public AsyncRelayCommand CreateBackupCommand { get; }
    public AsyncRelayCommand RestoreBackupCommand { get; }

    public Func<Task<bool>>? ConfirmRestoreAsync { get; set; }
    public Func<Task>? AfterRestoreAsync { get; set; }
    public Action? ShutdownApplication { get; set; }

    public async Task LoadAsync(
        UpdateSettings settings,
        CancellationToken cancellationToken = default)
    {
        AutoCheck = settings.AutoCheck;
        BackupBeforeUpdate = settings.BackupBeforeUpdate;
        UpdateChannel = settings.Channel;
        _pendingPackage = null;
        CanInstall = false;
        await RefreshBackupsAsync(cancellationToken);
        if (AutoCheck)
        {
            await CheckAsync(silent: true, cancellationToken);
        }
    }

    public void ApplyTo(UpdateSettings settings)
    {
        settings.AutoCheck = AutoCheck;
        settings.BackupBeforeUpdate = BackupBeforeUpdate;
        settings.Channel = NormalizeChannel(UpdateChannel);
    }

    public async Task CheckAsync(
        bool silent = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            CanInstall = false;
            _pendingPackage = null;
            if (!silent)
            {
                SetStatus("Suche nach Updates …");
            }

            UpdateCheckResult result =
                await _workflow.CheckAsync(cancellationToken);
            _pendingPackage = result.Package;
            CanInstall =
                result.UpdateAvailable && result.Package is not null;
            if (CanInstall)
            {
                string notes =
                    string.IsNullOrWhiteSpace(result.Package!.ReleaseNotes)
                        ? ""
                        : " — " + Truncate(
                            result.Package.ReleaseNotes,
                            160);
                SetStatus(
                    $"Update verfügbar: {result.Package.Version} " +
                    $"(aktuell {result.CurrentVersion}){notes}",
                    success: true);
            }
            else
            {
                SetStatus(result.Detail);
            }
        }
        catch (Exception ex)
        {
            _pendingPackage = null;
            CanInstall = false;
            SetStatus(ex.Message, error: true);
        }
    }

    public async Task InstallAsync(
        CancellationToken cancellationToken = default)
    {
        if (_pendingPackage is null)
        {
            SetStatus("Kein Update ausgewählt. Bitte zuerst suchen.");
            return;
        }

        try
        {
            CanInstall = false;
            SetStatus("Update wird heruntergeladen …");
            var progress = new Progress<UpdateWorkflowProgress>(
                item => SetStatus(UpdateWorkflowPresentation.Format(item)));
            UpdateWorkflowResult result = await _workflow.InstallAsync(
                _pendingPackage,
                new UpdateWorkflowOptions(
                    BackupBeforeUpdate,
                    _currentVersionProvider()),
                progress,
                cancellationToken);
            ReplaceBackups(result.Backups);
            ShutdownApplication?.Invoke();
        }
        catch (Exception ex)
        {
            CanInstall = _pendingPackage is not null;
            SetStatus(ex.Message, error: true);
        }
    }

    public async Task CreateBackupAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            UpdateBackup backup = await _updates.CreateBackupAsync(
                _currentVersionProvider(),
                cancellationToken);
            await RefreshBackupsAsync(cancellationToken);
            SetStatus("Backup erstellt: " + backup.Path, success: true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, error: true);
        }
    }

    public async Task RestoreSelectedBackupAsync(
        CancellationToken cancellationToken = default)
    {
        if (SelectedBackup is null ||
            ConfirmRestoreAsync is not null &&
            !await ConfirmRestoreAsync())
        {
            return;
        }

        await _updates.RestoreBackupAsync(
            SelectedBackup.Id,
            cancellationToken);
        if (AfterRestoreAsync is not null)
        {
            await AfterRestoreAsync();
        }

        SetStatus("Backup wurde wiederhergestellt.", success: true);
    }

    private async Task RefreshBackupsAsync(
        CancellationToken cancellationToken)
    {
        ReplaceBackups(await _updates.ListBackupsAsync(cancellationToken));
    }

    private void ReplaceBackups(
        IReadOnlyList<UpdateBackup> backups)
    {
        Backups.Clear();
        foreach (UpdateBackup backup in backups)
        {
            Backups.Add(backup);
        }
    }

    private void SetStatus(
        string message,
        bool error = false,
        bool success = false)
    {
        StatusMessage = message;
        StatusIsError = error;
        StatusIsSuccess = success;
    }

    private static string NormalizeChannel(string? channel) =>
        channel?.Trim() switch
        {
            "Stable" => "Stable",
            "Beta" => "Beta",
            _ => "Alpha"
        };

    private static string Truncate(string value, int maxLength)
    {
        string normalized =
            value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength].TrimEnd() + "…";
    }
}
