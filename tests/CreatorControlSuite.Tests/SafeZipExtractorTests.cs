using System.IO.Compression;
using CreatorControlSuite.Core.Updates;

namespace CreatorControlSuite.Tests;

public sealed class SafeZipExtractorTests
{
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("folder/../../outside.txt")]
    [InlineData("/absolute.txt")]
    [InlineData("C:/absolute.txt")]
    public void Extract_RejectsPathsOutsideDestination(string entryName)
    {
        using var directory = new TemporaryDirectory();
        string archivePath = Path.Combine(directory.Path, "malicious.zip");
        CreateArchive(archivePath, entryName, "malicious");
        string destination = Path.Combine(directory.Path, "target");

        Assert.Throws<InvalidDataException>(
            () => SafeZipExtractor.ExtractToDirectory(archivePath, destination));

        Assert.False(File.Exists(Path.Combine(directory.Path, "outside.txt")));
    }

    [Fact]
    public void Extract_PreservesLegitimateNestedFiles()
    {
        using var directory = new TemporaryDirectory();
        string archivePath = Path.Combine(directory.Path, "valid.zip");
        CreateArchive(archivePath, "app/config/settings.json", "{}");
        string destination = Path.Combine(directory.Path, "target");

        SafeZipExtractor.ExtractToDirectory(archivePath, destination);

        Assert.Equal(
            "{}",
            File.ReadAllText(Path.Combine(destination, "app", "config", "settings.json")));
    }

    [Fact]
    public void ResolveDestination_RejectsBackupTraversal()
    {
        using var directory = new TemporaryDirectory();

        Assert.Throws<InvalidDataException>(
            () => SafeZipExtractor.ResolveDestinationPath(
                directory.Path,
                "../secrets.txt"));
    }

    private static void CreateArchive(
        string archivePath,
        string entryName,
        string content)
    {
        using ZipArchive archive = ZipFile.Open(
            archivePath,
            ZipArchiveMode.Create);
        ZipArchiveEntry entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CreatorControlSuite.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
