using CreatorControlSuite.Core.Updates;

internal sealed record CommandRequest(string Command);
internal sealed record PairingRequest(string Code, string DeviceName);
internal sealed record ObsSceneRequest(string SceneName);
internal sealed record ObsMuteRequest(string InputName, bool Muted);
internal sealed record ObsVolumeRequest(string InputName, double VolumeDb);
internal sealed record ObsSceneItemRequest(string SceneName, string SourceName, bool Enabled);
internal sealed record ObsFilterRequest(string SourceName, string FilterName, bool Enabled);
internal sealed record ObsTransformRequest(string SceneName, string SourceName, bool Reset, double X, double Y, double Width, double Height, double Rotation);
internal sealed record ObsConfigurationRequest(string ProfileName, string SceneCollectionName);
internal sealed record ObsPresetRequest(string Name);
internal sealed record ObsPresetAudio(string Name, bool Muted, double VolumeDb);
internal sealed record ObsPresetSceneItem(string SourceName, bool Enabled);
internal sealed record ObsRemotePreset(string Name, DateTimeOffset CreatedAt, string ProfileName, string SceneCollectionName, string CurrentScene, ObsPresetAudio[] AudioInputs, ObsPresetSceneItem[] SceneItems);
internal sealed record ObsVolumeFadeRequest(string InputName, double TargetVolumeDb, int DurationMilliseconds);
internal sealed record ObsOutputRequest(string Action);
internal sealed record ObsTransitionRequest(string TransitionName, int DurationMilliseconds);
internal sealed record AgentPermissions(string[] AllowedCommands)
{
    public static AgentPermissions Default { get; } = new(["obs.start", "obs.stop", "obs.control", "spotify.playpause", "streamerbot.start", "files.deploy", "updates.stage", "updates.apply"]);
}

internal sealed record FileDeployRequest(
    string FileName,
    string Base64Zip,
    SignedUpdateManifest? Manifest);
internal sealed record CommandHistoryEntry(DateTimeOffset At, string Command, string Result);
internal sealed record UpdateApplyRequest(bool RestartSuite, bool AutomaticRollback);
internal sealed record AgentUpdateHistoryEntry(DateTimeOffset At, string Action, string PackageVersion, string Sha256, bool Success, string Message);
internal sealed record AgentUpdateState(string Status, string PackageName, string StagingDirectory, string PackageDirectory, string BackupDirectory, DateTimeOffset StagedAt, DateTimeOffset? AppliedAt, string Message, string Sha256, int FileCount, bool Validated, bool MaintenanceMode, bool? AutomaticRollback, string PackageVersion, string MinimumAgentVersion, string ManifestSignature, bool SignatureValid)
{
    public static AgentUpdateState Empty { get; } = new("none", "", "", "", "", DateTimeOffset.MinValue, null, "Kein Update bereitgestellt.", "", 0, false, false, null, "", "", "", false);
}
