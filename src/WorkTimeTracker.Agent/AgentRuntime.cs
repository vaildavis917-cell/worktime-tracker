namespace WorkTimeTracker.Agent;

/// <summary>
/// Mutable runtime state shared between the worker, uploader and screenshot
/// loop. Updated from the heartbeat response so the server can dynamically
/// shorten the screenshot interval when an admin enables live-view.
/// </summary>
public class AgentRuntime
{
    private readonly object _lock = new();
    private TimeSpan _screenshotInterval;

    public AgentRuntime(TimeSpan defaultInterval)
    {
        _screenshotInterval = defaultInterval;
    }

    public TimeSpan ScreenshotInterval
    {
        get
        {
            lock (_lock) return _screenshotInterval;
        }
    }

    public void SetScreenshotInterval(TimeSpan value)
    {
        if (value < TimeSpan.FromSeconds(1))
        {
            value = TimeSpan.FromSeconds(1);
        }
        if (value > TimeSpan.FromMinutes(10))
        {
            value = TimeSpan.FromMinutes(10);
        }
        lock (_lock)
        {
            _screenshotInterval = value;
        }
    }
}
