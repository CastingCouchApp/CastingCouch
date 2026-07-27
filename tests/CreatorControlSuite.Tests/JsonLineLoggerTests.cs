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
}
