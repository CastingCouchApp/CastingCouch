using CreatorControlSuite.Core.Security;

namespace CreatorControlSuite.Core.Updates;

public interface IRemoteUpdateTransport
{
    Task<bool> StageAsync(
        PairedAgentDevice device,
        RemoteUpdatePackage package,
        CancellationToken cancellationToken);

    Task<bool> ExecuteAsync(
        PairedAgentDevice device,
        string action,
        RemoteUpdateActionOptions options,
        CancellationToken cancellationToken);
}

public sealed record RemoteUpdatePackage(
    string FileName,
    byte[] Content,
    SignedUpdateManifest Manifest);

public sealed record RemoteUpdateActionOptions(
    bool RestartSuite,
    bool AutomaticRollback);

public sealed record RemoteUpdateRolloutOptions(
    int CanaryCount,
    TimeSpan DelayBetweenDevices,
    int MaximumFailurePercent,
    bool StopOnFailureThreshold,
    bool RestartSuite,
    bool AutomaticRollback);

public enum RemoteUpdateRolloutStopReason
{
    Completed,
    CanaryFailed,
    FailureThresholdExceeded
}

public sealed record RemoteUpdateRolloutProgress(
    string DeviceId,
    string DeviceName,
    string Phase,
    string Status,
    int Attempted,
    int Succeeded,
    int Failed,
    int Total);

public sealed record RemoteUpdateRolloutResult(
    int Attempted,
    int Succeeded,
    int Failed,
    RemoteUpdateRolloutStopReason StopReason);

public sealed class RemoteUpdateRolloutService(
    IRemoteUpdateTransport transport)
{
    public async Task<RemoteUpdateRolloutResult> RunAsync(
        IReadOnlyList<PairedAgentDevice> targets,
        RemoteUpdatePackage package,
        RemoteUpdateRolloutOptions options,
        IProgress<RemoteUpdateRolloutProgress>? progress = null,
        Func<CancellationToken, Task>? waitForMaintenanceWindow = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(package);
        int canaryCount = Math.Clamp(options.CanaryCount, 0, targets.Count);
        int maximumFailurePercent =
            Math.Clamp(options.MaximumFailurePercent, 0, 100);
        int attempted = 0;
        int succeeded = 0;
        int failed = 0;
        var actionOptions = new RemoteUpdateActionOptions(
            options.RestartSuite,
            options.AutomaticRollback);

        for (int index = 0; index < targets.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (waitForMaintenanceWindow is not null)
            {
                await waitForMaintenanceWindow(cancellationToken);
            }

            PairedAgentDevice device = targets[index];
            string phase = index < canaryCount ? "CANARY" : "ROLLOUT";
            attempted++;
            Report("Staging", device, phase);
            bool staged = await transport.StageAsync(
                device,
                package,
                cancellationToken);
            if (!staged)
            {
                failed++;
                Report("StageFailed", device, phase);
            }
            else
            {
                Report("Validating", device, phase);
                bool validated = await transport.ExecuteAsync(
                    device,
                    "validate",
                    actionOptions,
                    cancellationToken);
                if (!validated)
                {
                    failed++;
                    Report("ValidationFailed", device, phase);
                }
                else
                {
                    Report("Applying", device, phase);
                    bool applied = await transport.ExecuteAsync(
                        device,
                        "apply",
                        actionOptions,
                        cancellationToken);
                    if (applied)
                    {
                        succeeded++;
                        Report("InstallationStarted", device, phase);
                    }
                    else
                    {
                        failed++;
                        Report("ApplyFailed", device, phase);
                    }
                }
            }

            int failurePercent = attempted == 0
                ? 0
                : (int)Math.Round(failed * 100d / attempted);
            if (options.StopOnFailureThreshold &&
                failed > 0 &&
                failurePercent > maximumFailurePercent)
            {
                return Result(
                    RemoteUpdateRolloutStopReason.FailureThresholdExceeded);
            }

            if (canaryCount > 0 &&
                index + 1 == canaryCount &&
                failed > 0)
            {
                return Result(RemoteUpdateRolloutStopReason.CanaryFailed);
            }

            if (index < targets.Count - 1 &&
                options.DelayBetweenDevices > TimeSpan.Zero)
            {
                await Task.Delay(
                    options.DelayBetweenDevices,
                    cancellationToken);
            }
        }

        return Result(RemoteUpdateRolloutStopReason.Completed);

        void Report(
            string status,
            PairedAgentDevice device,
            string phase) =>
            progress?.Report(new RemoteUpdateRolloutProgress(
                device.Id,
                device.Name,
                phase,
                status,
                attempted,
                succeeded,
                failed,
                targets.Count));

        RemoteUpdateRolloutResult Result(
            RemoteUpdateRolloutStopReason reason) =>
            new(attempted, succeeded, failed, reason);
    }
}
