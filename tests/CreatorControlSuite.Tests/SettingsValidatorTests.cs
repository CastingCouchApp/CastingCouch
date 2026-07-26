using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Validation;

namespace CreatorControlSuite.Tests;

public sealed class SettingsValidatorTests
{
    [Fact]
    public void DefaultSettingsAreValidOrWarningsOnly()
    {
        var validator = new SettingsValidator();
        var report = validator.Validate(new AppSettings());

        Assert.DoesNotContain(
            report.Issues,
            issue =>
                issue.Severity == ValidationSeverity.Error &&
                issue.Code != "TWITCH_CHAT_SCOPE_MISSING");
    }

    [Fact]
    public void RejectsInvalidObsPort()
    {
        var settings = new AppSettings();
        settings.Obs.Port = 70000;

        var report = new SettingsValidator().Validate(settings);

        Assert.Contains(
            report.Issues,
            issue => issue.Code == "OBS_PORT_INVALID");
    }

    [Fact]
    public void RejectsInvalidEndSceneDuration()
    {
        var settings = new AppSettings();
        settings.Workflow.EndSceneSeconds = 0;

        var report = new SettingsValidator().Validate(settings);

        Assert.Contains(
            report.Issues,
            issue =>
                issue.Code ==
                "END_SCENE_DURATION_INVALID");
    }
}
