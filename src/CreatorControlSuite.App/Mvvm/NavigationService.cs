namespace CreatorControlSuite.App.Mvvm;

public sealed class NavigationService : INavigationService
{
    public string? CurrentPageKey { get; private set; }

    public event EventHandler<string>? PageChanged;

    public void Navigate(string pageKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageKey);
        if (string.Equals(CurrentPageKey, pageKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        CurrentPageKey = pageKey;
        PageChanged?.Invoke(this, pageKey);
    }
}
