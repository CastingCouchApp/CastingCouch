using System.Globalization;
using System.Text.RegularExpressions;

namespace CreatorControlSuite.Core.Updates;

public sealed partial class ProductVersionInfo(
    int major,
    int minor,
    int patch,
    string? preReleaseLabel = null,
    int preReleaseNumber = 0) :
    IComparable<ProductVersionInfo>,
    IEquatable<ProductVersionInfo>
{
    private static readonly Regex SemVerRegex = SemVerPattern();

    public int Major { get; } = major;
    public int Minor { get; } = minor;
    public int Patch { get; } = patch;
    public string? PreReleaseLabel { get; } = string.IsNullOrWhiteSpace(preReleaseLabel)
            ? null
            : preReleaseLabel.Trim().ToLowerInvariant();
    public int PreReleaseNumber { get; } = Math.Max(0, preReleaseNumber);
    public bool IsPrerelease => PreReleaseLabel is not null;

    public static bool TryParse(string? value, out ProductVersionInfo version)
    {
        version = new ProductVersionInfo(0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        int plus = trimmed.IndexOf('+');
        if (plus >= 0)
        {
            trimmed = trimmed[..plus];
        }

        Match match = SemVerRegex.Match(trimmed);
        if (!match.Success)
        {
            return false;
        }

        int major = int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture);
        int minor = int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture);
        int patch = int.Parse(match.Groups["patch"].Value, CultureInfo.InvariantCulture);
        string? label = null;
        int preNumber = 0;

        if (match.Groups["pre"].Success)
        {
            label = match.Groups["prelabel"].Value;
            if (match.Groups["prenum"].Success)
            {
                preNumber = int.Parse(
                    match.Groups["prenum"].Value,
                    CultureInfo.InvariantCulture);
            }
        }

        version = new ProductVersionInfo(major, minor, patch, label, preNumber);
        return true;
    }

    public static ProductVersionInfo Parse(string value)
    {
        if (!TryParse(value, out ProductVersionInfo? version))
        {
            throw new FormatException($"Ungültige Produktversion: '{value}'.");
        }

        return version;
    }

    /// <summary>
    /// WiX/MSI ProductVersion (nur Ziffern, max. 3 Teile).
    /// 8.0.0-alpha101 → 8.0.101
    /// </summary>
    public string ToMsiVersion()
    {
        if (IsPrerelease && PreReleaseNumber > 0 && Patch == 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{Major}.{Minor}.{PreReleaseNumber}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Major}.{Minor}.{Patch}");
    }

    public string ToChannel()
    {
        if (string.Equals(PreReleaseLabel, "alpha", StringComparison.Ordinal))
        {
            return "Alpha";
        }

        if (string.Equals(PreReleaseLabel, "beta", StringComparison.Ordinal))
        {
            return "Beta";
        }

        return "Stable";
    }

    public int CompareTo(ProductVersionInfo? other)
    {
        if (other is null)
        {
            return 1;
        }

        int core = Major.CompareTo(other.Major);
        if (core != 0)
        {
            return core;
        }

        core = Minor.CompareTo(other.Minor);
        if (core != 0)
        {
            return core;
        }

        core = Patch.CompareTo(other.Patch);
        if (core != 0)
        {
            return core;
        }

        if (!IsPrerelease && !other.IsPrerelease)
        {
            return 0;
        }

        if (!IsPrerelease)
        {
            return 1;
        }

        if (!other.IsPrerelease)
        {
            return -1;
        }

        int label = PreReleaseRank(PreReleaseLabel)
            .CompareTo(PreReleaseRank(other.PreReleaseLabel));
        if (label != 0)
        {
            return label;
        }

        return PreReleaseNumber.CompareTo(other.PreReleaseNumber);
    }

    public bool Equals(ProductVersionInfo? other) =>
        other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) =>
        obj is ProductVersionInfo other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Major, Minor, Patch, PreReleaseLabel, PreReleaseNumber);

    public override string ToString()
    {
        if (!IsPrerelease)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{Major}.{Minor}.{Patch}");
        }

        if (PreReleaseNumber > 0)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{Major}.{Minor}.{Patch}-{PreReleaseLabel}{PreReleaseNumber}");
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Major}.{Minor}.{Patch}-{PreReleaseLabel}");
    }

    public static bool operator >(ProductVersionInfo left, ProductVersionInfo right) =>
        left.CompareTo(right) > 0;

    public static bool operator <(ProductVersionInfo left, ProductVersionInfo right) =>
        left.CompareTo(right) < 0;

    public static bool operator >=(ProductVersionInfo left, ProductVersionInfo right) =>
        left.CompareTo(right) >= 0;

    public static bool operator <=(ProductVersionInfo left, ProductVersionInfo right) =>
        left.CompareTo(right) <= 0;

    private static int PreReleaseRank(string? label) =>
        label switch
        {
            "alpha" => 1,
            "beta" => 2,
            "rc" => 3,
            _ => 0
        };

    [GeneratedRegex(
        @"^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<pre>(?<prelabel>[a-zA-Z]+)(?<prenum>\d+)?))?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled)]
    private static partial Regex SemVerPattern();
}
