namespace WorkTimeTracker.Shared.Dtos;

public record AgentHeartbeatDto(
    string Hostname,
    string AgentVersion,
    DateTime SentAtUtc,
    int ActiveSessions);

public record AgentHeartbeatResponse(
    Guid ServerHostId,
    bool RequestFullSync);
