namespace WorkTimeTracker.Shared.Models;

public class Employee
{
    public Guid Id { get; set; }
    public string SamAccountName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Department { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public ICollection<RdpSession> Sessions { get; set; } = new List<RdpSession>();
}
