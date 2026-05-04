namespace WorkTimeTracker.Agent;

public class AgentOptions
{
    public string ServerUrl { get; set; } = "https://localhost:7001";
    public string AgentToken { get; set; } = string.Empty;
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan PeriodicScreenshotInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan EventBatchInterval { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan IdleThreshold { get; set; } = TimeSpan.FromMinutes(3);

    public string[] ScreenshotTriggerProcesses { get; set; } =
    {
        "Telegram", "telegram-desktop", "WhatsApp", "Discord",
        "chrome", "msedge", "firefox", "opera", "brave"
    };
}
