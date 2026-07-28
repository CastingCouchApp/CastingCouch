using System.Text.Json.Nodes;
using CreatorControlSuite.Core.Music;

namespace CreatorControlSuite.Core.Configuration;

internal static class SettingsSchemaMigrator
{
    public static bool Migrate(JsonObject root)
    {
        int version = root["SchemaVersion"]?.GetValue<int>() ?? 0;
        if (version > AppSettings.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Settings-Schema {version} ist neuer als das unterstützte Schema " +
                $"{AppSettings.CurrentSchemaVersion}.");
        }

        bool changed = false;
        while (version < AppSettings.CurrentSchemaVersion)
        {
            switch (version)
            {
                case 0:
                    MigrateVersionZeroToOne(root);
                    break;
                case 1:
                    MigrateVersionOneToTwo(root);
                    break;
                default:
                    throw new InvalidDataException(
                        $"Für Settings-Schema {version} fehlt eine Migration.");
            }

            version++;
            root["SchemaVersion"] = version;
            changed = true;
        }

        return changed;
    }

    private static void MigrateVersionZeroToOne(JsonObject root)
    {
        var updates = root["Updates"] as JsonObject ?? new JsonObject();
        if (updates["Channel"] is null &&
            root["Product"] is JsonObject product &&
            product["UpdateChannel"] is JsonValue legacyChannel)
        {
            updates["Channel"] = legacyChannel.GetValue<string>();
        }

        root["Updates"] = updates;
    }

    private static void MigrateVersionOneToTwo(JsonObject root)
    {
        if (root["MusicPlayer"] is not JsonObject musicPlayer ||
            musicPlayer["ProviderId"] is not JsonValue providerId)
        {
            return;
        }

        musicPlayer["ProviderId"] =
            MusicProviderIds.Normalize(providerId.GetValue<string>());
    }
}
