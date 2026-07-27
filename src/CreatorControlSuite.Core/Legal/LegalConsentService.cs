using System.Text.Json;
namespace CreatorControlSuite.Core.Legal;

public sealed class LegalConsentService(string statePath, string legalRoot) : ILegalConsentService
{
    public const string CurrentEulaVersion = "draft-2026-07-13", CurrentPrivacyVersion = "draft-2026-07-13";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true }; private readonly string _statePath = statePath, _legalRoot = legalRoot;

    public async Task<LegalConsentState> LoadAsync(CancellationToken ct = default) { if (!File.Exists(_statePath)) { return new(); } await using FileStream s = File.OpenRead(_statePath); return await JsonSerializer.DeserializeAsync<LegalConsentState>(s, Options, ct) ?? new(); }
    public async Task<bool> IsConsentRequiredAsync(CancellationToken ct = default) { LegalConsentState s = await LoadAsync(ct); return s.EulaAcceptedAt is null || s.PrivacyAcknowledgedAt is null || s.EulaVersion != CurrentEulaVersion || s.PrivacyVersion != CurrentPrivacyVersion; }
    public async Task SaveAcceptedAsync(CancellationToken ct = default) { Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!); var s = new LegalConsentState { EulaVersion = CurrentEulaVersion, EulaAcceptedAt = DateTimeOffset.UtcNow, PrivacyVersion = CurrentPrivacyVersion, PrivacyAcknowledgedAt = DateTimeOffset.UtcNow }; string t = _statePath + ".tmp"; await File.WriteAllTextAsync(t, JsonSerializer.Serialize(s, Options), ct); File.Move(t, _statePath, true); }
    public IReadOnlyList<LegalDocumentInfo> GetDocuments() => [new LegalDocumentInfo("eula", CurrentEulaVersion, "Endbenutzer-Lizenzvereinbarung", Path.Combine(_legalRoot, "EULA-DRAFT.txt")), new LegalDocumentInfo("privacy", CurrentPrivacyVersion, "Datenschutzhinweise", Path.Combine(_legalRoot, "PRIVACY-DRAFT.txt"))];
}
