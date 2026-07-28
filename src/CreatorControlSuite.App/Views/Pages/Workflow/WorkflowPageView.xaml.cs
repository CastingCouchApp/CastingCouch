using System.Windows;
using System.Windows.Controls;

namespace CreatorControlSuite.App.Views.Pages.Workflow;

public partial class WorkflowPageView : UserControl
{
    public WorkflowPageView()
    {
        InitializeComponent();
        PrepareStreamButton.Click += async (_, _) =>
            await InvokeAsync(actions => actions.PrepareAsync());
        StartCountdownButton.Click += async (_, _) =>
            await InvokeAsync(actions => actions.StartCountdownAsync());
        StopCountdownButton.Click += async (_, _) =>
            await InvokeAsync(actions => actions.StopCountdownAsync());
        GoLiveButton.Click += async (_, _) =>
            await InvokeAsync(actions => actions.GoLiveAsync());
        PauseStreamButton.Click += async (_, _) =>
            await InvokeAsync(actions => actions.PauseAsync());
        ResumeStreamButton.Click += async (_, _) =>
            await InvokeAsync(actions => actions.ResumeAsync());
        EndStreamButton.Click += async (_, _) =>
            await InvokeAsync(actions => actions.EndAsync());
    }

    public WorkflowPageActions? Actions { get; set; }

    public void ApplyStatus(string phase, string detail)
        => WorkflowStatusText.Text = phase + " · " + detail;

    public void ShowShortStreamTest(string status)
    {
        WorkflowTabControl.SelectedIndex = 4;
        ShortStreamTestViewHost.SetStatus(status);
    }

    private async Task InvokeAsync(
        Func<WorkflowPageActions, Task> action)
    {
        try
        {
            await action(Actions ??
                throw new InvalidOperationException(
                    "Workflow-Aktionen sind nicht konfiguriert."));
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Workflow",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }
}
