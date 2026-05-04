using WorkTimeTracker.Shared.Models;

namespace WorkTimeTracker.Agent.Services;

public interface IRdpSessionMonitor
{
    void Start();
    void Stop();
    event EventHandler<RdpSessionEvent>? SessionEvent;
}

public record RdpSessionEvent(
    int WtsSessionId,
    string SamAccountName,
    ActivityEventType Type,
    DateTime TimestampUtc,
    string? ClientName,
    string? ClientAddress);
