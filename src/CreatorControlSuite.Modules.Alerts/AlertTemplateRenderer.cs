using System.Text.RegularExpressions;

namespace CreatorControlSuite.Modules.Alerts;

public static partial class AlertTemplateRenderer
{
    [GeneratedRegex(@"\{(?<name>[A-Za-z0-9_]+)\}")]
    private static partial Regex VariablePattern();

    public static string Render(
        string template,
        string user,
        IReadOnlyDictionary<string, string> variables)
    {
        var values = new Dictionary<string, string>(
            variables,
            StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = user
        };

        return VariablePattern().Replace(
            template,
            match =>
            {
                var name = match.Groups["name"].Value;

                return values.TryGetValue(name, out var value)
                    ? value
                    : match.Value;
            });
    }
}
