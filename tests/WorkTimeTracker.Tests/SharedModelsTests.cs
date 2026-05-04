using WorkTimeTracker.Shared.Models;
using Xunit;

namespace WorkTimeTracker.Tests;

public class SharedModelsTests
{
    [Fact]
    public void Employee_DefaultsToActive()
    {
        var e = new Employee { SamAccountName = "ivanov", DisplayName = "Ivan Ivanov" };

        Assert.True(e.IsActive);
        Assert.NotNull(e.Sessions);
        Assert.Empty(e.Sessions);
    }

    [Fact]
    public void RdpSession_StateEnum_HasExpectedValues()
    {
        Assert.True(Enum.IsDefined(typeof(RdpSessionState), RdpSessionState.Active));
        Assert.True(Enum.IsDefined(typeof(RdpSessionState), RdpSessionState.Disconnected));
        Assert.True(Enum.IsDefined(typeof(RdpSessionState), RdpSessionState.Locked));
        Assert.True(Enum.IsDefined(typeof(RdpSessionState), RdpSessionState.Ended));
    }

    [Fact]
    public void ActivityEventType_CoversCoreLifecycle()
    {
        Assert.True(Enum.IsDefined(typeof(ActivityEventType), ActivityEventType.SessionLogon));
        Assert.True(Enum.IsDefined(typeof(ActivityEventType), ActivityEventType.SessionLogoff));
        Assert.True(Enum.IsDefined(typeof(ActivityEventType), ActivityEventType.RemoteConnect));
    }
}
