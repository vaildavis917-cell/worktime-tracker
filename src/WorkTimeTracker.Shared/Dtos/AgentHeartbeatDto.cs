namespace WorkTimeTracker.Shared.Dtos;

public record AgentHeartbeatDto(
    string Hostname,
    string AgentVersion,
    DateTime SentAtUtc,
    int ActiveSessions,
    int CurrentSessionId,
    string CurrentSamAccountName,
    string OsVersion);

public record AgentHeartbeatResponse(
    Guid ServerHostId,
    bool RequestFullSync,
    int ScreenshotIntervalSeconds);
