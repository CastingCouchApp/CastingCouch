using System.Text.Json;
using CreatorControlSuite.Modules.OBS.Models;

namespace CreatorControlSuite.Modules.OBS;

public sealed partial class ObsWebSocketClient
{
    private async Task<int> GetSceneItemIdAsync(
        string sceneName,
        string sourceName,
        CancellationToken cancellationToken)
    {
        JsonElement data = await SendRequestAsync(
            "GetSceneItemId",
            new
            {
                sceneName,
                sourceName
            },
            cancellationToken);

        return GetInt32(data, "sceneItemId");
    }

    private static int ParseObsColor(string htmlColor)
    {
        string value = htmlColor.Trim().TrimStart('#');

        if (value.Length != 6)
        {
            return 0xFFFFFF;
        }

        int red = Convert.ToInt32(value[..2], 16);
        int green = Convert.ToInt32(value.Substring(2, 2), 16);
        int blue = Convert.ToInt32(value.Substring(4, 2), 16);

        return (blue << 16) | (green << 8) | red;
    }

    public async Task<(string CurrentProfile, IReadOnlyList<string> Profiles)> GetProfileListAsync(CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendRequestAsync("GetProfileList", null, cancellationToken);
        string current = data.TryGetProperty("currentProfileName", out JsonElement currentElement) ? currentElement.GetString() ?? "" : "";
        string[] profiles = data.TryGetProperty("profiles", out JsonElement listElement)
            ? [.. listElement.EnumerateArray().Select(x => x.TryGetProperty("profileName", out JsonElement n) ? n.GetString() ?? "" : "").Where(x => x.Length > 0)]
            : [];
        return (current, profiles);
    }

    public Task SetCurrentProfileAsync(string profileName, CancellationToken cancellationToken = default)
        => SendRequestWithoutResultAsync("SetCurrentProfile", new { profileName }, cancellationToken);

    public async Task<(string CurrentSceneCollection, IReadOnlyList<string> SceneCollections)> GetSceneCollectionListAsync(CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendRequestAsync("GetSceneCollectionList", null, cancellationToken);
        string current = data.TryGetProperty("currentSceneCollectionName", out JsonElement currentElement) ? currentElement.GetString() ?? "" : "";
        string[] collections = data.TryGetProperty("sceneCollections", out JsonElement listElement)
            ? [.. listElement.EnumerateArray().Select(x => x.TryGetProperty("sceneCollectionName", out JsonElement n) ? n.GetString() ?? "" : "").Where(x => x.Length > 0)]
            : [];
        return (current, collections);
    }

    public Task SetCurrentSceneCollectionAsync(string sceneCollectionName, CancellationToken cancellationToken = default)
        => SendRequestWithoutResultAsync("SetCurrentSceneCollection", new { sceneCollectionName }, cancellationToken);

    public async Task<byte[]> GetSourceScreenshotAsync(
        string sourceName,
        int imageWidth = 640,
        int? imageHeight = 360,
        CancellationToken cancellationToken = default)
    {
        object requestData = imageHeight is int height
            ? new
            {
                sourceName,
                imageFormat = "png",
                imageWidth,
                imageHeight = height,
                imageCompressionQuality = -1
            }
            : new
            {
                sourceName,
                imageFormat = "png",
                imageWidth,
                imageCompressionQuality = -1
            };

        JsonElement data = await SendRequestAsync(
            "GetSourceScreenshot",
            requestData,
            cancellationToken);

        if (!data.TryGetProperty("imageData", out JsonElement imageDataElement))
        {
            return [];
        }

        string? imageData = imageDataElement.GetString();
        if (string.IsNullOrWhiteSpace(imageData))
        {
            return [];
        }

        int commaIndex = imageData.IndexOf(',');
        string base64 = commaIndex >= 0 ? imageData[(commaIndex + 1)..] : imageData;
        return Convert.FromBase64String(base64);
    }

    public async Task<ObsVideoSettings> GetVideoSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        JsonElement data = await SendRequestAsync(
            "GetVideoSettings",
            requestData: null,
            cancellationToken);
        return ObsVideoSettings.Parse(data);
    }

    public async Task<ObsSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        Task<ObsServerInfo> versionTask = GetVersionAsync(cancellationToken);
        Task<IReadOnlyList<ObsSceneInfo>> scenesTask = GetSceneListAsync(cancellationToken);
        Task<IReadOnlyList<ObsInputInfo>> inputsTask = GetInputListAsync(cancellationToken);
        Task<string> currentSceneTask = GetCurrentProgramSceneAsync(cancellationToken);
        Task<ObsStreamStatus> streamTask = GetStreamStatusAsync(cancellationToken);

        await Task.WhenAll(
            versionTask,
            scenesTask,
            inputsTask,
            currentSceneTask,
            streamTask);

        return new ObsSnapshot(
            Connected: IsConnected,
            CurrentProgramScene: await currentSceneTask,
            CurrentPreviewScene: "",
            Scenes: await scenesTask,
            Inputs: await inputsTask,
            Server: await versionTask,
            Stream: await streamTask);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _sendLock.Dispose();
    }
}
