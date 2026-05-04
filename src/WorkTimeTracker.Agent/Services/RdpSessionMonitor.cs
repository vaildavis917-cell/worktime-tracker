using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using WorkTimeTracker.Agent.Native;
using WorkTimeTracker.Shared.Models;

namespace WorkTimeTracker.Agent.Services;

// Real WTS session monitor.
//
// Runs a dedicated STA thread that creates a hidden HWND_MESSAGE window,
// registers it with WTSRegisterSessionNotification(NOTIFY_FOR_THIS_SESSION),
// and pumps messages until Stop() posts WM_QUIT.
//
// On Win10 with single active session, the agent runs inside the user
// session via Task Scheduler "At log on" (see README). Logon/Logoff for
// the user themselves are not received in-process here — they are emitted
// synthetically by the worker on agent start/stop. What we receive in
// flight: Lock/Unlock, Remote/Console Connect/Disconnect, and the
// session-create/terminate edge cases.

[SupportedOSPlatform("windows")]
public sealed class RdpSessionMonitor : IRdpSessionMonitor, IDisposable
{
    private readonly ILogger<RdpSessionMonitor> _logger;
    private Thread? _pumpThread;
    private uint _pumpThreadId;
    private WtsMessageWindow? _window;
    private volatile bool _running;

    public event EventHandler<RdpSessionEvent>? SessionEvent;

    public RdpSessionMonitor(ILogger<RdpSessionMonitor> logger)
    {
        _logger = logger;
    }

    public void Start()
    {
        if (_running)
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("RdpSessionMonitor.Start skipped: not running on Windows");
            return;
        }

        _running = true;
        _pumpThread = new Thread(MessagePump)
        {
            IsBackground = true,
            Name = "WtsSessionMonitor"
        };
        _pumpThread.SetApartmentState(ApartmentState.STA);
        _pumpThread.Start();
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }
        _running = false;

        if (_pumpThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_pumpThreadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
        _pumpThread?.Join(TimeSpan.FromSeconds(5));
        _pumpThread = null;
        _pumpThreadId = 0;
    }

    private void MessagePump()
    {
        _pumpThreadId = NativeMethods.GetCurrentThreadId();

        try
        {
            _window = new WtsMessageWindow();
            _window.SessionChange += OnSessionChange;

            var ok = NativeMethods.WTSRegisterSessionNotification(_window.Handle, NativeMethods.NOTIFY_FOR_THIS_SESSION);
            if (!ok)
            {
                var err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                _logger.LogError("WTSRegisterSessionNotification failed (Win32 error {Err})", err);
                return;
            }

            _logger.LogInformation("WTS session monitor armed on hwnd={Hwnd}", _window.Handle);

            while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WTS message pump crashed");
        }
        finally
        {
            if (_window is not null)
            {
                NativeMethods.WTSUnRegisterSessionNotification(_window.Handle);
                _window.Dispose();
                _window = null;
            }
        }
    }

    private void OnSessionChange(object? sender, int wparam)
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var (type, label) = MapWtsCode(wparam);

        if (type is null)
        {
            _logger.LogDebug("Ignoring WTS message with wparam={Wparam}", wparam);
            return;
        }

        var sam = NativeMethods.QuerySessionString(sessionId, NativeMethods.WTSUserName)
                  ?? Environment.UserName;
        var protocol = NativeMethods.QuerySessionProtocolType(sessionId);

        var evt = new RdpSessionEvent(
            WtsSessionId: sessionId,
            SamAccountName: sam,
            Type: type.Value,
            TimestampUtc: DateTime.UtcNow,
            ClientName: protocol == 2 ? "RDP" : "console",
            ClientAddress: null);

        _logger.LogInformation("WTS event {Label} session={SessionId} sam={Sam} protocol={Proto}",
            label, sessionId, sam, protocol);

        try
        {
            SessionEvent?.Invoke(this, evt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SessionEvent handler threw");
        }
    }

    private static (ActivityEventType? Type, string Label) MapWtsCode(int wparam) => wparam switch
    {
        NativeMethods.WTS_CONSOLE_CONNECT => (ActivityEventType.ConsoleConnect, "ConsoleConnect"),
        NativeMethods.WTS_CONSOLE_DISCONNECT => (ActivityEventType.ConsoleDisconnect, "ConsoleDisconnect"),
        NativeMethods.WTS_REMOTE_CONNECT => (ActivityEventType.RemoteConnect, "RemoteConnect"),
        NativeMethods.WTS_REMOTE_DISCONNECT => (ActivityEventType.RemoteDisconnect, "RemoteDisconnect"),
        NativeMethods.WTS_SESSION_LOGON => (ActivityEventType.SessionLogon, "SessionLogon"),
        NativeMethods.WTS_SESSION_LOGOFF => (ActivityEventType.SessionLogoff, "SessionLogoff"),
        NativeMethods.WTS_SESSION_LOCK => (ActivityEventType.SessionLock, "SessionLock"),
        NativeMethods.WTS_SESSION_UNLOCK => (ActivityEventType.SessionUnlock, "SessionUnlock"),
        _ => (null, $"Unknown(0x{wparam:X})")
    };

    public void Dispose()
    {
        Stop();
    }
}
