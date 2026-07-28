using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Legal;
using CreatorControlSuite.Core.Validation;
namespace CreatorControlSuite.Core.Diagnostics;

public sealed class InstallerSelfTestService(ISettingsStore settings, ISettingsValidator validator, ILegalConsentService legal, string dataRoot) : IInstallerSelfTestService
{
    private readonly ISettingsStore _settings = settings; private readonly ISettingsValidator _validator = validator;
    private readonly ILegalConsentService _legal = legal; private readonly string _dataRoot = dataRoot;

    public async Task<InstallerSelfTestReport> RunAsync(CancellationToken ct = default)
    {
        DateTimeOffset started = DateTimeOffset.Now; var items = new List<InstallerSelfTestItem>();
        FileCheck(items, "Hauptprogramm", "CreatorControlSuite.App.exe");
        FileCheck(items, "CommandClient", "CreatorControlSuite.CommandClient.exe");
        FileCheck(items, "Updater", "CreatorControlSuite.Updater.exe");
        DirCheck(items, "Legal-Ordner", "Legal"); DirCheck(items, "Keys-Ordner", "Keys");
        CheckWritableDirectory(items, "Lokaler Datenordner", _dataRoot);
        ValidationReport validation = _validator.Validate(await _settings.LoadAsync(ct));
        items.Add(new("Konfiguration", validation.IsValid ? InstallerSelfTestStatus.Passed : InstallerSelfTestStatus.Failed,
            validation.IsValid ? "Konfiguration ist gültig." : $"{validation.Issues.Count} Problem(e) erkannt.",
            validation.IsValid ? "" : "Systemdiagnose → Konfiguration öffnen."));
        bool legal = await _legal.IsConsentRequiredAsync(ct);
        items.Add(new("Rechtliche Bestätigung", legal ? InstallerSelfTestStatus.Warning : InstallerSelfTestStatus.Passed,
            legal ? "Aktuelle Rechtstexte wurden noch nicht bestätigt." : "Bestätigung ist aktuell.",
            legal ? "EULA und Datenschutzhinweise bestätigen." : ""));
        return new(started, DateTimeOffset.Now, !items.Any(x => x.Status == InstallerSelfTestStatus.Failed), items);
    }
    private static void FileCheck(ICollection<InstallerSelfTestItem> items, string name, string file)
    {
        string path = Path.Combine(AppContext.BaseDirectory, file); bool ok = File.Exists(path);
        items.Add(new(name, ok ? InstallerSelfTestStatus.Passed : InstallerSelfTestStatus.Failed,
            ok ? path : "Datei fehlt: " + path, ok ? "" : "Release-Build und Installer-Dateierfassung prüfen."));
    }
    private static void DirCheck(ICollection<InstallerSelfTestItem> items, string name, string dir)
    {
        string path = Path.Combine(AppContext.BaseDirectory, dir); bool ok = Directory.Exists(path);
        items.Add(new(name, ok ? InstallerSelfTestStatus.Passed : InstallerSelfTestStatus.Failed,
            ok ? path : "Ordner fehlt: " + path, ok ? "" : "Installer-Inhalt prüfen."));
    }
    private static void CheckWritableDirectory(ICollection<InstallerSelfTestItem> items, string name, string path)
    {
        try
        {
            Directory.CreateDirectory(path); string probe = Path.Combine(path, ".write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "ok"); File.Delete(probe);
            items.Add(new(name, InstallerSelfTestStatus.Passed, "Schreibzugriff vorhanden: " + path, ""));
        }
        catch (Exception ex) { items.Add(new(name, InstallerSelfTestStatus.Failed, ex.Message, "Berechtigungen des Benutzerprofils prüfen.")); }
    }
}
