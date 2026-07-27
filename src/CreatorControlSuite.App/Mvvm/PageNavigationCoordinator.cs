using CreatorControlSuite.App.Mvvm;
using CreatorControlSuite.App.ViewModels.Pages;

namespace CreatorControlSuite.App.Mvvm;

/// <summary>
/// Routes <see cref="INavigationService.PageChanged"/> to registered page view-models.
/// </summary>
public sealed class PageNavigationCoordinator
{
    private readonly Dictionary<string, IPageViewModel> _pages = new(StringComparer.OrdinalIgnoreCase);

    public PageNavigationCoordinator(INavigationService navigation, IEnumerable<IPageViewModel> pages)
    {
        foreach (IPageViewModel page in pages)
        {
            _pages[page.Key] = page;
        }

        navigation.PageChanged += async (_, key) =>
        {
            if (_pages.TryGetValue(key, out IPageViewModel? page))
            {
                await page.OnNavigatedToAsync();
            }
        };
    }

    public T GetRequired<T>() where T : class, IPageViewModel =>
        _pages.Values.OfType<T>().FirstOrDefault()
        ?? throw new InvalidOperationException($"Page view-model {typeof(T).Name} is not registered.");
}
