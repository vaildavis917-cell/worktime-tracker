namespace WorkTimeTracker.Agent.Services;

public interface IProcessMonitor
{
    void Start();
    void Stop();
    event EventHandler<ProcessEvent>? ProcessEvent;
    event EventHandler<ForegroundChangedEvent>? ForegroundChanged;
}

public record ProcessEvent(
    int WtsSessionId,
    string ProcessName,
    int ProcessId,
    bool IsStart,
    DateTime TimestampUtc);

public record ForegroundChangedEvent(
    int WtsSessionId,
    string ProcessName,
    string WindowTitle,
    DateTime TimestampUtc);
