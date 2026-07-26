using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Modules.Alerts.Models;
using CreatorControlSuite.Modules.OBS;

namespace CreatorControlSuite.Modules.Alerts;

public sealed class ObsAlertRenderer
{
    private readonly ISettingsStore _settingsStore;
    private readonly IObsWebSocketClient _obsClient;

    public ObsAlertRenderer(
        ISettingsStore settingsStore,
        IObsWebSocketClient obsClient)
    {
        _settingsStore = settingsStore;
        _obsClient = obsClient;
    }

    public Task InstallSourcesAsync(
        AlertDefinition definition,
        string renderedText,
        CancellationToken cancellationToken = default)
    {
        return PrepareSourcesAsync(
            definition,
            renderedText,
            createIfMissing: true,
            cancellationToken);
    }

    public async Task ShowAsync(
        AlertDefinition definition,
        string renderedText,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.LoadAsync(
            cancellationToken);

        var alertSettings = settings.Alerts;

        await PrepareSourcesAsync(
            definition,
            renderedText,
            createIfMissing: false,
            cancellationToken);

        await _obsClient.SetInputSettingsAsync(
            alertSettings.ObsTextSourceName,
            new
            {
                text = renderedText,
                color = ParseObsColor(definition.FontColor),
                font = new
                {
                    face = definition.FontFace,
                    size = definition.FontSize,
                    style = "Regular",
                    flags = 0
                }
            },
            overlay: true,
            cancellationToken);

        await _obsClient.SetSceneItemEnabledAsync(
            alertSettings.ObsSceneName,
            alertSettings.ObsTextSourceName,
            enabled: true,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(definition.MediaPath) &&
            File.Exists(definition.MediaPath))
        {
            await _obsClient.SetInputSettingsAsync(
                alertSettings.ObsMediaSourceName,
                new
                {
                    local_file = definition.MediaPath,
                    looping = false,
                    restart_on_activate = false,
                    close_when_inactive = true,
                    clear_on_media_end = true,
                    speed_percent = 100
                },
                overlay: false,
                cancellationToken);

            await _obsClient.SetSceneItemEnabledAsync(
                alertSettings.ObsSceneName,
                alertSettings.ObsMediaSourceName,
                enabled: true,
                cancellationToken);

            await _obsClient.RestartMediaInputAsync(
                alertSettings.ObsMediaSourceName,
                cancellationToken);
        }
    }

    public async Task HideAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = await _settingsStore.LoadAsync(
            cancellationToken);

        var alertSettings = settings.Alerts;

        try
        {
            await _obsClient.StopMediaInputAsync(
                alertSettings.ObsMediaSourceName,
                cancellationToken);
        }
        catch
        {
        }

        try
        {
            await _obsClient.SetSceneItemEnabledAsync(
                alertSettings.ObsSceneName,
                alertSettings.ObsMediaSourceName,
                enabled: false,
                cancellationToken);
        }
        catch
        {
        }

        try
        {
            await _obsClient.SetSceneItemEnabledAsync(
                alertSettings.ObsSceneName,
                alertSettings.ObsTextSourceName,
                enabled: false,
                cancellationToken);
        }
        catch
        {
        }
    }

    private async Task PrepareSourcesAsync(
        AlertDefinition definition,
        string renderedText,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(
            cancellationToken);

        if (!_obsClient.IsConnected)
        {
            throw new InvalidOperationException(
                "OBS ist für Alerts nicht verbunden.");
        }

        var alertSettings = settings.Alerts;

        if (createIfMissing)
        {
            await _obsClient.EnsureSceneAsync(
                alertSettings.ObsSceneName,
                cancellationToken);

            await _obsClient.EnsureTextInputAsync(
                alertSettings.ObsSceneName,
                alertSettings.ObsTextSourceName,
                renderedText,
                definition.FontFace,
                definition.FontSize,
                definition.FontColor,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(definition.MediaPath) &&
                File.Exists(definition.MediaPath))
            {
                await _obsClient.EnsureMediaInputAsync(
                    alertSettings.ObsSceneName,
                    alertSettings.ObsMediaSourceName,
                    definition.MediaPath,
                    cancellationToken);
            }
            else
            {
                await _obsClient.EnsureMediaInputAsync(
                    alertSettings.ObsSceneName,
                    alertSettings.ObsMediaSourceName,
                    localFile: string.Empty,
                    cancellationToken);
            }
        }
        else
        {
            await EnsureExistingSourcesAsync(
                alertSettings,
                cancellationToken);
        }

        await _obsClient.SetSceneItemTransformAsync(
            alertSettings.ObsSceneName,
            alertSettings.ObsTextSourceName,
            definition.X + definition.Width * 0.37,
            definition.Y,
            definition.Width * 0.63,
            definition.Height,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(definition.MediaPath) &&
            File.Exists(definition.MediaPath))
        {
            await _obsClient.SetSceneItemTransformAsync(
                alertSettings.ObsSceneName,
                alertSettings.ObsMediaSourceName,
                definition.X,
                definition.Y,
                definition.Width * 0.34,
                definition.Height,
                cancellationToken);
        }
    }

    private async Task EnsureExistingSourcesAsync(
        AlertSettings alertSettings,
        CancellationToken cancellationToken)
    {
        var scenes = await _obsClient.GetSceneListAsync(
            cancellationToken);

        var sceneExists = scenes.Any(scene =>
            string.Equals(
                scene.Name,
                alertSettings.ObsSceneName,
                StringComparison.OrdinalIgnoreCase));

        if (!sceneExists)
        {
            throw new InvalidOperationException(
                $"Die OBS-Alert-Szene '{alertSettings.ObsSceneName}' fehlt. " +
                "Bitte unter Alerts „In OBS als Szene hinzufügen“ ausführen.");
        }

        if (!await _obsClient.InputExistsAsync(
                alertSettings.ObsTextSourceName,
                cancellationToken) ||
            !await _obsClient.SceneItemExistsAsync(
                alertSettings.ObsSceneName,
                alertSettings.ObsTextSourceName,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Die OBS-Textquelle '{alertSettings.ObsTextSourceName}' fehlt. " +
                "Bitte unter Alerts „In OBS als Szene hinzufügen“ ausführen.");
        }

        if (!await _obsClient.InputExistsAsync(
                alertSettings.ObsMediaSourceName,
                cancellationToken) ||
            !await _obsClient.SceneItemExistsAsync(
                alertSettings.ObsSceneName,
                alertSettings.ObsMediaSourceName,
                cancellationToken))
        {
            throw new InvalidOperationException(
                $"Die OBS-Medienquelle '{alertSettings.ObsMediaSourceName}' fehlt. " +
                "Bitte unter Alerts „In OBS als Szene hinzufügen“ ausführen.");
        }
    }

    private static int ParseObsColor(string htmlColor)
    {
        var value = htmlColor.Trim().TrimStart('#');

        if (value.Length != 6)
        {
            return 0xFFFFFF;
        }

        var red = Convert.ToInt32(value.Substring(0, 2), 16);
        var green = Convert.ToInt32(value.Substring(2, 2), 16);
        var blue = Convert.ToInt32(value.Substring(4, 2), 16);

        return blue << 16 | green << 8 | red;
    }
}
