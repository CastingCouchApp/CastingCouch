using CreatorControlSuite.Core.Updates;

namespace CreatorControlSuite.App.Services;

public static class UpdateWorkflowPresentation
{
    public static string Format(UpdateWorkflowProgress progress) =>
        progress.Phase switch
        {
            UpdateWorkflowPhase.Downloading
                when progress.DownloadProgress.HasValue =>
                $"Update wird heruntergeladen … {progress.DownloadProgress:P0}",
            UpdateWorkflowPhase.Downloading =>
                "Update wird heruntergeladen …",
            UpdateWorkflowPhase.CreatingBackup =>
                "Backup vor Update …",
            UpdateWorkflowPhase.Applying =>
                "Updater wird gestartet. Die App wird beendet …",
            _ => "Update wird vorbereitet …"
        };
}
