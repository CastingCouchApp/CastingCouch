using System.Collections.Concurrent;
namespace CreatorControlSuite.Core.Licensing;
public sealed class LocalLicenseServerMock : ILicenseServerClient
{
    private readonly ConcurrentDictionary<string, Activation> _activations = new();
    public Task<LicenseServerActivationResponse> ActivateAsync(LicenseServerActivationRequest request, CancellationToken cancellationToken = default)
    {
        var edition = request.LicenseKey.StartsWith("PRO-", StringComparison.OrdinalIgnoreCase) ? "Pro" : request.LicenseKey.StartsWith("CREATOR-", StringComparison.OrdinalIgnoreCase) ? "Creator" : request.LicenseKey.StartsWith("CORE-", StringComparison.OrdinalIgnoreCase) ? "Core" : "";
        if (edition.Length == 0) return Task.FromResult(new LicenseServerActivationResponse(false,"Mock-Lizenzschlüssel ist ungültig.",null,null));
        var id = Guid.NewGuid().ToString("N");
        _activations[id] = new Activation(request.InstallationId, edition);
        return Task.FromResult(new LicenseServerActivationResponse(true,$"Mock-Aktivierung erfolgreich: {edition}",null,id));
    }
    public Task<LicenseServerStatusResponse> CheckStatusAsync(string activationId, string installationId, CancellationToken cancellationToken = default)
    {
        var ok = _activations.TryGetValue(activationId, out var a) && a.InstallationId == installationId;
        return Task.FromResult(new LicenseServerStatusResponse(ok, ok ? "Aktivierung aktiv." : "Aktivierung nicht gefunden.", false, DateTimeOffset.UtcNow));
    }
    public Task DeactivateAsync(LicenseServerDeactivationRequest request, CancellationToken cancellationToken = default) { _activations.TryRemove(request.ActivationId, out _); return Task.CompletedTask; }
    private sealed record Activation(string InstallationId, string Edition);
}
