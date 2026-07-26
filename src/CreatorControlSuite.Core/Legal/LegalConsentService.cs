using System.Text.Json;
namespace CreatorControlSuite.Core.Legal;
public sealed class LegalConsentService : ILegalConsentService
{
    public const string CurrentEulaVersion="draft-2026-07-13", CurrentPrivacyVersion="draft-2026-07-13";
    static readonly JsonSerializerOptions Options=new(){WriteIndented=true,PropertyNameCaseInsensitive=true}; readonly string _statePath,_legalRoot;
    public LegalConsentService(string statePath,string legalRoot){_statePath=statePath;_legalRoot=legalRoot;}
    public async Task<LegalConsentState> LoadAsync(CancellationToken ct=default){ if(!File.Exists(_statePath)) return new(); await using var s=File.OpenRead(_statePath); return await JsonSerializer.DeserializeAsync<LegalConsentState>(s,Options,ct)??new(); }
    public async Task<bool> IsConsentRequiredAsync(CancellationToken ct=default){ var s=await LoadAsync(ct); return s.EulaAcceptedAt is null||s.PrivacyAcknowledgedAt is null||s.EulaVersion!=CurrentEulaVersion||s.PrivacyVersion!=CurrentPrivacyVersion; }
    public async Task SaveAcceptedAsync(CancellationToken ct=default){ Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!); var s=new LegalConsentState{EulaVersion=CurrentEulaVersion,EulaAcceptedAt=DateTimeOffset.UtcNow,PrivacyVersion=CurrentPrivacyVersion,PrivacyAcknowledgedAt=DateTimeOffset.UtcNow}; var t=_statePath+".tmp"; await File.WriteAllTextAsync(t,JsonSerializer.Serialize(s,Options),ct); File.Move(t,_statePath,true); }
    public IReadOnlyList<LegalDocumentInfo> GetDocuments()=>new[]{new LegalDocumentInfo("eula",CurrentEulaVersion,"Endbenutzer-Lizenzvereinbarung",Path.Combine(_legalRoot,"EULA-DRAFT.txt")),new LegalDocumentInfo("privacy",CurrentPrivacyVersion,"Datenschutzhinweise",Path.Combine(_legalRoot,"PRIVACY-DRAFT.txt"))};
}
