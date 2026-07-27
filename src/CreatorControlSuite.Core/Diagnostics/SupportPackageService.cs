using System.IO.Compression;
using System.Text.Json;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Logging;
namespace CreatorControlSuite.Core.Diagnostics;

public sealed class SupportPackageService(string root, ISettingsStore settings, IAppLogger logger, RuntimeHealthService health) : ISupportPackageService
{
    private readonly string _root = root; private readonly ISettingsStore _settings = settings; private readonly IAppLogger _logger = logger; private readonly RuntimeHealthService _health = health;

    public async Task<SupportPackageResult> CreateAsync(string target, SupportPackageOptions o, CancellationToken ct = default)
    {
        var inc = new List<string>(); var warn = new List<string>(); string stage = Path.Combine(Path.GetTempPath(), "CCS.Support." + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
        try
        {
            if (o.IncludeSettings) { AppSettings s = await _settings.LoadAsync(ct); var clean = new { s.Product, s.Branding, s.Obs, Twitch = new { s.Twitch.ChannelName, s.Twitch.AutoConnect, s.Twitch.EnableChat, s.Twitch.EnableEventSub }, Spotify = new { s.Spotify.AutoConnect, s.Spotify.RedirectUri, s.Spotify.PreferredDeviceId, s.Spotify.StartPlaylistUri, s.Spotify.StartVolumePercent }, s.Alerts, s.Overlay, s.Workflow, s.StreamDeck, s.Updates }; await File.WriteAllTextAsync(Path.Combine(stage, "settings-sanitized.json"), JsonSerializer.Serialize(clean, new JsonSerializerOptions { WriteIndented = true }), ct); inc.Add("Bereinigte Einstellungen"); }
            if (o.IncludeLogs) { await _logger.ExportAsync(Path.Combine(stage, "logs.txt"), ct); inc.Add("Logs"); }
            if (o.IncludeDiagnostics) { IReadOnlyList<RuntimeHealthItem> h = await _health.CheckAsync(ct); await File.WriteAllTextAsync(Path.Combine(stage, "runtime-health.json"), JsonSerializer.Serialize(h, new JsonSerializerOptions { WriteIndented = true }), ct); inc.Add("Laufzeitdiagnose"); }
            Copy(Path.Combine(_root, "CrashReports"), Path.Combine(stage, "CrashReports"), o.IncludeCrashReports, warn, inc, "Crashberichte");
            Copy(Path.Combine(_root, "Profiles"), Path.Combine(stage, "Profiles"), o.IncludeProfiles, warn, inc, "Profile");
            if (o.IncludeOverlayData)
            {
                string f = Path.Combine(_root, "Overlay", "data", "overlay-data.json"); if (File.Exists(f)) { Directory.CreateDirectory(Path.Combine(stage, "Overlay")); File.Copy(f, Path.Combine(stage, "Overlay", "overlay-data.json"), true); inc.Add("Overlay-Daten"); }
                else
                {
                    warn.Add("overlay-data.json wurde nicht gefunden.");
                }
            }
            await File.WriteAllTextAsync(Path.Combine(stage, "support-package-info.txt"), "Creator Control Suite Supportpaket\nVersion: 2.0.81\nOAuth-Tokens und DPAPI-Secrets werden nicht exportiert.", ct);
            if (File.Exists(target))
            {
                File.Delete(target);
            }

            ZipFile.CreateFromDirectory(stage, target, CompressionLevel.Optimal, false); return new(target, DateTimeOffset.Now, inc, warn);
        }
        finally { try { Directory.Delete(stage, true); } catch { } }
    }
    private static void Copy(string src, string dst, bool enabled, ICollection<string> w, ICollection<string> i, string label) { if (!enabled) { return; } if (!Directory.Exists(src)) { w.Add("Ordner fehlt: " + src); return; } Directory.CreateDirectory(dst); foreach (string f in Directory.GetFiles(src, "*", SearchOption.AllDirectories)) { string d = Path.Combine(dst, Path.GetRelativePath(src, f)); Directory.CreateDirectory(Path.GetDirectoryName(d)!); File.Copy(f, d, true); } i.Add(label); }
}
