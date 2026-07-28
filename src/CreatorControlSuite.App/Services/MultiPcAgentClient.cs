using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace CreatorControlSuite.App.Services;

public sealed class MultiPcAgentClient : IMultiPcAgentClient
{
    private const string AgentKeyHeader = "X-CCS-Agent-Key";

    public HttpClient CreateClient(
        string host,
        int port,
        string agentKey,
        string? certFingerprint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentKey);

        string expectedFingerprint = (certFingerprint ?? string.Empty).Trim();
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
            {
                if (certificate is null)
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(expectedFingerprint))
                {
                    return false;
                }

                string actual = Convert.ToHexString(
                    SHA256.HashData(certificate.GetRawCertData()));
                return string.Equals(
                    actual,
                    expectedFingerprint,
                    StringComparison.OrdinalIgnoreCase);
            }
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5),
            BaseAddress = new Uri($"https://{host}:{port}/")
        };
        return client;
    }

    public Task<HttpResponseMessage> GetAsync(
        string host,
        int port,
        string agentKey,
        string relativePath,
        string? certFingerprint = null,
        CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Get, host, port, agentKey, relativePath, content: null, certFingerprint, cancellationToken);

    public Task<HttpResponseMessage> PostAsync(
        string host,
        int port,
        string agentKey,
        string relativePath,
        HttpContent? content = null,
        string? certFingerprint = null,
        CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, host, port, agentKey, relativePath, content, certFingerprint, cancellationToken);

    public Task<HttpResponseMessage> GetObsAsync(
        string host,
        int port,
        string agentKey,
        string obsRelativePath,
        string? certFingerprint = null,
        CancellationToken cancellationToken = default)
        => GetAsync(host, port, agentKey, NormalizeObsPath(obsRelativePath), certFingerprint, cancellationToken);

    public Task<HttpResponseMessage> PostObsAsync(
        string host,
        int port,
        string agentKey,
        string obsRelativePath,
        HttpContent? content = null,
        string? certFingerprint = null,
        CancellationToken cancellationToken = default)
        => PostAsync(host, port, agentKey, NormalizeObsPath(obsRelativePath), content, certFingerprint, cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string host,
        int port,
        string agentKey,
        string relativePath,
        HttpContent? content,
        string? certFingerprint,
        CancellationToken cancellationToken)
    {
        using var client = CreateClient(host, port, agentKey, certFingerprint);
        string path = relativePath.TrimStart('/');
        using var request = new HttpRequestMessage(method, path)
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation(AgentKeyHeader, agentKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await client.SendAsync(request, cancellationToken);
    }

    private static string NormalizeObsPath(string obsRelativePath)
    {
        string trimmed = (obsRelativePath ?? string.Empty).Trim().TrimStart('/');
        if (trimmed.StartsWith("api/v1/obs/", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (trimmed.StartsWith("obs/", StringComparison.OrdinalIgnoreCase))
        {
            return "api/v1/" + trimmed;
        }

        return "api/v1/obs/" + trimmed;
    }
}
