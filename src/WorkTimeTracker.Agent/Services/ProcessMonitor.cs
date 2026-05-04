using Microsoft.Extensions.Logging;

namespace WorkTimeTracker.Agent.Services;

// Stub for process / foreground-window monitor.
//
// Full implementation:
//   1. Process start/exit:
//        - Subscribe to WMI events Win32_ProcessStartTrace / Win32_ProcessStopTrace
//          via System.Management.ManagementEventWatcher, OR
//        - Use ETW with TraceEvent for lower overhead at high process churn.
//   2. Foreground window:
//        - SetWinEventHook(EVENT_SYSTEM_FOREGROUND, ...) running on a thread
//          that pumps messages. Resolve PID via GetWindowThreadProcessId,
//          then map PID -> ProcessName / SessionId.
//   3. Filter to RDP sessions only (skip session 0 / services).

public class ProcessMonitor : IProcessMonitor
{
    private readonly ILogger<ProcessMonitor> _logger;

    public event EventHandler<ProcessEvent>? ProcessEvent;
    public event EventHandler<ForegroundChangedEvent>? ForegroundChanged;

    public ProcessMonitor(ILogger<ProcessMonitor> logger)
    {
        _logger = logger;
    }

    public void Start() => _logger.LogInformation("ProcessMonitor.Start() — TODO");

    public void Stop() => _logger.LogInformation("ProcessMonitor.Stop()");

    protected virtual void OnProcessEvent(ProcessEvent evt) => ProcessEvent?.Invoke(this, evt);
    protected virtual void OnForegroundChanged(ForegroundChangedEvent evt) => ForegroundChanged?.Invoke(this, evt);
}
