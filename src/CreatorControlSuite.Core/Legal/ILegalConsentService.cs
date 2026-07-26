namespace CreatorControlSuite.Core.Legal;
public interface ILegalConsentService
{
    Task<LegalConsentState> LoadAsync(CancellationToken cancellationToken=default);
    Task<bool> IsConsentRequiredAsync(CancellationToken cancellationToken=default);
    Task SaveAcceptedAsync(CancellationToken cancellationToken=default);
    IReadOnlyList<LegalDocumentInfo> GetDocuments();
}
