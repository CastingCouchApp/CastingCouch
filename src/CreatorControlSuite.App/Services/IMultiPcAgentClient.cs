using System.Net.Http;

namespace CreatorControlSuite.App.Services;

public interface IMultiPcAgentClient
{
    HttpClient CreateClient(string host, int port, string agentKey, string? certFingerprint = null);

    Task<HttpResponseMessage> GetAsync(
        string host,
        int port,
        string agentKey,
        string relativePath,
        string? certFingerprint = null,
        CancellationToken cancellationToken = default);

    Task<HttpResponseMessage> PostAsync(
        string host,
        int port,
        string agentKey,
        string relativePath,
        HttpContent? content = null,
        string? certFingerprint = null,
        CancellationToken cancellationToken = default);

    Task<HttpResponseMessage> GetObsAsync(
        string host,
        int port,
        string agentKey,
        string obsRelativePath,
        string? certFingerprint = null,
        CancellationToken cancellationToken = default);

    Task<HttpResponseMessage> PostObsAsync(
        string host,
        int port,
        string agentKey,
        string obsRelativePath,
        HttpContent? content = null,
        string? certFingerprint = null,
        CancellationToken cancellationToken = default);
}
