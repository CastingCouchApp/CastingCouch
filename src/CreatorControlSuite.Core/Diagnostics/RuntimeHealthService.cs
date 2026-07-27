using System.Diagnostics;
using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Validation;

namespace CreatorControlSuite.Core.Diagnostics;

public sealed class RuntimeHealthService(
    ISettingsStore settingsStore,
    ISettingsValidator validator)
{
    private readonly ISettingsStore _settingsStore = settingsStore;
    private readonly ISettingsValidator _validator = validator;

    public async Task<IReadOnlyList<RuntimeHealthItem>> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<RuntimeHealthItem>();
        AppSettings settings = await _settingsStore.LoadAsync(cancellationToken);
        ValidationReport validation = _validator.Validate(settings);

        results.AddRange(
            validation.Issues.Select(issue =>
                new RuntimeHealthItem(
                    issue.Section,
                    issue.Severity.ToString(),
                    issue.Message,
                    issue.SuggestedFix)));

        results.Add(
            new RuntimeHealthItem(
                "System",
                "Information",
                "Arbeitsspeicher der Suite",
                FormatBytes(
                    Process.GetCurrentProcess()
                        .PrivateMemorySize64)));

        results.Add(
            new RuntimeHealthItem(
                "System",
                "Information",
                ".NET",
                Environment.Version.ToString()));

        results.Add(
            new RuntimeHealthItem(
                "System",
                "Information",
                "Datenordner",
                Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "CreatorControlSuite")));

        return results;
    }

    private static string FormatBytes(long bytes)
    {
        return (bytes / 1024d / 1024d).ToString("0.0") + " MB";
    }
}

public sealed record RuntimeHealthItem(
    string Area,
    string Status,
    string Detail,
    string Recommendation = "");
