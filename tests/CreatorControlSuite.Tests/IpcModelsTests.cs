using CreatorControlSuite.Core.Ipc;

namespace CreatorControlSuite.Tests;

public sealed class IpcModelsTests
{
    [Fact]
    public void WorkflowCommandNamesRemainStable()
    {
        Assert.Equal("workflow.prepare", IpcCommandNames.Prepare);
        Assert.Equal("workflow.live", IpcCommandNames.Live);
        Assert.Equal("workflow.end", IpcCommandNames.End);
    }
}
