using System.Diagnostics;
using CreatorControlSuite.Core.Legal;

namespace CreatorControlSuite.App.Services;

public sealed class LegalDocumentLauncher(
    ILegalConsentService legalConsentService) : ILegalDocumentLauncher
{
    private readonly ILegalConsentService _legalConsentService =
        legalConsentService;

    public LegalDocumentOpenResult Open(string documentId)
    {
        LegalDocumentInfo? document = _legalConsentService
            .GetDocuments()
            .FirstOrDefault(candidate =>
                string.Equals(
                    candidate.Id,
                    documentId,
                    StringComparison.OrdinalIgnoreCase));
        if (document is null || !File.Exists(document.FilePath))
        {
            return new(false, "Dokument wurde nicht gefunden.");
        }

        try
        {
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = document.FilePath,
                    UseShellExecute = true
                });
            return new(true, $"{document.Title} wurde geöffnet.");
        }
        catch (Exception exception)
        {
            return new(
                false,
                "Dokument konnte nicht geöffnet werden: "
                + exception.Message);
        }
    }
}
