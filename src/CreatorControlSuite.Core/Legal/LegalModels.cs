namespace CreatorControlSuite.Core.Legal;
public sealed class LegalConsentState { public string EulaVersion{get;set;}=""; public DateTimeOffset? EulaAcceptedAt{get;set;} public string PrivacyVersion{get;set;}=""; public DateTimeOffset? PrivacyAcknowledgedAt{get;set;} }
public sealed record LegalDocumentInfo(string Id,string Version,string Title,string FilePath);
