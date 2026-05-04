namespace WorkTimeTracker.Shared.Models;

public enum ActivityEventType
{
    SessionLogon,
    SessionLogoff,
    SessionLock,
    SessionUnlock,
    RemoteConnect,
    RemoteDisconnect,
    ConsoleConnect,
    ConsoleDisconnect,
    ProcessStart,
    ProcessExit,
    ForegroundWindowChanged,
    UrlVisited,
    IdleStart,
    IdleEnd,
    AgentStarted,
    AgentStopped
}

public class ActivityEvent
{
    public Guid Id { get; set; }
    public Guid RdpSessionId { get; set; }
    public RdpSession? RdpSession { get; set; }

    public ActivityEventType Type { get; set; }
    public DateTime Timestamp { get; set; }

    public string? ProcessName { get; set; }
    public string? WindowTitle { get; set; }
    public string? Url { get; set; }
    public string? PayloadJson { get; set; }
}
