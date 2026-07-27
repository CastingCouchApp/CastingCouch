using CreatorControlSuite.App.Services;
using CreatorControlSuite.Modules.Workflow.Models;

namespace CreatorControlSuite.App.Core.Eventing;

public sealed record WorkflowPhaseChanged(
    StreamPhase Phase,
    string Detail,
    DateTimeOffset Timestamp);

public sealed record MusicSnapshotUpdated(
    MusicPlayerUiState State,
    DateTimeOffset Timestamp);

public sealed record ModuleConnectionChanged(
    string ModuleId,
    bool Connected,
    string Detail,
    DateTimeOffset Timestamp);
