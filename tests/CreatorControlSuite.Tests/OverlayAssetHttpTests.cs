using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using CreatorControlSuite.Modules.Overlay;
using CreatorControlSuite.Modules.Overlay.Assets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace CreatorControlSuite.Tests;

public sealed class OverlayAssetHttpTests
{
    [Fact]
    public async Task GetAssets_ReturnsCatalog()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayAssetStore(root);
            await using (var stream = new MemoryStream(MinimalPng()))
            {
                await store.ImportAsync(stream, "a.png");
            }

            await using TestHost host = await TestHost.StartAsync(store);
            using HttpResponseMessage response = await host.Client.GetAsync("/assets");
            response.EnsureSuccessStatusCode();
            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(1, doc.RootElement.GetProperty("assets").GetArrayLength());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task GetAssetById_ReturnsFile()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayAssetStore(root);
            OverlayAssetInfo asset;
            await using (var stream = new MemoryStream(MinimalPng()))
            {
                asset = await store.ImportAsync(stream, "a.png");
            }

            await using TestHost host = await TestHost.StartAsync(store);
            using HttpResponseMessage response = await host.Client.GetAsync(asset.PublicUrl);
            response.EnsureSuccessStatusCode();
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(MinimalPng(), await response.Content.ReadAsByteArrayAsync());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task PostAssets_ImportsOnLoopback()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayAssetStore(root);
            await using TestHost host = await TestHost.StartAsync(store);

            using var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(MinimalPng());
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(file, "file", "upload.png");

            using HttpResponseMessage response = await host.Client.PostAsync("/assets", content);
            response.EnsureSuccessStatusCode();
            using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("upload.png", doc.RootElement.GetProperty("name").GetString());
            Assert.Single(store.List());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task DeleteAssets_RemovesOnLoopback()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayAssetStore(root);
            OverlayAssetInfo asset;
            await using (var stream = new MemoryStream(MinimalPng()))
            {
                asset = await store.ImportAsync(stream, "a.png");
            }

            await using TestHost host = await TestHost.StartAsync(store);
            using HttpResponseMessage response = await host.Client.DeleteAsync("/assets/" + asset.Id);
            response.EnsureSuccessStatusCode();
            Assert.Empty(store.List());
        }
        finally
        {
            Cleanup(root);
        }
    }

    [Fact]
    public async Task PostAssets_ForbiddenWhenMutationsDisallowed()
    {
        string root = CreateTempRoot();
        try
        {
            var store = new OverlayAssetStore(root);
            await using TestHost host = await TestHost.StartAsync(store, allowMutations: _ => false);

            using var content = new MultipartFormDataContent();
            var file = new ByteArrayContent(MinimalPng());
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(file, "file", "upload.png");

            using HttpResponseMessage response = await host.Client.PostAsync("/assets", content);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Empty(store.List());
        }
        finally
        {
            Cleanup(root);
        }
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "ccs-assets-http-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Cleanup(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static byte[] MinimalPng() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x00, 0x03, 0x00, 0x01, 0x00, 0x05, 0xFE, 0xD4, 0xEF, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45,
        0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
    ];

    private sealed class TestHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public HttpClient Client { get; }

        private TestHost(WebApplication app, HttpClient client)
        {
            _app = app;
            Client = client;
        }

        public static async Task<TestHost> StartAsync(
            IOverlayAssetStore store,
            Func<HttpContext, bool>? allowMutations = null)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
            WebApplication app = builder.Build();
            OverlayAssetHttp.MapRoutes(app, store, allowMutations);
            await app.StartAsync();
            string url = app.Urls.First();
            var client = new HttpClient { BaseAddress = new Uri(url) };
            return new TestHost(app, client);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }
}
