using System.Collections.ObjectModel;
using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.Core.Migration;

namespace CreatorControlSuite.App.ViewModels.Pages;

public sealed class MigrationPageViewModel : ViewModelBase
{
    private readonly ILegacyMigrationService _migration;

    public MigrationPageViewModel(ILegacyMigrationService migration)
    {
        _migration = migration;
        DetectCommand = new AsyncRelayCommand(_ => DetectAsync());
        ImportCommand = new AsyncRelayCommand(
            _ => ImportSelectedAsync(),
            _ => SelectedCandidate is not null);
    }

    public ObservableCollection<MigrationCandidate> Candidates { get; } = [];

    public MigrationCandidate? SelectedCandidate
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                ImportCommand.RaiseCanExecuteChanged();
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

    public AsyncRelayCommand DetectCommand { get; }
    public AsyncRelayCommand ImportCommand { get; }

    public Func<Task>? AfterImportAsync { get; set; }

    public async Task DetectAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<MigrationCandidate> detected =
                await _migration.DetectAsync(cancellationToken);
            Candidates.Clear();
            foreach (MigrationCandidate candidate in detected)
            {
                Candidates.Add(candidate);
            }

            SelectedCandidate = Candidates.FirstOrDefault();
            SetStatus(
                Candidates.Count == 0
                    ? "Keine alte Suite automatisch gefunden."
                    : $"{Candidates.Count} möglicher Installationsordner gefunden.");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, error: true);
        }
    }

    public async Task ImportSelectedAsync(
        CancellationToken cancellationToken = default)
    {
        if (SelectedCandidate is null)
        {
            return;
        }

        try
        {
            MigrationResult result = await _migration.ImportAsync(
                SelectedCandidate.SourcePath,
                cancellationToken);
            if (result.Success && AfterImportAsync is not null)
            {
                await AfterImportAsync();
            }

            string imported = result.ImportedItems.Count == 0
                ? "Keine"
                : string.Join(", ", result.ImportedItems);
            string warnings = result.Warnings.Count == 0
                ? ""
                : "\nHinweise: " + string.Join(" | ", result.Warnings);
            SetStatus(
                $"{result.Detail}\nImportiert: {imported}{warnings}",
                error: !result.Success,
                success: result.Success);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, error: true);
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
}
