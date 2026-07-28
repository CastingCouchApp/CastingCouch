using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Profiles;

namespace CreatorControlSuite.Tests;

public sealed class ProfileServiceTests
{
    [Fact]
    public async Task CanCreateAndApplyProfile()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "CreatorControlSuite.ProfileTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(root);

        try
        {
            var settingsStore =
                new JsonSettingsStore(
                    Path.Combine(root, "settings.json"));

            var service =
                new JsonProfileService(
                    Path.Combine(root, "profiles"),
                    settingsStore);

            var settings = new AppSettings();
            settings.Branding.DisplayName = "Denver John";
            await settingsStore.SaveAsync(settings);

            CreatorProfile profile =
                await service.CreateFromCurrentSettingsAsync(
                    "RP",
                    "Roleplay");

            settings.Branding.DisplayName = "Changed";
            await settingsStore.SaveAsync(settings);

            await service.ApplyAsync(profile.Id);

            AppSettings loaded = await settingsStore.LoadAsync();

            Assert.Equal(
                "Denver John",
                loaded.Branding.DisplayName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SaveExportAndApply_DoNotPersistOrReplaceStreamerBotPassword()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "CreatorControlSuite.ProfileTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var settingsStore = new MemorySettingsStore();
            var service = new JsonProfileService(
                Path.Combine(root, "profiles"),
                settingsStore);
            var profile = new CreatorProfile
            {
                Id = "secure-profile",
                Name = "Secure",
                Settings = new AppSettings()
            };
            profile.Settings.StreamerBot.Password = "profile-secret";

            await service.SaveAsync(profile);
            string profileJson = await File.ReadAllTextAsync(
                Path.Combine(root, "profiles", "secure-profile.json"));

            Assert.DoesNotContain("profile-secret", profileJson, StringComparison.Ordinal);

            settingsStore.Settings.StreamerBot.Password = "current-secret";
            await service.ApplyAsync(profile.Id);

            Assert.Equal(
                "current-secret",
                settingsStore.Settings.StreamerBot.Password);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class MemorySettingsStore : ISettingsStore
    {
        public AppSettings Settings { get; private set; } = new();

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Settings);

        public Task SaveAsync(
            AppSettings settings,
            CancellationToken cancellationToken = default)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }
}
