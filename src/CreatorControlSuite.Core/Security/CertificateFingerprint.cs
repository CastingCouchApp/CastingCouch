using System.Security.Cryptography;
using System.Text;

namespace CreatorControlSuite.Core.Security;

public static class CertificateFingerprint
{
    public const int Sha256HexLength = 64;

    public static string Normalize(string? value)
    {
        string compact = new(
            (value ?? string.Empty)
            .Where(character => character is not ':' and not '-' && !char.IsWhiteSpace(character))
            .ToArray());

        if (compact.Length != Sha256HexLength ||
            compact.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new FormatException(
                "Der Zertifikat-Fingerprint muss aus 64 Hexadezimalzeichen bestehen.");
        }

        return compact.ToUpperInvariant();
    }

    public static bool Matches(string expected, string actual)
    {
        try
        {
            byte[] expectedBytes = Encoding.ASCII.GetBytes(Normalize(expected));
            byte[] actualBytes = Encoding.ASCII.GetBytes(Normalize(actual));
            return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
