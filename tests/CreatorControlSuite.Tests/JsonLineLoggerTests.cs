using CreatorControlSuite.Core.Logging;

namespace CreatorControlSuite.Tests;

public sealed class JsonLineLoggerTests
{
    [Fact]
    public async Task CanWriteAndReadLog()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "CreatorControlSuite.LoggerTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var logger = new JsonLineAppLogger(root);

            logger.Write(
                AppLogLevel.Information,
                "Test",
                "Hello");

            IReadOnlyList<AppLogEntry> entries = await logger.ReadRecentAsync();

            Assert.Contains(
                entries,
                entry =>
                    entry.Category == "Test" &&
                    entry.Message == "Hello");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WriteDoesNotCrashWhenLogFileIsTemporarilyLocked()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "CreatorControlSuite.LoggerTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            string path = Path.Combine(
                root,
                "suite-" + DateTime.Now.ToString("yyyyMMdd") + ".jsonl");
            using var exclusiveLock = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            var logger = new JsonLineAppLogger(root);

            Exception exception = Record.Exception(() =>
                logger.Write(
                    AppLogLevel.Information,
                    "Test",
                    "Locked log"));

            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Write_RedactsSecretsFromMessageExceptionAndProperties()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "CreatorControlSuite.LoggerTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var logger = new JsonLineAppLogger(root);

            logger.Write(
                AppLogLevel.Error,
                "Security",
                "Authorization: Bearer message-token",
                new InvalidOperationException("""{"password":"exception-secret"}"""),
                new Dictionary<string, string>
                {
                    ["agentKey"] = "property-secret",
                    ["endpoint"] = "https://localhost/?access_token=query-secret"
                });

            AppLogEntry entry = Assert.Single(await logger.ReadRecentAsync());
            string serialized = System.Text.Json.JsonSerializer.Serialize(entry);
            Assert.DoesNotContain("message-token", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("exception-secret", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("property-secret", serialized, StringComparison.Ordinal);
            Assert.DoesNotContain("query-secret", serialized, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", serialized, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Write_AddsAmbientCorrelationId()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "CreatorControlSuite.LoggerTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var logger = new JsonLineAppLogger(root);
            using (OperationCorrelation.Begin("workflow-123"))
            {
                logger.Write(AppLogLevel.Information, "Workflow", "started");
            }

            AppLogEntry entry = Assert.Single(await logger.ReadRecentAsync());
            Assert.Equal("workflow-123", entry.Properties["correlationId"]);
            Assert.Null(OperationCorrelation.CurrentId);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
