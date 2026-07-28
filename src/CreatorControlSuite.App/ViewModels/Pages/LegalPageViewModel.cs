using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.App.Services;

namespace CreatorControlSuite.App.ViewModels.Pages;

public sealed class LegalPageViewModel : ViewModelBase
{
    private readonly ILegalDocumentLauncher _documents;

    public LegalPageViewModel(ILegalDocumentLauncher documents)
    {
        _documents = documents;
        OpenEulaCommand = new RelayCommand(
            () => OpenDocument("eula"));
        OpenPrivacyCommand = new RelayCommand(
            () => OpenDocument("privacy"));
    }

    public RelayCommand OpenEulaCommand { get; }

    public RelayCommand OpenPrivacyCommand { get; }

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

    public void OpenDocument(string documentId)
    {
        LegalDocumentOpenResult result = _documents.Open(documentId);
        StatusMessage = result.Message;
        StatusIsError = !result.Success;
    }
}
