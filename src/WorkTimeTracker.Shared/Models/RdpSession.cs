namespace WorkTimeTracker.Shared.Models;

public enum RdpSessionState
{
    Active,
    Disconnected,
    Locked,
    Ended
}

public class RdpSession
{
    public Guid Id { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }
    public Guid ServerHostId { get; set; }
    public ServerHost? ServerHost { get; set; }

    public int WtsSessionId { get; set; }
    public string ClientName { get; set; } = string.Empty;
    public string? ClientAddress { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public TimeSpan? ActiveDuration { get; set; }
    public TimeSpan? IdleDuration { get; set; }

    public RdpSessionState State { get; set; }
}
