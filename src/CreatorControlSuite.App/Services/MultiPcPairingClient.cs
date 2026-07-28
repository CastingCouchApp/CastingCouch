using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using CreatorControlSuite.Core.Security;

namespace CreatorControlSuite.App.Services;

public sealed record MultiPcPairingResult(
    string DeviceId,
    string MachineName,
    string AgentKey,
    int Port,
    string CertificateFingerprint,
    string Transport,
    string[] AllowedCommands);

public interface IMultiPcPairingClient
{
    Task<MultiPcPairingResult> PairAsync(
        string host,
        int port,
        string code,
        string deviceName,
        string expectedFingerprint,
        CancellationToken cancellationToken = default);
}

public sealed class MultiPcPairingClient : IMultiPcPairingClient
{
    public async Task<MultiPcPairingResult> PairAsync(
        string host,
        int port,
        string code,
        string deviceName,
        string expectedFingerprint,
        CancellationToken cancellationToken = default)
    {
        string trustedFingerprint = CertificateFingerprint.Normalize(expectedFingerprint);
        string? observedFingerprint = null;
        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                observedFingerprint = certificate is null
                    ? null
                    : Convert.ToHexString(SHA256.HashData(certificate.GetRawCertData()));
                return observedFingerprint is not null &&
                    CertificateFingerprint.Matches(trustedFingerprint, observedFingerprint);
            }
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"https://{host}:{port}/api/v1/pair",
            new { code, deviceName },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        MultiPcPairingResult? result =
            await response.Content.ReadFromJsonAsync<MultiPcPairingResult>(
                cancellationToken: cancellationToken);

        if (result is null || string.IsNullOrWhiteSpace(result.AgentKey))
        {
            throw new InvalidDataException(
                "Der Remote-Agent hat keine gültigen Kopplungsdaten geliefert.");
        }

        if (observedFingerprint is null ||
            !CertificateFingerprint.Matches(
                trustedFingerprint,
                result.CertificateFingerprint))
        {
            throw new System.Security.SecurityException(
                "TLS-Fingerprint stimmt nicht mit dem bestätigten Agent-Fingerprint überein.");
        }

        return result with { AllowedCommands = result.AllowedCommands ?? [] };
    }
}
