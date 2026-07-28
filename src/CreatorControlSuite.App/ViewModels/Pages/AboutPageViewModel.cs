using System.Reflection;
using CreatorControlSuite.App.Mvvm;

namespace CreatorControlSuite.App.ViewModels.Pages;

public sealed class AboutPageViewModel : ViewModelBase, IPageViewModel
{
    public AboutPageViewModel()
    {
        VersionText = "CastingCouch " + ResolveVersion();
    }

    public string Key => "about";
    public string Title => "Über das Programm";
    public string VersionText { get; }
    public string Description { get; } =
        "Professionelle Streaming-Steuerzentrale für OBS, Twitch, Spotify, Alerts, Overlays und Stream Deck.";

    public Task OnNavigatedToAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    private static string ResolveVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plus = informational.IndexOf('+');
            return plus > 0 ? informational[..plus] : informational;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
