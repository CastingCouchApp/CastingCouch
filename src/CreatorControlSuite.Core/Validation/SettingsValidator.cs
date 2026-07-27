using CreatorControlSuite.Core.Configuration;

namespace CreatorControlSuite.Core.Validation;

public sealed class SettingsValidator : ISettingsValidator
{
    public ValidationReport Validate(AppSettings settings)
    {
        var issues = new List<ValidationIssue>();

        ValidateObs(settings, issues);
        ValidateTwitch(settings, issues);
        ValidateSpotify(settings, issues);
        ValidateAlerts(settings, issues);
        ValidateOverlay(settings, issues);
        ValidateWorkflow(settings, issues);

        return new ValidationReport(
            !issues.Any(issue =>
                issue.Severity == ValidationSeverity.Error),
            issues);
    }

    private static void ValidateObs(
        AppSettings settings,
        ICollection<ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(settings.Obs.Host))
        {
            issues.Add(Error(
                "OBS_HOST_EMPTY",
                "OBS",
                "OBS-Host ist leer.",
                "127.0.0.1 eintragen."));
        }

        if (settings.Obs.Port is < 1 or > 65535)
        {
            issues.Add(Error(
                "OBS_PORT_INVALID",
                "OBS",
                "OBS-Port ist ungültig.",
                "Standardport 4455 verwenden."));
        }

