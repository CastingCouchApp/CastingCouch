using CreatorControlSuite.Core.Security;

namespace CreatorControlSuite.Tests;

public sealed class SecretRedactorTests
{
    [Theory]
    [InlineData("""{"password":"hunter2","ok":true}""", "hunter2")]
    [InlineData("""{"agentKey":"ABCDEF","name":"studio"}""", "ABCDEF")]
    [InlineData("Authorization: Bearer access-token-value", "access-token-value")]
    [InlineData("refresh_token=refresh-token-value&state=ok", "refresh-token-value")]
    [InlineData("X-CCS-Agent-Key: device-secret", "device-secret")]
    public void Redact_RemovesKnownSecretShapes(string input, string secret)
    {
        string redacted = SecretRedactor.Redact(input);

        Assert.DoesNotContain(secret, redacted, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_PreservesOrdinaryDiagnosticText()
    {
        const string message = "OBS connection timed out after 5 seconds.";

        Assert.Equal(message, SecretRedactor.Redact(message));
    }
}
