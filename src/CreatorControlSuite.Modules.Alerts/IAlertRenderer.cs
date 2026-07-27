using CreatorControlSuite.Modules.Alerts.Models;

namespace CreatorControlSuite.Modules.Alerts;

public interface IAlertRenderer
{
    Task InstallSourcesAsync(
        AlertDefinition definition,
        string renderedText,
        CancellationToken cancellationToken = default);

    Task ShowAsync(
        AlertDefinition definition,
        string renderedText,
        CancellationToken cancellationToken = default);

    Task HideAsync(CancellationToken cancellationToken = default);
}
