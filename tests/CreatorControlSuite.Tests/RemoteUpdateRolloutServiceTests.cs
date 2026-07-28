using CreatorControlSuite.Core.Security;
using CreatorControlSuite.Core.Updates;

namespace CreatorControlSuite.Tests;

public sealed class RemoteUpdateRolloutServiceTests
{
    [Fact]
    public async Task RunAsync_StopsAfterFailedCanary()
    {
        PairedAgentDevice[] targets = [Device("canary"), Device("regular")];
        var transport = new FakeTransport
        {
            StageResults = { ["canary"] = false }
        };
        var service = new RemoteUpdateRolloutService(transport);

        RemoteUpdateRolloutResult result = await service.RunAsync(
            targets,
            Package(),
            new RemoteUpdateRolloutOptions(
                CanaryCount: 1,
                DelayBetweenDevices: TimeSpan.Zero,
                MaximumFailurePercent: 100,
                StopOnFailureThreshold: false,
                RestartSuite: true,
                AutomaticRollback: true));

        Assert.Equal(RemoteUpdateRolloutStopReason.CanaryFailed, result.StopReason);
        Assert.Equal(1, result.Attempted);
        Assert.Equal(1, result.Failed);
        Assert.DoesNotContain("regular", transport.StagedDevices);
    }

    [Fact]
    public async Task RunAsync_StopsWhenFailureThresholdIsExceeded()
    {
        PairedAgentDevice[] targets =
            [Device("one"), Device("two"), Device("three")];
        var transport = new FakeTransport
        {
            ApplyResults = { ["one"] = false }
        };
        var service = new RemoteUpdateRolloutService(transport);

        RemoteUpdateRolloutResult result = await service.RunAsync(
            targets,
            Package(),
            new RemoteUpdateRolloutOptions(
                CanaryCount: 0,
                DelayBetweenDevices: TimeSpan.Zero,
                MaximumFailurePercent: 25,
                StopOnFailureThreshold: true,
                RestartSuite: true,
                AutomaticRollback: true));

        Assert.Equal(
            RemoteUpdateRolloutStopReason.FailureThresholdExceeded,
            result.StopReason);
        Assert.Equal(1, result.Attempted);
        Assert.DoesNotContain("two", transport.StagedDevices);
    }

    [Fact]
    public async Task RunAsync_StagesValidatesAndAppliesEveryTarget()
    {
        PairedAgentDevice[] targets = [Device("one"), Device("two")];
        var transport = new FakeTransport();
        var service = new RemoteUpdateRolloutService(transport);
        var progress = new List<RemoteUpdateRolloutProgress>();

        RemoteUpdateRolloutResult result = await service.RunAsync(
            targets,
            Package(),
            new RemoteUpdateRolloutOptions(
                CanaryCount: 1,
                DelayBetweenDevices: TimeSpan.Zero,
                MaximumFailurePercent: 25,
                StopOnFailureThreshold: true,
                RestartSuite: true,
                AutomaticRollback: true),
            new ImmediateProgress<RemoteUpdateRolloutProgress>(progress.Add));

        Assert.Equal(RemoteUpdateRolloutStopReason.Completed, result.StopReason);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal(2, transport.ValidatedDevices.Count);
        Assert.Equal(2, transport.AppliedDevices.Count);
        Assert.Contains(progress, item => item.Status == "InstallationStarted");
    }

    private static PairedAgentDevice Device(string id) => new(
        id,
        id,
        "127.0.0.1",
        DateTimeOffset.UtcNow,
        "secret",
        new string('A', 64),
        ["updates.stage", "updates.apply"]);

    private static RemoteUpdatePackage Package() => new(
        "update.zip",
        [1, 2, 3],
        new SignedUpdateManifest(
            UpdateManifestCanonical.ProductId,
            "8.0.0-alpha1",
            "Alpha",
            "update.zip",
            "hash",
            3,
            DateTimeOffset.UtcNow,
            "",
            "",
            "signature"));

    private sealed class FakeTransport : IRemoteUpdateTransport
    {
        public Dictionary<string, bool> StageResults { get; } = [];
        public Dictionary<string, bool> ValidateResults { get; } = [];
        public Dictionary<string, bool> ApplyResults { get; } = [];
        public List<string> StagedDevices { get; } = [];
        public List<string> ValidatedDevices { get; } = [];
        public List<string> AppliedDevices { get; } = [];

        public Task<bool> StageAsync(
            PairedAgentDevice device,
            RemoteUpdatePackage package,
            CancellationToken cancellationToken)
        {
            StagedDevices.Add(device.Id);
            return Task.FromResult(
                !StageResults.TryGetValue(device.Id, out bool result) || result);
        }

        public Task<bool> ExecuteAsync(
            PairedAgentDevice device,
            string action,
            RemoteUpdateActionOptions options,
            CancellationToken cancellationToken)
        {
            Dictionary<string, bool> results;
            if (action == "validate")
            {
                ValidatedDevices.Add(device.Id);
                results = ValidateResults;
            }
            else
            {
                AppliedDevices.Add(device.Id);
                results = ApplyResults;
            }

            return Task.FromResult(
                !results.TryGetValue(device.Id, out bool result) || result);
        }
    }

    private sealed class ImmediateProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
