using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using CreatorControlSuite.Core.Sidecar;

namespace CreatorControlSuite.Tests;

public sealed class SidecarHttpServerTests
{
    [Fact]
    public async Task Health_ReturnsOkJson()
    {
        int port = SidecarHttpServer.GetFreeLoopbackPort();
        await using var server = new SidecarHttpServer(port);
        await server.StartAsync();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        HttpResponseMessage response = await client.GetAsync($"http://127.0.0.1:{port}/sidecar/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task YtmNowPlaying_ReturnsDisconnectedSnapshot()
    {
        int port = SidecarHttpServer.GetFreeLoopbackPort();
        await using var server = new SidecarHttpServer(port);
        await server.StartAsync();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        HttpResponseMessage response = await client.GetAsync($"http://127.0.0.1:{port}/sidecar/ytm/now-playing");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ytmusic", json.GetProperty("provider").GetString());
        Assert.False(json.GetProperty("connected").GetBoolean());
        Assert.False(json.GetProperty("isPlaying").GetBoolean());
        Assert.Equal("", json.GetProperty("title").GetString());
        Assert.Equal("Nicht verbunden", json.GetProperty("statusText").GetString());
    }

    [Fact]
    public async Task WorkflowRun_ReturnsStub()
    {
        int port = SidecarHttpServer.GetFreeLoopbackPort();
        await using var server = new SidecarHttpServer(port);
        await server.StartAsync();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var content = JsonContent.Create(new { command = "workflow.prepare" });
        HttpResponseMessage response = await client.PostAsync($"http://127.0.0.1:{port}/sidecar/workflow/run", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.GetProperty("ok").GetBoolean());
        Assert.Equal("Run-of-Show noch nicht im Sidecar", json.GetProperty("message").GetString());
    }

    [Fact]
    public void ParsePort_ReadsFlagAndEqualsForm()
    {
        Assert.True(SidecarCommandLine.IsSidecarMode(["--sidecar", "--port", "19001"]));
        Assert.Equal(19001, SidecarCommandLine.ParsePort(["--sidecar", "--port", "19001"]));
        Assert.Equal(18765, SidecarCommandLine.ParsePort(["--sidecar"]));
        Assert.Equal(19111, SidecarCommandLine.ParsePort(["--sidecar", "--port=19111"]));
        Assert.False(SidecarCommandLine.IsSidecarMode(["system.ping"]));
    }
}
