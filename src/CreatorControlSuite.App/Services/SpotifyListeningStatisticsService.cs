using System.Text.Json;
using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.App.Services;

public sealed class SpotifyListeningStatisticsService
{
    private readonly string _filePath;
    private readonly Dictionary<string, SpotifyTrackStatistic> _tracks = new(StringComparer.Ordinal);
    private string? _activeTrackId;
    private DateTimeOffset _lastSampleAt = DateTimeOffset.Now;

    public SpotifyListeningStatisticsService()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CreatorControlSuite", "Statistics");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "spotify-listening-statistics.json");
        Load();
    }

    public void Observe(SpotifyPlaybackState playback)
    {
        var now = DateTimeOffset.Now;
        var elapsed = Math.Clamp((now - _lastSampleAt).TotalSeconds, 0, 15);
        _lastSampleAt = now;

        if (_activeTrackId is not null && _tracks.TryGetValue(_activeTrackId, out var active) && playback.IsPlaying)
        {
            active.ListeningSeconds += elapsed;
        }

        var track = playback.Track;
        if (track is null)
        {
            _activeTrackId = null;
            Save();
            return;
        }

        if (!_tracks.TryGetValue(track.Id, out var statistic))
        {
            statistic = new SpotifyTrackStatistic
            {
                TrackId = track.Id,
                Title = track.Name,
                Artist = track.Artist,
                Album = track.Album
            };
            _tracks[track.Id] = statistic;
        }

        if (!string.Equals(_activeTrackId, track.Id, StringComparison.Ordinal))
        {
            statistic.PlayCount++;
            statistic.LastPlayedAt = now;
            _activeTrackId = track.Id;
        }

        Save();
    }

    public SpotifyListeningStatisticsSnapshot GetSnapshot()
    {
        var totalSeconds = _tracks.Values.Sum(item => item.ListeningSeconds);
        return new SpotifyListeningStatisticsSnapshot(
            _tracks.Values.Sum(item => item.PlayCount),
            TimeSpan.FromSeconds(totalSeconds),
            _tracks.Values.OrderByDescending(item => item.PlayCount).ThenByDescending(item => item.ListeningSeconds).Take(10).ToList(),
            _tracks.Values.GroupBy(item => item.Artist, StringComparer.OrdinalIgnoreCase)
                .Select(group => new SpotifyArtistStatistic(group.Key, group.Sum(item => item.PlayCount), TimeSpan.FromSeconds(group.Sum(item => item.ListeningSeconds))))
                .OrderByDescending(item => item.PlayCount).ThenByDescending(item => item.ListeningTime).Take(10).ToList());
    }

    public void Reset()
    {
        _tracks.Clear();
        _activeTrackId = null;
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var items = JsonSerializer.Deserialize<List<SpotifyTrackStatistic>>(File.ReadAllText(_filePath)) ?? [];
            foreach (var item in items.Where(item => !string.IsNullOrWhiteSpace(item.TrackId))) _tracks[item.TrackId] = item;
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_tracks.Values.OrderBy(item => item.Artist).ThenBy(item => item.Title), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

public sealed class SpotifyTrackStatistic
{
    public string TrackId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public int PlayCount { get; set; }
    public double ListeningSeconds { get; set; }
    public DateTimeOffset LastPlayedAt { get; set; }
    public string DisplayText => $"{PlayCount}× · {Artist} – {Title} · {TimeSpan.FromSeconds(ListeningSeconds):hh\\:mm\\:ss}";
}

public sealed record SpotifyArtistStatistic(string Artist, int PlayCount, TimeSpan ListeningTime)
{
    public string DisplayText => $"{PlayCount}× · {Artist} · {ListeningTime:hh\\:mm\\:ss}";
}

public sealed record SpotifyListeningStatisticsSnapshot(int TotalPlays, TimeSpan TotalListeningTime, IReadOnlyList<SpotifyTrackStatistic> TopTracks, IReadOnlyList<SpotifyArtistStatistic> TopArtists);
