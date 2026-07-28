using System.Net;
using System.Net.Http;
using CreatorControlSuite.Modules.Twitch;
using CreatorControlSuite.Modules.Twitch.Models;

namespace CreatorControlSuite.Tests;

public sealed class TwitchApiContractTests
{
    [Fact]
    [Trait("Category", "Contract")]
    public async Task CurrentUser_MapsHelixSchemaAndRequiredHeaders()
    {
        var handler = new FixtureHandler("users.json");
        var client = CreateClient(handler);

        TwitchUser user = await client.GetCurrentUserAsync();

        RequestSnapshot request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://api.twitch.tv/helix/users", request.Uri);
        Assert.Equal("Bearer contract-token", request.Authorization);
        Assert.Equal("contract-client", request.ClientId);
        Assert.Equal("141981764", user.Id);
        Assert.Equal("twitchdev", user.Login);
        Assert.Equal("TwitchDev", user.DisplayName);
        Assert.Equal(
            "https://static-cdn.jtvnw.net/profile.png",
            user.ProfileImageUrl);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task FollowerCount_UsesCurrentQueryContract()
    {
        var handler = new FixtureHandler("followers-total.json");
        var client = CreateClient(handler);

        int count = await client.GetFollowerCountAsync(
            "broadcaster / id");

        RequestSnapshot request = Assert.Single(handler.Requests);
        Assert.Equal(812, count);
        Assert.EndsWith(
            "channels/followers?broadcaster_id=broadcaster%20%2F%20id&first=1",
            request.Uri);
        Assert.DoesNotContain("moderator_id", request.Uri);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task Chatters_FollowCursorPagingAndNormalizeNames()
    {
        var handler = new FixtureHandler(
            "chatters-page-1.json",
            "chatters-page-2.json");
        var client = CreateClient(handler);

        IReadOnlyList<string> chatters = await client.GetChattersAsync(
            "broadcaster",
            "moderator");

        Assert.Equal(2, handler.Requests.Count);
        Assert.EndsWith(
            "chat/chatters?broadcaster_id=broadcaster&moderator_id=moderator&first=1000",
            handler.Requests[0].Uri);
        Assert.EndsWith(
            "chat/chatters?broadcaster_id=broadcaster&moderator_id=moderator&first=1000&after=next%2F%2B%3D%3D",
            handler.Requests[1].Uri);
        Assert.Equal(["alpha", "Beta", "Zeta"], chatters);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task DroppedChatMessage_ExposesHelixReason()
    {
        var handler = new FixtureHandler("chat-message-dropped.json");
        var client = CreateClient(handler);

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.SendChatMessageAsync(
                    "broadcaster",
                    "sender",
                    "Hello"));

        Assert.Contains("held for review", error.Message);
        RequestSnapshot request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("chat/messages", request.Uri);
        Assert.Contains("\"broadcaster_id\":\"broadcaster\"", request.Body);
        Assert.Contains("\"sender_id\":\"sender\"", request.Body);
        Assert.Contains("\"message\":\"Hello\"", request.Body);
    }

    [Fact]
    [Trait("Category", "Contract")]
    public async Task HelixError_PreservesStatusAndResponseDetails()
    {
        var handler = new FixtureHandler(
            new FixtureResponse(
                ReadFixture("error.json"),
                HttpStatusCode.Unauthorized));
        var client = CreateClient(handler);

        InvalidOperationException error =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => client.GetCurrentUserAsync());

        Assert.Contains("Twitch API 401", error.Message);
        Assert.Contains("OAuth token is not valid", error.Message);
    }

    private static TwitchApiClient CreateClient(FixtureHandler handler)
    {
        var client = new TwitchApiClient(new HttpClient(handler));
        client.Configure("contract-client", "contract-token");
        return client;
    }

    private sealed class FixtureHandler : HttpMessageHandler
    {
        private readonly Queue<FixtureResponse> _responses;

        public FixtureHandler(params string[] fixtureNames)
            : this(fixtureNames.Select(name =>
                new FixtureResponse(ReadFixture(name))).ToArray())
        {
        }

        public FixtureHandler(params FixtureResponse[] responses)
        {
            _responses = new Queue<FixtureResponse>(responses);
        }

        public List<RequestSnapshot> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RequestSnapshot(
                request.Method,
                request.RequestUri?.AbsoluteUri ?? "",
                request.Headers.Authorization?.ToString() ?? "",
                request.Headers.TryGetValues("Client-Id", out var values)
                    ? values.Single()
                    : "",
                body));
            FixtureResponse fixture = _responses.Dequeue();
            return new HttpResponseMessage(fixture.StatusCode)
            {
                Content = new StringContent(
                    fixture.Content,
                    System.Text.Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed record FixtureResponse(
        string Content,
        HttpStatusCode StatusCode = HttpStatusCode.OK);

    private sealed record RequestSnapshot(
        HttpMethod Method,
        string Uri,
        string Authorization,
        string ClientId,
        string Body);

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "twitch",
            name));
}
