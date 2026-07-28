using System.Xml.Linq;

namespace CreatorControlSuite.Tests;

public sealed class ArchitectureGuardTests
{
    private static readonly IReadOnlyDictionary<string, int> OversizedFileBaseline =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["src/CreatorControlSuite.App/Shell/MainWindow.xaml.cs"] = 23_655,
            ["src/CreatorControlSuite.App/Shell/MainWindow.xaml"] = 3_881
        };

    [Fact]
    [Trait("Category", "Architecture")]
    public void SourceFiles_DoNotIntroduceOrGrowOversizedDebt()
    {
        string root = FindRepositoryRoot();
        string sourceRoot = Path.Combine(root, "src");
        var violations = new List<string>();

        foreach (string path in Directory.EnumerateFiles(
                     sourceRoot,
                     "*",
                     SearchOption.AllDirectories)
                 .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                                path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            int lineCount = File.ReadLines(path).Count();
            if (OversizedFileBaseline.TryGetValue(relative, out int baseline))
            {
                if (lineCount > baseline)
                {
                    violations.Add($"{relative}: {lineCount} > Baseline {baseline}");
                }

                continue;
            }

            if (lineCount >= 1_000)
            {
                violations.Add($"{relative}: neue übergroße Datei mit {lineCount} Zeilen");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Architekturgrößen-Gate verletzt:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void CoreAndModules_DoNotReferenceApp()
    {
        string root = FindRepositoryRoot();
        string[] projectFiles = Directory.GetFiles(
            Path.Combine(root, "src"),
            "*.csproj",
            SearchOption.AllDirectories);
        var violations = new List<string>();

        foreach (string projectFile in projectFiles)
        {
            string projectName = Path.GetFileNameWithoutExtension(projectFile);
            if (projectName == "CreatorControlSuite.App")
            {
                continue;
            }

            XDocument project = XDocument.Load(projectFile);
            IEnumerable<string> references = project
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value ?? "");
            if (references.Any(reference =>
                    reference.Contains(
                        "CreatorControlSuite.App",
                        StringComparison.OrdinalIgnoreCase)))
            {
                violations.Add(projectName + " → CreatorControlSuite.App");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Unerlaubte Rückreferenzen:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void Core_HasNoProjectReferences()
    {
        string root = FindRepositoryRoot();
        string projectPath = Path.Combine(
            root,
            "src",
            "CreatorControlSuite.Core",
            "CreatorControlSuite.Core.csproj");
        XDocument project = XDocument.Load(projectPath);

        Assert.Empty(project.Descendants("ProjectReference"));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void ModuleDependencies_MatchExplicitAllowlist()
    {
        string root = FindRepositoryRoot();
        var allowed = new Dictionary<string, string[]>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["CreatorControlSuite.Modules.OBS"] = ["CreatorControlSuite.Core"],
            ["CreatorControlSuite.Modules.Twitch"] = ["CreatorControlSuite.Core"],
            ["CreatorControlSuite.Modules.Spotify"] = ["CreatorControlSuite.Core"],
            ["CreatorControlSuite.Modules.Alerts"] =
                ["CreatorControlSuite.Core", "CreatorControlSuite.Modules.OBS"],
            ["CreatorControlSuite.Modules.Overlay"] =
                ["CreatorControlSuite.Core", "CreatorControlSuite.Modules.OBS"],
            ["CreatorControlSuite.Modules.Workflow"] = ["CreatorControlSuite.Core"],
            ["CreatorControlSuite.Modules.StreamDeck"] = ["CreatorControlSuite.Core"],
            ["CreatorControlSuite.Modules.YouTubeMusic"] = ["CreatorControlSuite.Core"]
        };
        var violations = new List<string>();
        foreach ((string projectName, string[] allowedReferences) in allowed)
        {
            string projectPath = Path.Combine(
                root,
                "src",
                projectName,
                projectName + ".csproj");
            XDocument project = XDocument.Load(projectPath);
            string[] actual = [.. project.Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value ?? "")
                .Select(value => Path.GetFileNameWithoutExtension(value) ?? "")
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)];
            string[] expected = [.. allowedReferences
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)];
            if (!actual.SequenceEqual(expected, StringComparer.OrdinalIgnoreCase))
            {
                violations.Add(
                    $"{projectName}: [{string.Join(", ", actual)}], erwartet " +
                    $"[{string.Join(", ", expected)}]");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Unerlaubte Modulabhängigkeiten:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    [Trait("Category", "Architecture")]
    public void CommercialLicensing_IsNotPartOfProduct()
    {
        string root = FindRepositoryRoot();
        string sourceRoot = Path.Combine(root, "src");
        string licensingRoot = Path.Combine(
            sourceRoot,
            "CreatorControlSuite.Core",
            "Licensing");
        string[] licensingSources = Directory.Exists(licensingRoot)
            ? Directory.GetFiles(licensingRoot, "*.cs", SearchOption.AllDirectories)
            : [];

        Assert.Empty(licensingSources);
        Assert.False(
            File.Exists(Path.Combine(
                sourceRoot,
                "CreatorControlSuite.LicenseMockServer",
                "CreatorControlSuite.LicenseMockServer.csproj")),
            "Der Lizenz-Mockserver darf nicht wieder eingeführt werden.");

        string[] prohibitedTokens =
        [
            "CreatorControlSuite.Core.Licensing",
            "IFeatureGate",
            "ILicenseService",
            "LicenseMockServer",
            "license-public.pem"
        ];
        string[] sourceFiles = Directory
            .EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var violations = new List<string>();

        foreach (string sourceFile in sourceFiles)
        {
            string content = File.ReadAllText(sourceFile);
            foreach (string token in prohibitedTokens.Where(token =>
                         content.Contains(token, StringComparison.Ordinal)))
            {
                violations.Add(
                    $"{Path.GetRelativePath(root, sourceFile)}: {token}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Kommerzielle Laufzeit-Lizenzierung gefunden:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Repository-Root mit Directory.Build.props wurde nicht gefunden.");
    }
}
