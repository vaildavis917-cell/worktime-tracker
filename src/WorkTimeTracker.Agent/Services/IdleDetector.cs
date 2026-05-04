using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkTimeTracker.Agent.Native;
using WorkTimeTracker.Shared.Dtos;
using WorkTimeTracker.Shared.Models;

namespace WorkTimeTracker.Agent.Services;

// Idle detector backed by GetLastInputInfo.
//
// GetLastInputInfo returns the system tick count at the time of the last
// input (mouse, keyboard, pen). Comparing with GetTickCount() gives the
// "no input for N ms" duration. This is global to the input desktop, so
// it correctly stays at the same value while the user is locked or away.
//
// We poll every 5 seconds and emit IdleStart when the gap exceeds
// AgentOptions.IdleThreshold, IdleEnd when input returns. State is
// debounced so a hiccup at the threshold boundary doesn't cause flapping.

[SupportedOSPlatform("windows")]
public sealed class IdleDetector : BackgroundService
{
    private readonly ILogger<IdleDetector> _logger;
    private readonly AgentOptions _options;
    private readonly IEventQueue _events;
    private readonly int _ourSessionId;
    private readonly string _sam;
    private bool _isIdle;

    public IdleDetector(
        ILogger<IdleDetector> logger,
        IOptions<AgentOptions> options,
        IEventQueue events)
    {
        _logger = logger;
        _options = options.Value;
        _events = events;
        _ourSessionId = Process.GetCurrentProcess().SessionId;
        _sam = Environment.UserName;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(5);
        _logger.LogInformation(
            "IdleDetector running. threshold={Threshold}, poll={Poll}",
            _options.IdleThreshold, pollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Idle tick failed");
            }

            try { await Task.Delay(pollInterval, stoppingToken); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void Tick()
    {
        var idleFor = GetIdleDuration();
        var threshold = _options.IdleThreshold;

        if (!_isIdle && idleFor >= threshold)
        {
            _isIdle = true;
            _logger.LogInformation("User idle for {Idle} (threshold {Threshold}) — pausing timer", idleFor, threshold);
            EnqueueIdleEvent(ActivityEventType.IdleStart, idleFor);
        }
        else if (_isIdle && idleFor < TimeSpan.FromSeconds(2))
        {
            _isIdle = false;
            _logger.LogInformation("User active again — resuming timer");
            EnqueueIdleEvent(ActivityEventType.IdleEnd, idleFor);
        }
    }

    private static TimeSpan GetIdleDuration()
    {
        var info = new NativeMethods.LASTINPUTINFO
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.LASTINPUTINFO>()
        };
        if (!NativeMethods.GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }
        var ticks = NativeMethods.GetTickCount();
        var diff = unchecked(ticks - info.dwTime);
        return TimeSpan.FromMilliseconds(diff);
    }

    private void EnqueueIdleEvent(ActivityEventType type, TimeSpan duration)
    {
        _events.Enqueue(new ActivityEventDto(
            ClientEventId: Guid.NewGuid(),
            WtsSessionId: _ourSessionId,
            SamAccountName: _sam,
            Type: type,
            TimestampUtc: DateTime.UtcNow,
            ProcessName: null,
            WindowTitle: null,
            Url: null,
            PayloadJson: $"{{\"idleSeconds\":{(int)duration.TotalSeconds}}}"));
    }
}