        foreach ((string? name, string? value) in new[]
        {
            ("Startszene", settings.Obs.StartScene),
            ("Live-Szene", settings.Obs.LiveScene),
            ("Pausenszene", settings.Obs.PauseScene),
            ("Endszene", settings.Obs.EndScene)
        })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(Error(
                    "OBS_SCENE_EMPTY",
                    "OBS",
                    name + " ist nicht festgelegt.",
                    "Den exakten OBS-Szenennamen eintragen."));
            }
        }
    }

    private static void ValidateTwitch(
        AppSettings settings,
        ICollection<ValidationIssue> issues)
    {
        if (settings.Twitch.AutoConnect &&
            string.IsNullOrWhiteSpace(settings.Twitch.ClientId))
        {
            issues.Add(Warning(
                "TWITCH_CLIENT_ID_EMPTY",
                "Twitch",
                "Automatische Verbindung ist aktiv, aber die Client-ID fehlt.",
                "Client-ID eintragen oder Auto-Connect deaktivieren."));
        }

        if (settings.Twitch.EnableChat &&
            !settings.Twitch.Scopes.Contains(
                "user:read:chat",
                StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(Error(
                "TWITCH_CHAT_SCOPE_MISSING",
                "Twitch",
                "Der Chat-Lesebereich fehlt.",
                "Scope user:read:chat ergänzen."));
        }
    }

    private static void ValidateSpotify(
        AppSettings settings,
        ICollection<ValidationIssue> issues)
    {
        if (settings.Spotify.AutoConnect &&
            string.IsNullOrWhiteSpace(settings.Spotify.ClientId))
        {
            issues.Add(Warning(
                "SPOTIFY_CLIENT_ID_EMPTY",
                "Spotify",
                "Automatische Verbindung ist aktiv, aber die Client-ID fehlt.",
                "Client-ID eintragen oder Auto-Connect deaktivieren."));
        }

        if (!Uri.TryCreate(
                settings.Spotify.RedirectUri,
                UriKind.Absolute,
                out Uri? redirect) ||
            !string.Equals(
                redirect.Host,
                "127.0.0.1",
                StringComparison.Ordinal))
        {
            issues.Add(Error(
                "SPOTIFY_REDIRECT_INVALID",
                "Spotify",
                "Spotify Redirect-URI ist ungültig.",
                "http://127.0.0.1:43821/callback/ verwenden."));
        }

        if (settings.Spotify.StartVolumePercent is < 0 or > 100)
        {
            issues.Add(Error(
                "SPOTIFY_VOLUME_INVALID",
                "Spotify",
                "Startlautstärke liegt nicht zwischen 0 und 100.",
                "Wert zwischen 0 und 100 eintragen."));
        }
    }

    private static void ValidateAlerts(
        AppSettings settings,
        ICollection<ValidationIssue> issues)
    {
        if (settings.Alerts.QueueCapacity < 1)
        {
            issues.Add(Error(
                "ALERT_QUEUE_INVALID",
                "Alerts",
                "Alert-Queue muss mindestens einen Eintrag aufnehmen.",
                "Queue-Kapazität auf mindestens 1 setzen."));
        }

        foreach (AlertDefinitionSettings definition in settings.Alerts.Definitions.Values)
        {
            if (definition.DurationSeconds < 1)
            {
                issues.Add(Error(
                    "ALERT_DURATION_INVALID",
                    "Alerts",
                    $"Alert {definition.Type} hat eine ungültige Dauer.",
                    "Dauer auf mindestens eine Sekunde setzen."));
            }

            if (!string.IsNullOrWhiteSpace(definition.MediaPath) &&
                !File.Exists(definition.MediaPath))
            {
                issues.Add(Warning(
                    "ALERT_MEDIA_MISSING",
                    "Alerts",
                    $"Mediendatei für {definition.Type} wurde nicht gefunden.",
                    definition.MediaPath));
            }
        }
    }

    private static void ValidateOverlay(
        AppSettings settings,
        ICollection<ValidationIssue> issues)
    {
        if (!string.IsNullOrWhiteSpace(settings.Overlay.RootPath))
        {
            try
            {
                _ = Path.GetFullPath(settings.Overlay.RootPath);
            }
            catch
            {
                issues.Add(Error(
                    "OVERLAY_PATH_INVALID",
                    "Overlay",
                    "Overlay-Pfad ist ungültig.",
                    "Einen vollständigen lokalen Pfad wählen."));
            }
        }

        settings.Overlay.Instances ??= [];
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (OverlayInstanceSettings instance in settings.Overlay.Instances)
        {
            if (string.IsNullOrWhiteSpace(instance.Id))
            {
                issues.Add(Error(
                    "OVERLAY_INSTANCE_ID_EMPTY",
                    "Overlay",
                    "Overlay-Instanz ohne Id.",
                    "Jeder Overlay-Eintrag braucht eine Id."));
                continue;
            }

            if (!seenIds.Add(instance.Id.Trim()))
            {
                issues.Add(Error(
                    "OVERLAY_INSTANCE_ID_DUPLICATE",
                    "Overlay",
                    $"Doppelte Overlay-Id „{instance.Id}“.",
                    "Ids der Overlay-Instanzen müssen eindeutig sein."));
            }

            if (string.IsNullOrWhiteSpace(instance.RootPath))
            {
                continue;
            }

            if (instance.RootPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                issues.Add(Error(
                    "OVERLAY_INSTANCE_PATH_INVALID",
                    "Overlay",
                    $"Overlay-Pfad für „{instance.Name}“ ist ungültig.",
                    "Einen vollständigen lokalen Pfad wählen."));
                continue;
            }

            try
            {
                _ = Path.GetFullPath(instance.RootPath);
            }
            catch
            {
                issues.Add(Error(
                    "OVERLAY_INSTANCE_PATH_INVALID",
                    "Overlay",
                    $"Overlay-Pfad für „{instance.Name}“ ist ungültig.",
                    "Einen vollständigen lokalen Pfad wählen."));
            }
        }
    }

    private static void ValidateWorkflow(
        AppSettings settings,
        ICollection<ValidationIssue> issues)
    {
        if (settings.Workflow.StartCountdownSeconds < 0)
        {
            issues.Add(Error(
                "COUNTDOWN_INVALID",
                "Stream-Workflow",
                "Countdown darf nicht negativ sein.",
                "Countdown auf 0 oder mehr Sekunden setzen."));
        }

        if (settings.Workflow.EndSceneSeconds < 1)
        {
            issues.Add(Error(
                "END_SCENE_DURATION_INVALID",
                "Stream-Workflow",
                "Endszene muss mindestens eine Sekunde sichtbar sein.",
                "60 Sekunden werden empfohlen."));
        }
    }

    private static ValidationIssue Error(
        string code,
        string section,
        string message,
        string fix)
    {
        return new ValidationIssue(
            code,
            ValidationSeverity.Error,
            section,
            message,
            fix);
    }

    private static ValidationIssue Warning(
        string code,
        string section,
        string message,
        string fix)
    {
        return new ValidationIssue(
            code,
            ValidationSeverity.Warning,
            section,
            message,
            fix);
    }
}
