using System.IO.Compression;

namespace CreatorControlSuite.Core.Updates;

public static class SafeZipExtractor
{
    public const int DefaultMaximumEntries = 20_000;
    public const long DefaultMaximumUncompressedBytes = 4L * 1024 * 1024 * 1024;

    public static void ExtractToDirectory(
        string archivePath,
        string destinationDirectory,
        bool overwriteFiles = true,
        int maximumEntries = DefaultMaximumEntries,
        long maximumUncompressedBytes = DefaultMaximumUncompressedBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        if (maximumEntries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        }

        if (maximumUncompressedBytes < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumUncompressedBytes));
        }

        Directory.CreateDirectory(destinationDirectory);
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > maximumEntries)
        {
            throw new InvalidDataException(
                $"ZIP enthält mehr als {maximumEntries} Einträge.");
        }

        long totalUncompressedBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            totalUncompressedBytes = checked(totalUncompressedBytes + entry.Length);
            if (totalUncompressedBytes > maximumUncompressedBytes)
            {
                throw new InvalidDataException(
                    "ZIP überschreitet die erlaubte entpackte Gesamtgröße.");
            }

            RejectSymbolicLink(entry);
            string destinationPath = ResolveDestinationPath(
                destinationDirectory,
                entry.FullName);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            string? parent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            entry.ExtractToFile(destinationPath, overwriteFiles);
        }
    }

    public static string ResolveDestinationPath(
        string destinationDirectory,
        string entryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);

        string normalizedEntry = entryName.Replace('\\', '/');
        if (normalizedEntry.StartsWith('/') ||
            normalizedEntry.Contains(':') ||
            normalizedEntry.Split('/').Any(segment => segment == ".."))
        {
            throw new InvalidDataException(
                $"Unsicherer ZIP-Pfad: {entryName}");
        }

        string root = Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string target = Path.GetFullPath(
            Path.Combine(destinationDirectory, normalizedEntry));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!target.StartsWith(root, comparison))
        {
            throw new InvalidDataException(
                $"ZIP-Pfad verlässt das Zielverzeichnis: {entryName}");
        }

        return target;
    }

    private static void RejectSymbolicLink(ZipArchiveEntry entry)
    {
        const int unixFileTypeMask = 0xF000;
        const int unixSymbolicLink = 0xA000;
        int unixMode = (entry.ExternalAttributes >> 16) & 0xFFFF;
        if ((unixMode & unixFileTypeMask) == unixSymbolicLink)
        {
            throw new InvalidDataException(
                $"Symbolische Links sind in Update-ZIPs nicht erlaubt: {entry.FullName}");
        }
    }
}
