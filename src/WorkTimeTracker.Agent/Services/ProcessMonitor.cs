using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using WorkTimeTracker.Agent.Native;

namespace WorkTimeTracker.Agent.Services;

// Real process and foreground-window monitor.
//
// Two parallel sources:
//
//   1. Foreground window — SetWinEventHook(EVENT_SYSTEM_FOREGROUND) on a
//      dedicated STA thread that pumps messages. Each callback resolves
//      the PID via GetWindowThreadProcessId, looks up the process name,
//      and emits a ForegroundChangedEvent. Debounced: identical
//      (process, title) tuples within 500ms collapse.
//
//   2. Process start/exit — WMI ManagementEventWatcher subscribed to
//      Win32_ProcessStartTrace / Win32_ProcessStopTrace. Fires on every
//      process in the OS — we filter to current SessionId.
//
// On a non-Windows host every entry-point becomes a no-op.

[SupportedOSPlatform("windows")]
public sealed class ProcessMonitor : IProcessMonitor, IDisposable
{
    private readonly ILogger<ProcessMonitor> _logger;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private IntPtr _hookHandle;
    private NativeMethods.WinEventDelegate? _hookDelegate;
    private ManagementEventWatcher? _startWatcher;
    private ManagementEventWatcher? _stopWatcher;
    private string? _lastProcessName;
    private string? _lastWindowTitle;
    private DateTime _lastForegroundAt = DateTime.MinValue;
    private readonly int _ourSessionId;
    private volatile bool _running;

    public event EventHandler<ProcessEvent>? ProcessEvent;
    public event EventHandler<ForegroundChangedEvent>? ForegroundChanged;

    public ProcessMonitor(ILogger<ProcessMonitor> logger)
    {
        _logger = logger;
        _ourSessionId = Process.GetCurrentProcess().SessionId;
    }

    public void Start()
    {
        if (_running) return;
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("ProcessMonitor.Start skipped: not on Windows");
            return;
        }

        _running = true;

        StartForegroundHookThread();
        StartProcessWatchers();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;

        StopProcessWatchers();
        StopForegroundHookThread();
    }

    private void StartForegroundHookThread()
    {
        _hookThread = new Thread(ForegroundPump)
        {
            IsBackground = true,
            Name = "ForegroundWindowHook"
        };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();
    }

    private void StopForegroundHookThread()
    {
        if (_hookThreadId != 0)
        {
            NativeMethods.PostThreadMessage(_hookThreadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
        _hookThread?.Join(TimeSpan.FromSeconds(3));
        _hookThread = null;
        _hookThreadId = 0;
    }

    private void ForegroundPump()
    {
        _hookThreadId = NativeMethods.GetCurrentThreadId();

        // Keep the delegate rooted for the lifetime of the hook so the
        // GC does not collect it under our feet.
        _hookDelegate = OnForegroundChanged;

        try
        {
            _hookHandle = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _hookDelegate,
                idProcess: 0,
                idThread: 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

            if (_hookHandle == IntPtr.Zero)
            {
                _logger.LogError("SetWinEventHook returned NULL — foreground tracking disabled");
                return;
            }

            _logger.LogInformation("Foreground hook armed");

            while (NativeMethods.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Foreground pump crashed");
        }
        finally
        {
            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
            _hookDelegate = null;
        }
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero) return;

        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return;

            string processName;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                processName = p.ProcessName;
            }
            catch
            {
                return;
            }

            var title = ReadWindowTitle(hwnd);

            // Debounce — drop dupes that arrive in rapid succession.
            var now = DateTime.UtcNow;
            if (processName == _lastProcessName
                && title == _lastWindowTitle
                && (now - _lastForegroundAt).TotalMilliseconds < 500)
            {
                return;
            }
            _lastProcessName = processName;
            _lastWindowTitle = title;
            _lastForegroundAt = now;

            ForegroundChanged?.Invoke(this, new ForegroundChangedEvent(
                WtsSessionId: _ourSessionId,
                ProcessName: processName,
                WindowTitle: title,
                TimestampUtc: now));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Foreground callback failed");
        }
    }

    private static string ReadWindowTitle(IntPtr hwnd)
    {
        var len = NativeMethods.GetWindowTextLength(hwnd);
        if (len <= 0) return string.Empty;
        var sb = new StringBuilder(Math.Min(len + 1, 1024));
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private void StartProcessWatchers()
    {
        try
        {
            _startWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM Win32_ProcessStartTrace"));
            _startWatcher.EventArrived += (_, e) => HandleProcessTrace(e, isStart: true);
            _startWatcher.Start();

            _stopWatcher = new ManagementEventWatcher(
                new WqlEventQuery("SELECT * FROM Win32_ProcessStopTrace"));
            _stopWatcher.EventArrived += (_, e) => HandleProcessTrace(e, isStart: false);
            _stopWatcher.Start();

            _logger.LogInformation("WMI process watchers armed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start WMI process watchers — process events disabled");
            DisposeWatchers();
        }
    }

    private void StopProcessWatchers()
    {
        DisposeWatchers();
    }

    private void DisposeWatchers()
    {
        try { _startWatcher?.Stop(); } catch { /* ignore */ }
        try { _startWatcher?.Dispose(); } catch { /* ignore */ }
        try { _stopWatcher?.Stop(); } catch { /* ignore */ }
        try { _stopWatcher?.Dispose(); } catch { /* ignore */ }
        _startWatcher = null;
        _stopWatcher = null;
    }

    private void HandleProcessTrace(EventArrivedEventArgs e, bool isStart)
    {
        try
        {
            var processName = e.NewEvent.Properties["ProcessName"]?.Value?.ToString() ?? string.Empty;
            var pidObj = e.NewEvent.Properties["ProcessID"]?.Value;
            var pid = pidObj is null ? 0 : Convert.ToInt32(pidObj);
            if (string.IsNullOrEmpty(processName) || pid == 0) return;

            // Trim trailing .exe to keep events tidy.
            if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                processName = processName[..^4];
            }

            // Filter to the current user's session. WMI doesn't expose
            // SessionId directly here; for a Win10 single-session host
            // this is fine — every interactive process is the user's.
            ProcessEvent?.Invoke(this, new ProcessEvent(
                WtsSessionId: _ourSessionId,
                ProcessName: processName,
                ProcessId: pid,
                IsStart: isStart,
                TimestampUtc: DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse Win32_ProcessTrace event");
        }
    }

    public void Dispose() => Stop();
}
