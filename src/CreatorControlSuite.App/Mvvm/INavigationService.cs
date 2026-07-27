namespace CreatorControlSuite.App.Mvvm;

public interface INavigationService
{
    string? CurrentPageKey { get; }

    event EventHandler<string>? PageChanged;

    void Navigate(string pageKey);
}
