using Microsoft.Extensions.Logging;

namespace WorkTimeTracker.Agent.Services;

// Stub for the WTS session monitor.
//
// Target host is **Windows 10 / 11** (client SKUs), not Windows Server.
// On client Windows only one interactive session is active at a time —
// either the console (locally signed-in user) or one RDP connection.
// Connecting via RDP automatically disconnects the console session, and
// vice versa. The agent must therefore track at most one active session
// timer per machine and switch its session-type label on console <-> remote
// transitions.
//
// Full implementation needs to:
//   1. Create a hidden message-only window via CreateWindowEx (HWND_MESSAGE).
//   2. Call WTSRegisterSessionNotification(hwnd, NOTIFY_FOR_ALL_SESSIONS)
//      from wtsapi32.dll.
//   3. Pump messages on a dedicated thread; handle WM_WTSSESSION_CHANGE.
//   4. Map wParam codes:
//        WTS_CONSOLE_CONNECT/DISCONNECT,
//        WTS_REMOTE_CONNECT/DISCONNECT,
//        WTS_SESSION_LOGON/LOGOFF,
//        WTS_SESSION_LOCK/UNLOCK.
//   5. Determine session type:
//        WTSQuerySessionInformation(WTSClientProtocolType)
//          0 = console, 2 = RDP. Tag every RdpSessionEvent accordingly.
//   6. Resolve session -> username via WTSQuerySessionInformation(WTSUserName).
//   7. Raise SessionEvent for the worker to enqueue.
//
// On non-Windows hosts, fall back to no-op so unit tests run on Linux CI if needed.

public class RdpSessionMonitor : IRdpSessionMonitor
{
    private readonly ILogger<RdpSessionMonitor> _logger;

    public event EventHandler<RdpSessionEvent>? SessionEvent;

    public RdpSessionMonitor(ILogger<RdpSessionMonitor> logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        _logger.LogInformation("RdpSessionMonitor.Start() — TODO: hook WTSRegisterSessionNotification");
    }

    public void Stop()
    {
        _logger.LogInformation("RdpSessionMonitor.Stop()");
    }

    protected virtual void OnSessionEvent(RdpSessionEvent evt) => SessionEvent?.Invoke(this, evt);
}
