using System.Text.Json;

namespace CreatorControlSuite.App.Services;

public sealed class SpotifyAutomationLogService
{
    private readonly object _sync = new();
    private readonly string _path;
    private readonly List<SpotifyAutomationLogEntry> _entries = [];

    public SpotifyAutomationLogService()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CreatorControlSuite", "Logs");
        Directory.CreateDirectory(folder);
        _path = Path.Combine(folder, "spotify-automation.json");
        try
        {
            if (File.Exists(_path))
                _entries.AddRange(JsonSerializer.Deserialize<List<SpotifyAutomationLogEntry>>(File.ReadAllText(_path)) ?? []);
        }
        catch { }
    }

    public void Add(string category, string message, bool success = true)
    {
        lock (_sync)
        {
            _entries.Insert(0, new SpotifyAutomationLogEntry(DateTimeOffset.Now, category, message, success));
            if (_entries.Count > 250) _entries.RemoveRange(250, _entries.Count - 250);
            Save();
        }
    }

    public IReadOnlyList<SpotifyAutomationLogEntry> GetRecent(int count = 50)
    {
        lock (_sync) return _entries.Take(Math.Max(1, count)).ToList();
    }

    public void Clear()
    {
        lock (_sync) { _entries.Clear(); Save(); }
    }

    private void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }
}

public sealed record SpotifyAutomationLogEntry(DateTimeOffset Timestamp, string Category, string Message, bool Success)
{
    public string DisplayText => $"{Timestamp:dd.MM. HH:mm:ss} · {(Success ? "OK" : "FEHLER")} · {Category} · {Message}";
}
