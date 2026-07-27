using CreatorControlSuite.Core.Modules;

namespace CreatorControlSuite.Modules.StreamDeck;

public sealed class StreamDeckModule(
    StreamDeckProfileService profileService) : IConnectableModule
{
    private readonly StreamDeckProfileService _profileService = profileService;

    public string Id => "streamdeck";
    public string DisplayName => "Stream Deck";

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<ModuleStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        return Task.FromResult(
            new ModuleStatus(
                Id,
                DisplayName,
                ModuleHealth.Ready,
                "Standardprofil kann exportiert werden.",
                DateTimeOffset.Now));
    }

    public Task<Models.StreamDeckProfilePackage>
        BuildDefaultProfileAsync(
            CancellationToken cancellationToken = default)
    {
        return _profileService.BuildDefaultProfileAsync(
            cancellationToken);
    }
}
