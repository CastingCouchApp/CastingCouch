using CreatorControlSuite.App.ViewModels.Pages;
using CreatorControlSuite.Core.Migration;

namespace CreatorControlSuite.Tests;

public sealed class MigrationPageViewModelTests
{
    [Fact]
    public async Task DetectAsync_PopulatesAndSelectsCandidates()
    {
        MigrationCandidate candidate = Candidate();
        var service = new FakeMigrationService { Detected = [candidate] };
        var viewModel = new MigrationPageViewModel(service);

        await viewModel.DetectAsync();

        Assert.Single(viewModel.Candidates);
        Assert.Same(candidate, viewModel.SelectedCandidate);
        Assert.Contains("1 möglicher", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ImportSelectedAsync_ReloadsAndReportsWarnings()
    {
        MigrationCandidate candidate = Candidate();
        var service = new FakeMigrationService
        {
            Detected = [candidate],
            Result = new MigrationResult(
                true,
                candidate.SourcePath,
                ["Einstellungen"],
                ["Alert-Zuordnung prüfen"],
                "Migration abgeschlossen.")
        };
        var viewModel = new MigrationPageViewModel(service);
        bool reloaded = false;
        viewModel.AfterImportAsync = () =>
        {
            reloaded = true;
            return Task.CompletedTask;
        };
        await viewModel.DetectAsync();

        await viewModel.ImportSelectedAsync();

        Assert.True(reloaded);
        Assert.True(viewModel.StatusIsSuccess);
        Assert.Contains("Einstellungen", viewModel.StatusMessage);
        Assert.Contains("Alert-Zuordnung", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ImportSelectedAsync_ReportsServiceFailure()
    {
        MigrationCandidate candidate = Candidate();
        var service = new FakeMigrationService
        {
            Detected = [candidate],
            ImportException = new InvalidOperationException("Importfehler")
        };
        var viewModel = new MigrationPageViewModel(service);
        await viewModel.DetectAsync();

        await viewModel.ImportSelectedAsync();

        Assert.True(viewModel.StatusIsError);
        Assert.Equal("Importfehler", viewModel.StatusMessage);
    }

    private static MigrationCandidate Candidate() => new(
        "LegacyStreamingSuite",
        "/legacy",
        "Bisherige Streaming Suite",
        ["Einstellungen"]);

    private sealed class FakeMigrationService : ILegacyMigrationService
    {
        public IReadOnlyList<MigrationCandidate> Detected { get; set; } = [];
        public MigrationResult Result { get; set; } = new(
            true,
            "/legacy",
            [],
            [],
            "Migration abgeschlossen.");
        public Exception? ImportException { get; set; }

        public Task<IReadOnlyList<MigrationCandidate>> DetectAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Detected);

        public Task<MigrationResult> ImportAsync(
            string sourcePath,
            CancellationToken cancellationToken = default) =>
            ImportException is null
                ? Task.FromResult(Result)
                : Task.FromException<MigrationResult>(ImportException);
    }
}
