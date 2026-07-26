using CreatorControlSuite.Core.Configuration; using CreatorControlSuite.Core.Legal; using CreatorControlSuite.Core.Licensing; using CreatorControlSuite.Core.Validation;
namespace CreatorControlSuite.Core.Diagnostics;
public sealed class ReleaseReadinessService : IReleaseReadinessService
{
 private readonly ISettingsStore _settings;private readonly ISettingsValidator _validator;private readonly ILegalConsentService _legal;private readonly ILicenseService _licenses;
 public ReleaseReadinessService(ISettingsStore s,ISettingsValidator v,ILegalConsentService l,ILicenseService li){_settings=s;_validator=v;_legal=l;_licenses=li;}
 public async Task<ReleaseReadinessReport> CheckAsync(CancellationToken ct=default)
 {
  var r=new List<ReleaseReadinessItem>();var v=_validator.Validate(await _settings.LoadAsync(ct));r.Add(new("Konfiguration",v.IsValid?"OK":"Fehler",v.IsValid?"Konfiguration gültig.":$"{v.Issues.Count} Problem(e).",!v.IsValid));
  var legal=await _legal.IsConsentRequiredAsync(ct);r.Add(new("Rechtliche Bestätigung",legal?"Offen":"OK",legal?"Dokumentversion nicht bestätigt.":"Bestätigt.",legal));
  var lic=await _licenses.GetStatusAsync(ct);r.Add(new("Lizenzsystem",lic.State.ToString(),lic.Detail,!lic.IsUsable));
  var drafts=Directory.Exists(Path.Combine(AppContext.BaseDirectory,"Legal"))&&Directory.GetFiles(Path.Combine(AppContext.BaseDirectory,"Legal"),"*DRAFT*").Any();r.Add(new("Rechtstexte",drafts?"Entwurf":"OK",drafts?"DRAFT-Rechtstexte enthalten.":"Keine DRAFT-Dateien.",drafts));
  var keys=File.Exists(Path.Combine(AppContext.BaseDirectory,"Keys","license-public.pem"))&&File.Exists(Path.Combine(AppContext.BaseDirectory,"Keys","update-public.pem"));r.Add(new("Public Keys",keys?"OK":"Fehlt",keys?"Beide Public Keys vorhanden.":"Produktive Public Keys fehlen.",!keys));
  r.Add(new("Code Signing","Offen","Authenticode-Signatur des finalen Releases fehlt.",true));return new(!r.Any(x=>x.Blocking),r);
 }
}
