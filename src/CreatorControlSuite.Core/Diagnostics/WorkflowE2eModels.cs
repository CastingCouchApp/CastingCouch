namespace CreatorControlSuite.Core.Diagnostics;

public sealed record WorkflowE2eStepResult(string Step, bool Success, TimeSpan Duration, string Detail);
public sealed record WorkflowE2eReport(DateTimeOffset StartedAt, DateTimeOffset CompletedAt, bool Success, IReadOnlyList<WorkflowE2eStepResult> Steps);
public interface IWorkflowE2eService { Task<WorkflowE2eReport> RunAsync(CancellationToken cancellationToken = default); }
