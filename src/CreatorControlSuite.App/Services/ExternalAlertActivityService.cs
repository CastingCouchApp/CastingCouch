namespace CreatorControlSuite.App.Services;

public sealed class ExternalAlertActivityService
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _activeAlerts = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<int>? ActiveCountChanged;

    public int ActiveCount
    {
        get
        {
            lock (_gate)
            {
                return _activeAlerts.Count;
            }
        }
    }

    public void Start(string source, string id)
    {
        string key = BuildKey(source, id);
        int count;
        lock (_gate)
        {
            _activeAlerts.Add(key);
            count = _activeAlerts.Count;
        }
        ActiveCountChanged?.Invoke(this, count);
    }

    public void End(string source, string id)
    {
        string key = BuildKey(source, id);
        int count;
        lock (_gate)
        {
            _activeAlerts.Remove(key);
            count = _activeAlerts.Count;
        }
        ActiveCountChanged?.Invoke(this, count);
    }

    public void ClearSource(string source)
    {
        string prefix = (string.IsNullOrWhiteSpace(source) ? "external" : source.Trim()) + ":";
        int count;
        lock (_gate)
        {
            _activeAlerts.RemoveWhere(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            count = _activeAlerts.Count;
        }
        ActiveCountChanged?.Invoke(this, count);
    }

    private static string BuildKey(string source, string id)
    {
        string normalizedSource = string.IsNullOrWhiteSpace(source) ? "external" : source.Trim();
        string normalizedId = string.IsNullOrWhiteSpace(id) ? "default" : id.Trim();
        return normalizedSource + ":" + normalizedId;
    }
}
