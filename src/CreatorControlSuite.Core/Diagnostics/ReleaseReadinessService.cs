using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Legal;
using CreatorControlSuite.Core.Validation;
namespace CreatorControlSuite.Core.Diagnostics;

public sealed class ReleaseReadinessService(
    ISettingsStore s,
    ISettingsValidator v,
    ILegalConsentService l) : IReleaseReadinessService
{
    private readonly ISettingsStore _settings = s; private readonly ISettingsValidator _validator = v; private readonly ILegalConsentService _legal = l;

    public async Task<ReleaseReadinessReport> CheckAsync(CancellationToken ct = default)
    {
        var r = new List<ReleaseReadinessItem>(); ValidationReport v = _validator.Validate(await _settings.LoadAsync(ct)); r.Add(new("Konfiguration", v.IsValid ? "OK" : "Fehler", v.IsValid ? "Konfiguration gültig." : $"{v.Issues.Count} Problem(e).", !v.IsValid));
        bool legal = await _legal.IsConsentRequiredAsync(ct); r.Add(new("Rechtliche Bestätigung", legal ? "Offen" : "OK", legal ? "Dokumentversion nicht bestätigt." : "Bestätigt.", legal));
        bool drafts = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "Legal")) && Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Legal"), "*DRAFT*").Any(); r.Add(new("Rechtstexte", drafts ? "Entwurf" : "OK", drafts ? "DRAFT-Rechtstexte enthalten." : "Keine DRAFT-Dateien.", drafts));
        bool keys = File.Exists(Path.Combine(AppContext.BaseDirectory, "Keys", "update-public.pem")); r.Add(new("Update Public Key", keys ? "OK" : "Fehlt", keys ? "Update Public Key vorhanden." : "Produktiver Update Public Key fehlt.", !keys));
        r.Add(new("Code Signing", "Offen", "Authenticode-Signatur des finalen Releases fehlt.", true)); return new(!r.Any(x => x.Blocking), r);
    }
}
