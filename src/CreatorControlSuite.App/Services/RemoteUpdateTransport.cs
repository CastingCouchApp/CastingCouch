using System.Net.Http;
using System.Net.Http.Json;
using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Core.Updates;

namespace CreatorControlSuite.App.Services;

public sealed class RemoteUpdateTransport(
    IMultiPcAgentClient agentClient) : IRemoteUpdateTransport
{
    public async Task<bool> StageAsync(
        PairedAgentDevice device,
        RemoteUpdatePackage package,
        CancellationToken cancellationToken)
    {
        try
        {
            using HttpClient client = CreateClient(device, TimeSpan.FromMinutes(5));
            using var request = CreateRequest(
                device,
                "api/v1/update/stage",
                JsonContent.Create(new
                {
                    fileName = package.FileName,
                    base64Zip = Convert.ToBase64String(package.Content),
                    manifest = package.Manifest
                }));
            using HttpResponseMessage response =
                await client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public async Task<bool> ExecuteAsync(
        PairedAgentDevice device,
        string action,
        RemoteUpdateActionOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        try
        {
            using HttpClient client = CreateClient(device, TimeSpan.FromMinutes(2));
            using var request = CreateRequest(
                device,
                $"api/v1/update/{action}",
                JsonContent.Create(new
                {
                    restartSuite = options.RestartSuite,
                    automaticRollback = options.AutomaticRollback
                }));
            using HttpResponseMessage response =
                await client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private HttpClient CreateClient(
        PairedAgentDevice device,
        TimeSpan timeout)
    {
        HttpClient client = agentClient.CreateClient(
            device.Host,
            device.AgentPort,
            device.AgentKey,
            device.CertificateFingerprint);
        client.Timeout = timeout;
        return client;
    }

    private static HttpRequestMessage CreateRequest(
        PairedAgentDevice device,
        string path,
        HttpContent content)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = content
        };
        request.Headers.Add("X-CCS-Agent-Key", device.AgentKey);
        return request;
    }
}
