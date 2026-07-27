using CreatorControlSuite.Core.Configuration;
using CreatorControlSuite.Core.Logging;
using CreatorControlSuite.Modules.OBS;
using CreatorControlSuite.Modules.Overlay;

namespace CreatorControlSuite.App.Services;

public sealed class ObsBrowserSourceInstaller(
    ISettingsStore settingsStore,
    IObsWebSocketClient obs,
    IOverlayDataService overlay,
    IAppLogger logger)
{
    private readonly ISettingsStore _settingsStore = settingsStore;
    private readonly IObsWebSocketClient _obs = obs;
    private readonly IOverlayDataService _overlay = overlay;
    private readonly IAppLogger _logger = logger;

    public async Task<IReadOnlyList<string>> InstallAsync(CancellationToken ct = default)
    {
        if (!_obs.IsConnected)
        {
            throw new InvalidOperationException("OBS ist nicht verbunden.");
        }

        AppSettings settings = await _settingsStore.LoadAsync(ct);
        string root = await _overlay.GetOverlayRootAsync(ct);

        Def[] definitions =
        [
            new Def(settings.Obs.StartScene,"ccs_scene_start",Path.Combine(root,"scenes","start.html"),true),
            new Def(settings.Obs.LiveScene,"ccs_scene_game",Path.Combine(root,"scenes","game.html"),true),
            new Def(settings.Obs.PauseScene,"ccs_scene_pause",Path.Combine(root,"scenes","pause.html"),true),
            new Def("Metaschutz","ccs_scene_metaschutz",Path.Combine(root,"scenes","metaschutz.html"),true),
            new Def("Reactions","ccs_scene_reactions",Path.Combine(root,"scenes","reactions.html"),true),
            new Def(settings.Obs.EndScene,"ccs_scene_ende",Path.Combine(root,"scenes","ende.html"),true)
        ];

        var result = new List<string>();
        foreach (Def? d in definitions.Where(x => x.Enabled))
        {
            if (!File.Exists(d.File))
            {
                throw new FileNotFoundException("Overlay-Datei fehlt.", d.File);
            }

            await EnsureAsync(d, settings.Overlay.Width, settings.Overlay.Height, ct);
            result.Add(d.Scene + " → " + d.Source);
        }

        _logger.Write(AppLogLevel.Information, "OBS", "Browserquellen eingerichtet.");
        return result;
    }

    private async Task EnsureAsync(Def d, int width, int height, CancellationToken ct)
    {
        await _obs.EnsureSceneAsync(d.Scene, ct);
        var inputSettings = new
        {
            is_local_file = true,
            local_file = d.File,
            width,
            height,
            reroute_audio = false,
            restart_when_active = true,
            shutdown = true
        };

        if (!await _obs.InputExistsAsync(d.Source, ct))
        {
            await _obs.CreateInputAsync(d.Scene, d.Source, "browser_source", inputSettings, true, ct);
        }
        else
        {
            await _obs.SetInputSettingsAsync(d.Source, inputSettings, false, ct);
            if (!await _obs.SceneItemExistsAsync(d.Scene, d.Source, ct))
            {
                await _obs.CreateSceneItemAsync(d.Scene, d.Source, true, ct);
            }
        }

        await _obs.SetSceneItemTransformAsync(d.Scene, d.Source, 0, 0, width, height, ct);
    }

    public async Task<string> InstallContentAsync(string scene, string contentType, CancellationToken ct = default)
    {
        if (!_obs.IsConnected)
        {
            throw new InvalidOperationException("OBS ist nicht verbunden.");
        }

        if (string.IsNullOrWhiteSpace(scene))
        {
            throw new InvalidOperationException("Bitte eine OBS-Szene auswählen.");
        }

        AppSettings settings = await _settingsStore.LoadAsync(ct);
        string root = await _overlay.GetOverlayRootAsync(ct);
        string safe = (contentType ?? "content-name").Trim().ToLowerInvariant();
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "content-name", "scene-text", "start-timer", "spotify-info", "live-info", "meta-status", "pause-text", "stream-stats", "reaction-title", "reaction-frame", "reaction-text", "frame" };
        if (!allowed.Contains(safe))
        {
            throw new InvalidOperationException("Unbekanntes Content-Modul.");
        }

        string file = Path.Combine(root, "modules", safe + ".html");
        string source = "ccs_" + safe.Replace('-', '_');
        await EnsureAsync(new Def(scene, source, file, true), settings.Overlay.Width, settings.Overlay.Height, ct);
        return scene + " → " + source + " wurde eingefügt.";
    }

    public async Task<string> InstallGoalAsync(string goalType, CancellationToken ct = default)
    {
        if (!_obs.IsConnected)
        {
            throw new InvalidOperationException("OBS ist nicht verbunden.");
        }

        AppSettings settings = await _settingsStore.LoadAsync(ct);
        string root = await _overlay.GetOverlayRootAsync(ct);
        string normalized = (goalType ?? "").Trim().ToLowerInvariant();
        string file = normalized switch
        {
            "follower" => Path.Combine(root, "modules", "follower-goal.html"),
            "sub" => Path.Combine(root, "modules", "sub-goal.html"),
            "donation" => Path.Combine(root, "modules", "donation-goal.html"),
            _ => throw new ArgumentOutOfRangeException(nameof(goalType))
        };
        string source = "ccs_" + normalized + "_goal";
        string goalScene = string.IsNullOrWhiteSpace(settings.Obs.GoalOverlayScene)
            ? "CCS Ziele & Overlay-Daten"
            : settings.Obs.GoalOverlayScene.Trim();
        await EnsureAsync(new Def(goalScene, source, file, true), settings.Overlay.Width, settings.Overlay.Height, ct);
        return goalScene + " → " + source + " wurde eingerichtet. Die Szene kann in beliebigen OBS-Szenen als Szenenquelle hinzugefügt werden.";
    }

    public async Task<string> InstallAllGoalsAsync(CancellationToken ct = default)
    {
        if (!_obs.IsConnected)
        {
            throw new InvalidOperationException("OBS ist nicht verbunden.");
        }

        AppSettings settings = await _settingsStore.LoadAsync(ct);
        string root = await _overlay.GetOverlayRootAsync(ct);
        string goalScene = string.IsNullOrWhiteSpace(settings.Obs.GoalOverlayScene)
            ? "CCS Ziele & Overlay-Daten"
            : settings.Obs.GoalOverlayScene.Trim();

        Def[] definitions =
        [
            new Def(goalScene,"ccs_follower_goal",Path.Combine(root,"modules","follower-goal.html"),true),
            new Def(goalScene,"ccs_sub_goal",Path.Combine(root,"modules","sub-goal.html"),true),
            new Def(goalScene,"ccs_donation_goal",Path.Combine(root,"modules","donation-goal.html"),true)
        ];

        foreach (Def? definition in definitions)
        {
            if (!File.Exists(definition.File))
            {
                throw new FileNotFoundException("Overlay-Datei fehlt.", definition.File);
            }

            await EnsureAsync(definition, settings.Overlay.Width, settings.Overlay.Height, ct);
        }

        _logger.Write(AppLogLevel.Information, "OBS", $"Ziel-Overlay-Szene '{goalScene}' eingerichtet.");
        return $"Die OBS-Szene '{goalScene}' wurde mit Follower-, Sub- und Donation-Ziel eingerichtet. Du kannst diese Szene jetzt in anderen OBS-Szenen als Quelle hinzufügen.";
    }

    private sealed record Def(string Scene, string Source, string File, bool Enabled);
}
