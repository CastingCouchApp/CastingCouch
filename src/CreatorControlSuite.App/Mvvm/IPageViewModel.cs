namespace CreatorControlSuite.App.Mvvm;

public interface IPageViewModel
{
    string Key { get; }
    string Title { get; }

    Task OnNavigatedToAsync(CancellationToken cancellationToken = default);
}
