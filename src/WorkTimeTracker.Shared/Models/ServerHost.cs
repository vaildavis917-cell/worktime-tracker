namespace WorkTimeTracker.Shared.Models;

public class ServerHost
{
    public Guid Id { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AgentToken { get; set; } = string.Empty;
    public DateTime? LastHeartbeatAt { get; set; }
    public string? AgentVersion { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
