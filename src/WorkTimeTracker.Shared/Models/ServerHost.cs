namespace WorkTimeTracker.Shared.Models;

// Despite the legacy name "ServerHost", this represents any machine on
// which the agent is installed — primarily Windows 10/11 workstations
// reached over RDP.
public class ServerHost
{
    public Guid Id { get; set; }
    public string Hostname { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string AgentToken { get; set; } = string.Empty;
    public DateTime? LastHeartbeatAt { get; set; }
    public string? AgentVersion { get; set; }
    public string? OsVersion { get; set; }
    public int? CurrentSessionId { get; set; }
    public string? CurrentSamAccountName { get; set; }
    public DateTime? LiveViewUntil { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}
