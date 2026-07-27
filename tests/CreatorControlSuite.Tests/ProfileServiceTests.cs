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
}
