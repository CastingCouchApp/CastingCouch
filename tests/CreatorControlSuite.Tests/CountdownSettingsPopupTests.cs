using System.Globalization;
using System.Xml.Linq;

namespace CreatorControlSuite.Tests;

public sealed class CountdownSettingsPopupTests
{
    private static readonly XNamespace XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace PresentationNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void SettingsPopup_IsWideEnoughForAllPresetLabels()
    {
        XDocument document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "MainWindow.xaml"));

        XElement popup = Assert.Single(
            document.Descendants(PresentationNs + "Popup"),
            element =>
                (string?)element.Attribute(XamlNs + "Name") == "DashboardCountdownSettingsPopup");
        XElement border = Assert.Single(popup.Elements(PresentationNs + "Border"));

        string? minWidthText = (string?)border.Attribute("MinWidth");
        Assert.NotNull(minWidthText);
        double minWidth = double.Parse(minWidthText, CultureInfo.InvariantCulture);

        Assert.True(minWidth >= 300, $"Der Countdown-Dialog ist mit {minWidth}px zu schmal.");
        Assert.Null(border.Attribute("MaxWidth"));
    }

    [Fact]
    public void PresetButtons_AreTallEnoughForUnclippedLabels()
    {
        XDocument document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "CreatorControlSuite.App",
            "Shell",
            "MainWindow.xaml"));

        foreach (string name in new[]
                 {
                     "DashboardCountdownPreset5Button",
                     "DashboardCountdownPreset10Button",
                     "DashboardCountdownPreset30Button"
                 })
        {
            XElement button = Assert.Single(
                document.Descendants(PresentationNs + "Button"),
                element => (string?)element.Attribute(XamlNs + "Name") == name);
            string? heightText = (string?)button.Attribute("Height");
            Assert.NotNull(heightText);
            double height = double.Parse(heightText, CultureInfo.InvariantCulture);

            Assert.True(height >= 36, $"{name} ist mit {height}px zu niedrig.");
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tests")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
