using System.Text.RegularExpressions;

namespace CreatorControlSuite.Core.Security;

public static partial class SecretRedactor
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value ?? "";
        }

        string redacted = JsonSecretRegex().Replace(
            value,
            "$1[REDACTED]$3");
        redacted = BearerRegex().Replace(
            redacted,
            "$1[REDACTED]");
        redacted = AgentHeaderRegex().Replace(
            redacted,
            "$1[REDACTED]");
        return KeyValueSecretRegex().Replace(
            redacted,
            "$1=[REDACTED]");
    }

    public static bool IsSensitiveKey(string key) =>
        SensitiveKeyRegex().IsMatch(key ?? "");

    [GeneratedRegex(
        "(\"(?:password|secret|token|apiKey|agentKey|authorization|clientSecret|refreshToken|accessToken)\"\\s*:\\s*\")([^\"]*)(\")",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JsonSecretRegex();

    [GeneratedRegex(
        "(Authorization\\s*:\\s*Bearer\\s+)([^\\s,;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(
        "(X-(?:CCS-)?Agent-Key\\s*:\\s*)([^\\s,;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AgentHeaderRegex();

    [GeneratedRegex(
        "\\b(password|secret|token|api[_-]?key|agent[_-]?key|refresh_token|access_token)\\s*=\\s*([^&\\s;]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyValueSecretRegex();

    [GeneratedRegex(
        "(password|secret|token|authorization|api.?key|agent.?key)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveKeyRegex();
}
