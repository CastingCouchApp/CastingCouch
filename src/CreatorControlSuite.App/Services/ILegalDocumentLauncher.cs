namespace CreatorControlSuite.App.Services;

public interface ILegalDocumentLauncher
{
    LegalDocumentOpenResult Open(string documentId);
}

public sealed record LegalDocumentOpenResult(
    bool Success,
    string Message);
