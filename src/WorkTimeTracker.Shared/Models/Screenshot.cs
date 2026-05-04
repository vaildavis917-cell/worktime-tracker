namespace WorkTimeTracker.Shared.Models;

public enum ScreenshotTrigger
{
    Periodic,
    ProcessLaunched,
    ForegroundChanged,
    UnlockEvent,
    ManualRequest
}

public class Screenshot
{
    public Guid Id { get; set; }
    public Guid RdpSessionId { get; set; }
    public RdpSession? RdpSession { get; set; }

    public DateTime CapturedAt { get; set; }
    public ScreenshotTrigger Trigger { get; set; }
    public string? TriggerProcess { get; set; }

    public string StorageKey { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public long SizeBytes { get; set; }
}
