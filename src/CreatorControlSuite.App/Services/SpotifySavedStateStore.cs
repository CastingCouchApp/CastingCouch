using CreatorControlSuite.Modules.Spotify.Models;

namespace CreatorControlSuite.App.Services;

/// <summary>
/// In-memory Spotify playback snapshots for timed automation restore (no UI).
/// </summary>
internal sealed class SpotifySavedStateStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, SpotifyAutomationSavedState> _states = new(StringComparer.OrdinalIgnoreCase);

    public int TtlMinutes { get; set; } = 12 * 60;

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _states.Count;
            }
        }
    }

    public bool ContainsKey(string group)
    {
        lock (_sync)
        {
            return _states.ContainsKey(group);
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _states.Clear();
        }
    }

    public IReadOnlyDictionary<string, SpotifyAutomationSavedState> Snapshot()
    {
        lock (_sync)
        {
            return new Dictionary<string, SpotifyAutomationSavedState>(_states, StringComparer.OrdinalIgnoreCase);
        }
    }

    public bool TryGet(string group, out SpotifyAutomationSavedState? state)
    {
        lock (_sync)
        {
            return _states.TryGetValue(group, out state);
        }
    }

    public void Set(string group, SpotifyAutomationSavedState state)
    {
        lock (_sync)
        {
            _states[group] = state;
        }
    }

    public bool Remove(string group)
    {
        lock (_sync)
        {
            return _states.Remove(group);
        }
    }

    public int DiscardExpired(out IReadOnlyList<string> removedGroups)
    {
        lock (_sync)
        {
            List<string> expired = [.. _states.Where(entry => IsExpired(entry.Value)).Select(entry => entry.Key)];
            foreach (string group in expired)
            {
                _states.Remove(group);
            }

            removedGroups = expired;
            return expired.Count;
        }
    }

    public bool IsExpired(SpotifyAutomationSavedState state) =>
        DateTimeOffset.UtcNow - state.SavedAtUtc > TimeSpan.FromMinutes(Math.Clamp(TtlMinutes, 1, 7 * 24 * 60));

    public static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age.TotalMinutes < 1)
        {
            return "weniger als einer Minute";
        }

        if (age.TotalHours < 1)
        {
            int minutes = Math.Max(1, (int)age.TotalMinutes);
            return minutes == 1 ? "1 Minute" : $"{minutes} Minuten";
        }

        if (age.TotalDays < 1)
        {
            int hours = Math.Max(1, (int)age.TotalHours);
            return hours == 1 ? "1 Stunde" : $"{hours} Stunden";
        }

        int days = Math.Max(1, (int)age.TotalDays);
        return days == 1 ? "1 Tag" : $"{days} Tagen";
    }
}

internal sealed record SpotifyAutomationSavedState(
    string ContextUri,
    SpotifyTrack? Track,
    int ProgressMs,
    int VolumePercent,
    bool ShuffleEnabled,
    string RepeatMode,
    bool WasPlaying,
    DateTimeOffset SavedAtUtc);

internal sealed record SpotifySavedStateOverviewItem(string Group, string Summary, bool IsExpired);
