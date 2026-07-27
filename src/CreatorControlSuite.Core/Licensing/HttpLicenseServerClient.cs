using System.Net.Http.Json;
namespace CreatorControlSuite.Core.Licensing;

public sealed class HttpLicenseServerClient(HttpClient http) : ILicenseServerClient
{
    private readonly HttpClient _http = http;

    public async Task<LicenseServerActivationResponse> ActivateAsync(LicenseServerActivationRequest r, CancellationToken ct = default) { using HttpResponseMessage x = await _http.PostAsJsonAsync("api/v1/licenses/activate", r, ct); return await x.Content.ReadFromJsonAsync<LicenseServerActivationResponse>(cancellationToken: ct) ?? new(false, "Keine gültige Antwort.", null, null); }
    public async Task<LicenseServerStatusResponse> CheckStatusAsync(string a, string i, CancellationToken ct = default) { using HttpResponseMessage x = await _http.GetAsync("api/v1/licenses/status/" + Uri.EscapeDataString(a) + "?installationId=" + Uri.EscapeDataString(i), ct); return await x.Content.ReadFromJsonAsync<LicenseServerStatusResponse>(cancellationToken: ct) ?? new(false, "Keine gültige Antwort.", false, DateTimeOffset.UtcNow); }
    public async Task DeactivateAsync(LicenseServerDeactivationRequest r, CancellationToken ct = default) { using HttpResponseMessage x = await _http.PostAsJsonAsync("api/v1/licenses/deactivate", r, ct); x.EnsureSuccessStatusCode(); }
}
