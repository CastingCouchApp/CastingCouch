namespace CreatorControlSuite.App.Views.Pages.Workflow;

public sealed record WorkflowPageActions(
    Func<Task> PrepareAsync,
    Func<Task> StartCountdownAsync,
    Func<Task> StopCountdownAsync,
    Func<Task> GoLiveAsync,
    Func<Task> PauseAsync,
    Func<Task> ResumeAsync,
    Func<Task> EndAsync);
