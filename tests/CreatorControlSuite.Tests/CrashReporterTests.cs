using CreatorControlSuite.Core.Diagnostics;

namespace CreatorControlSuite.Tests;

public sealed class CrashReporterTests
{
    [Fact]
    public async Task WritesCrashReport()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "CreatorControlSuite.CrashTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var reporter = new FileCrashReporter(root);
            string path = await reporter.WriteAsync(
                new InvalidOperationException("Testfehler"));

            Assert.True(File.Exists(path));
            Assert.Contains(
                "Testfehler",
                await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
