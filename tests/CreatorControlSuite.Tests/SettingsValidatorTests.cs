using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Validation;

namespace CreatorControlSuite.Tests;

public sealed class SettingsValidatorTests
{
    [Fact]
    public void DefaultSettingsAreValidOrWarningsOnly()
    {
        var validator = new SettingsValidator();
        ValidationReport report = validator.Validate(new AppSettings());

        Assert.DoesNotContain(
            report.Issues,
            issue =>
                issue.Severity == ValidationSeverity.Error &&
                issue.Code != "TWITCH_CHAT_SCOPE_MISSING");
    }

    [Theory]
    [MemberData(nameof(ErrorCases))]
    public void Validate_ReportsExpectedError(
        string code,
        Action<AppSettings> mutate)
    {
        AppSettings settings = CreateBaselineSettings();
        mutate(settings);

        ValidationReport report = new SettingsValidator().Validate(settings);

        Assert.Contains(
            report.Issues,
            issue =>
                issue.Code == code &&
                issue.Severity == ValidationSeverity.Error);
    }

    [Theory]
    [InlineData("TWITCH_CLIENT_ID_EMPTY")]
    [InlineData("SPOTIFY_CLIENT_ID_EMPTY")]
    public void Validate_ReportsExpectedWarning(string code)
    {
        AppSettings settings = CreateBaselineSettings();
        settings.Twitch.AutoConnect = true;
        settings.Twitch.ClientId = "";
        settings.Spotify.AutoConnect = true;
        settings.Spotify.ClientId = "";

        ValidationReport report = new SettingsValidator().Validate(settings);

        Assert.Contains(
            report.Issues,
            issue =>
                issue.Code == code &&
                issue.Severity == ValidationSeverity.Warning);
    }

    public static TheoryData<string, Action<AppSettings>> ErrorCases => new()
    {
        {
            "OBS_HOST_EMPTY",
            settings => settings.Obs.Host = ""
        },
        {
            "OBS_PORT_INVALID",
            settings => settings.Obs.Port = 70000
        },
        {
            "OBS_SCENE_EMPTY",
            settings => settings.Obs.StartScene = ""
        },
        {
            "TWITCH_CHAT_SCOPE_MISSING",
            settings =>
            {
                settings.Twitch.EnableChat = true;
                settings.Twitch.Scopes = ["channel:read:subscriptions"];
            }
        },
        {
            "SPOTIFY_REDIRECT_INVALID",
            settings => settings.Spotify.RedirectUri = "http://localhost:43821/callback/"
        },
        {
            "SPOTIFY_VOLUME_INVALID",
            settings => settings.Spotify.StartVolumePercent = 150
        },
        {
            "ALERT_QUEUE_INVALID",
            settings => settings.Alerts.QueueCapacity = 0
        },
        {
            "ALERT_DURATION_INVALID",
            settings => settings.Alerts.Definitions["Follow"].DurationSeconds = 0
        },
        {
            "OVERLAY_SIZE_INVALID",
            settings =>
            {
                settings.Overlay.Width = 100;
                settings.Overlay.Height = 100;
            }
        },
        {
            "COUNTDOWN_INVALID",
            settings => settings.Workflow.StartCountdownSeconds = -1
        },
        {
            "END_SCENE_DURATION_INVALID",
            settings => settings.Workflow.EndSceneSeconds = 0
        }
    };

    private static AppSettings CreateBaselineSettings()
    {
        var settings = new AppSettings();
        settings.Twitch.AutoConnect = false;
        settings.Spotify.AutoConnect = false;
        settings.Twitch.EnableChat = false;
        return settings;
    }
}
