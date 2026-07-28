using CreatorControlSuite.App.Services;
using CreatorControlSuite.App.ViewModels.Pages;

namespace CreatorControlSuite.Tests;

public sealed class LegalPageViewModelTests
{
    [Fact]
    public void OpenDocument_ReportsSuccessfulLaunch()
    {
        var launcher = new FakeLegalDocumentLauncher(
            new(true, "Datenschutz wurde geöffnet."));
        var viewModel = new LegalPageViewModel(launcher);

        viewModel.OpenDocument("privacy");

        Assert.Equal("privacy", launcher.LastDocumentId);
        Assert.Equal(
            "Datenschutz wurde geöffnet.",
            viewModel.StatusMessage);
        Assert.False(viewModel.StatusIsError);
    }

    [Fact]
    public void OpenDocument_ReportsMissingDocument()
    {
        var launcher = new FakeLegalDocumentLauncher(
            new(false, "Dokument wurde nicht gefunden."));
        var viewModel = new LegalPageViewModel(launcher);

        viewModel.OpenDocument("eula");

        Assert.Equal("eula", launcher.LastDocumentId);
        Assert.Equal(
            "Dokument wurde nicht gefunden.",
            viewModel.StatusMessage);
        Assert.True(viewModel.StatusIsError);
    }

    private sealed class FakeLegalDocumentLauncher(
        LegalDocumentOpenResult result) : ILegalDocumentLauncher
    {
        public string? LastDocumentId { get; private set; }

        public LegalDocumentOpenResult Open(string documentId)
        {
            LastDocumentId = documentId;
            return result;
        }
    }
}
