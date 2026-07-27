using CreatorControlSuite.Modules.Overlay;

namespace CreatorControlSuite.Tests;

public sealed class ChatOverlayAssetsTests
{
    [Theory]
    [InlineData("index.html", "text/html")]
    [InlineData("chat.css", "text/css")]
    [InlineData("chat.js", "application/javascript")]
    public void TryGet_ReturnsEmbeddedChatAssets(string fileName, string contentTypePrefix)
    {
        Assert.True(ChatOverlayAssets.TryGet(fileName, out string content, out string contentType));
        Assert.False(string.IsNullOrWhiteSpace(content));
        Assert.StartsWith(contentTypePrefix, contentType, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChatJs_LoadsSessionHistoryAndDedupsByMessageId()
    {
        Assert.True(ChatOverlayAssets.TryGet("chat.js", out string js, out _));
        Assert.Contains("/chat/history", js);
        Assert.Contains("messageId", js);
        Assert.Contains("seenMessageIds", js);
    }
}
