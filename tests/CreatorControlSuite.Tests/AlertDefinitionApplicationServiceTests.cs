using CreatorControlSuite.App.Services;
using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Tests;

public sealed class AlertDefinitionApplicationServiceTests
{
    [Fact]
    public async Task Create_UsesUniqueTypeAndPersists()
    {
        var settings = new AppSettings();
        settings.Alerts.Definitions["Eigener Alert"] = new();
        var store = new FakeSettingsStore();
        var service = new AlertDefinitionApplicationService(store);

        AlertDefinitionSettings created =
            await service.CreateAsync(settings, "Eigener Alert");

        Assert.Equal("Eigener Alert 2", created.Type);
        Assert.Equal(
            "{user} hat einen Alert ausgelöst!",
            created.TextTemplate);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task Duplicate_CopiesAllDefinitionFields()
    {
        var settings = new AppSettings();
        settings.Alerts.Definitions["Source"] = new AlertDefinitionSettings
        {
            Type = "Source",
            Enabled = false,
            TextTemplate = "Text",
            MediaPath = "media.mp4",
            SoundPath = "sound.wav",
            DurationSeconds = 14,
            Priority = 12,
            FontFace = "Inter",
            FontSize = 51,
            FontColor = "#123456",
            Animation = "Zoom",
            X = 1,
            Y = 2,
            Width = 3,
            Height = 4,
            VolumePercent = 65,
            SoundStartSeconds = 1.25,
            SoundEndSeconds = 4.5,
            AudioOutputDeviceId = "device"
        };
        var service = new AlertDefinitionApplicationService(
            new FakeSettingsStore());

        AlertDefinitionSettings duplicate =
            await service.DuplicateAsync(settings, "Source");

        Assert.Equal("Source Kopie", duplicate.Type);
        Assert.False(duplicate.Enabled);
        Assert.Equal("Text", duplicate.TextTemplate);
        Assert.Equal("media.mp4", duplicate.MediaPath);
        Assert.Equal("sound.wav", duplicate.SoundPath);
        Assert.Equal(14, duplicate.DurationSeconds);
        Assert.Equal(12, duplicate.Priority);
        Assert.Equal("Inter", duplicate.FontFace);
        Assert.Equal(51, duplicate.FontSize);
        Assert.Equal("#123456", duplicate.FontColor);
        Assert.Equal("Zoom", duplicate.Animation);
        Assert.Equal(65, duplicate.VolumePercent);
        Assert.Equal(1.25, duplicate.SoundStartSeconds);
        Assert.Equal(4.5, duplicate.SoundEndSeconds);
        Assert.Equal("device", duplicate.AudioOutputDeviceId);
    }

    [Fact]
    public async Task Toggle_RollsBackWhenPersistenceFails()
    {
        var settings = new AppSettings();
        AlertDefinitionSettings definition =
            settings.Alerts.Definitions["Follow"];
        bool initial = definition.Enabled;
        var store = new FakeSettingsStore
        {
            SaveException = new IOException("disk full")
        };
        var service = new AlertDefinitionApplicationService(store);

        await Assert.ThrowsAsync<IOException>(
            () => service.ToggleAsync(settings, "Follow"));

        Assert.Equal(initial, definition.Enabled);
    }

    [Fact]
    public async Task Delete_RollsBackWhenPersistenceFails()
    {
        var settings = new AppSettings();
        var store = new FakeSettingsStore
        {
            SaveException = new IOException("disk full")
        };
        var service = new AlertDefinitionApplicationService(store);

        await Assert.ThrowsAsync<IOException>(
            () => service.DeleteAsync(settings, "Follow"));

        Assert.True(
            settings.Alerts.Definitions.ContainsKey("Follow"));
    }

    [Fact]
    public async Task Delete_RejectsLastDefinition()
    {
        var settings = new AppSettings();
        settings.Alerts.Definitions =
            new Dictionary<string, AlertDefinitionSettings>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["Only"] = new()
                {
                    Type = "Only"
                }
            };
        var service = new AlertDefinitionApplicationService(
            new FakeSettingsStore());

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.DeleteAsync(settings, "Only"));

        Assert.Contains("Mindestens", exception.Message);
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public int SaveCount { get; private set; }
        public Exception? SaveException { get; init; }

        public Task<AppSettings> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new AppSettings());

        public Task SaveAsync(
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return SaveException is null
                ? Task.CompletedTask
                : Task.FromException(SaveException);
        }
    }
}
