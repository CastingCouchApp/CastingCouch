namespace CreatorControlSuite.Core.Configuration;

public sealed class WorkflowSettings
{
    public List<TimedAutomationRuleSettings> TimedAutomations { get; set; } = [];
    public List<RunOfShowStepSettings> RunOfShowSteps { get; set; } = [];
    public List<RunOfShowPlanSettings> RunOfShowPlans { get; set; } = [];
    public string ActiveRunOfShowPlanId { get; set; } = "";
    public int StartCountdownSeconds { get; set; } = 600;
    public string CountdownLabel { get; set; } = "Countdown";
    public int EndSceneSeconds { get; set; } = 60;
    public bool ExportSessionReport { get; set; } = true;
    public bool AutoPrepareNextStream { get; set; } = true;
    public bool AutoStartSpotifyPlaylist { get; set; } = true;
    public bool AutoFadeSpotifyOnLive { get; set; } = true;
    public bool AutoPlayEndMusic { get; set; } = false;
    public bool PauseSpotifyOnStreamEnd { get; set; } = true;
    public bool AutoSwitchScenes { get; set; } = false;
    public bool AutoStartObsStream { get; set; } = false;
    public bool AutoStopObsStream { get; set; } = true;
    public int ViewerSampleSeconds { get; set; } = 15;
}


public sealed class RunOfShowPlanSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Standard";
    public List<RunOfShowStepSettings> Steps { get; set; } = [];
}

public sealed class RunOfShowStepSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Neuer Regieschritt";
    public bool Enabled { get; set; } = true;
    public string ObsScene { get; set; } = "";
    public string TransitionName { get; set; } = "";
    public int TransitionDurationMilliseconds { get; set; } = 1000;
    public string SpotifyAction { get; set; } = "None";
    public int SpotifyVolumePercent { get; set; } = 35;
    public string SpotifyPlaylistUri { get; set; } = "";
    public bool SpotifyPlaylistShuffle { get; set; } = true;
    public int SpotifyActionDelaySeconds { get; set; }
    public int SpotifyFadeSeconds { get; set; }
    public int SpotifyPriority { get; set; }
    public string StreamerBotActionId { get; set; } = "";
    public string StreamerBotActionName { get; set; } = "";
    public int ActionDelayMilliseconds { get; set; }
    public bool ContinueOnActionError { get; set; }
    public bool UpdateTwitchChannel { get; set; }
    public string TwitchTitle { get; set; } = "";
    public string TwitchCategoryId { get; set; } = "";
    public string TwitchCategoryName { get; set; } = "";
    public bool ContinueOnTwitchError { get; set; }
    public bool AutoAdvance { get; set; }
    public int AutoAdvanceDelaySeconds { get; set; } = 10;
}

public sealed class TimedAutomationRuleSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Neue Automatisierung";
    public bool Enabled { get; set; } = true;
    public string TriggerType { get; set; } = "SceneElapsed";
    public string TriggerScene { get; set; } = "";
    public int DelaySeconds { get; set; } = 10;
    public string ActionType { get; set; } = "SwitchScene";
    public string ObsScene { get; set; } = "";
    public string ObsSource { get; set; } = "";
    public bool SourceVisible { get; set; }
    public string TargetScene { get; set; } = "";
    public string TransitionName { get; set; } = "";
    public int TransitionDurationMilliseconds { get; set; } = 1000;
    /// <summary>Start | Stop — nur bei ActionType OverlayCountdown.</summary>
    public string OverlayCountdownAction { get; set; } = "Start";
    /// <summary>0 = Workflow.StartCountdownSeconds nutzen.</summary>
    public int OverlayCountdownSeconds { get; set; }
    public string SpotifyAction { get; set; } = "None";
    public int SpotifyVolumePercent { get; set; } = 35;
    public string SpotifyPlaylistUri { get; set; } = "";
    public bool SpotifyPlaylistShuffle { get; set; } = true;
    public int SpotifyActionDelaySeconds { get; set; }
    public int SpotifyFadeSeconds { get; set; }
    public int SpotifyPriority { get; set; }
    public string SpotifyAutomationGroup { get; set; } = "Standard";
    public bool SpotifyExclusiveGroup { get; set; } = true;
    public bool SpotifySavePreviousState { get; set; }
    public bool SpotifyAutoRestorePreviousState { get; set; }
    public int SpotifyAutoRestoreDelaySeconds { get; set; } = 30;
    public bool SpotifyAutoRestoreRequireSameScene { get; set; } = true;
    public bool SpotifyAutoRestoreRequireSameGroup { get; set; } = true;
    public bool SpotifyAutoRestoreRequireUnchangedPlayback { get; set; } = true;
    public bool ResetSourceAtStreamEnd { get; set; }
    public bool ResetSourceVisible { get; set; } = true;
    public bool OncePerStream { get; set; } = true;
    public string ObsInput { get; set; } = "";
    public bool InputMuted { get; set; } = true;
    public string StreamerBotActionId { get; set; } = "";
    public string StreamerBotActionName { get; set; } = "";
    public string ConditionType { get; set; } = "None";
    public string ConditionValue { get; set; } = "";
    public bool ConditionNegated { get; set; }
    public string NextRuleId { get; set; } = "";
    public int NextRuleDelaySeconds { get; set; }
    public bool ContinueChainOnError { get; set; }
    public int Priority { get; set; }
    public string ExecutionMode { get; set; } = "SkipIfRunning";
    public int TimeoutSeconds { get; set; } = 60;
    public string ScheduleTime { get; set; } = "20:00";
    public string ScheduleDays { get; set; } = "Monday,Tuesday,Wednesday,Thursday,Friday,Saturday,Sunday";
    public string ScheduleDate { get; set; } = "";
    public string ActiveFromDate { get; set; } = "";
    public string ActiveUntilDate { get; set; } = "";
    public string ExcludedDates { get; set; } = "";
    public string BlackoutRanges { get; set; } = "";
    public string MissedRunBehavior { get; set; } = "SameDay";
    public int CatchUpGraceMinutes { get; set; } = 30;
    public string DependencyRuleId { get; set; } = "";
    public string DependencyRequiredStatus { get; set; } = "Erfolgreich";
    public int RetryCount { get; set; }
    public int RetryDelaySeconds { get; set; } = 5;
    public string FailureRuleId { get; set; } = "";
    public string WorkflowGroup { get; set; } = "";
    public int WorkflowOrder { get; set; }
    public bool StartWorkflowGroup { get; set; }
    public string WorkflowFailureMode { get; set; } = "Stop";
    public string RollbackRuleId { get; set; } = "";
    public double DesignerX { get; set; }
    public double DesignerY { get; set; }
    public string DesignerNodeType { get; set; } = "Action";
    public string LastScheduledRunDate { get; set; } = "";
    public string LastRunAt { get; set; } = "";
    public string LastRunStatus { get; set; } = "Noch nie";
    public int SuccessfulRuns { get; set; }
    public int FailedRuns { get; set; }
    public int SkippedRuns { get; set; }
}

