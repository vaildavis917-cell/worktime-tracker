using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkTimeTracker.Agent.Services;
using WorkTimeTracker.Shared.Dtos;
using WorkTimeTracker.Shared.Models;

namespace WorkTimeTracker.Agent;

public class AgentWorker : BackgroundService
{
    private readonly ILogger<AgentWorker> _logger;
    private readonly AgentOptions _options;
    private readonly AgentRuntime _runtime;
    private readonly IRdpSessionMonitor _rdpMonitor;
    private readonly IScreenshotService _screenshots;
    private readonly IProcessMonitor _processMonitor;
    private readonly IEventUploader _uploader;
    private readonly IEventQueue _events;
    private readonly HashSet<string> _screenshotTriggerNames;
    private DateTime _lastTriggerScreenshotAt = DateTime.MinValue;

    public AgentWorker(
        ILogger<AgentWorker> logger,
        IOptions<AgentOptions> options,
        AgentRuntime runtime,
        IRdpSessionMonitor rdpMonitor,
        IScreenshotService screenshots,
        IProcessMonitor processMonitor,
        IEventUploader uploader,
        IEventQueue events)
    {
        _logger = logger;
        _options = options.Value;
        _runtime = runtime;
        _rdpMonitor = rdpMonitor;
        _screenshots = screenshots;
        _processMonitor = processMonitor;
        _uploader = uploader;
        _events = events;
        _screenshotTriggerNames = _options.ScreenshotTriggerProcesses
            .Select(NormaliseProcessName)
            .Where(p => p.Length > 0)
            .ToHashSet();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Agent starting. Host={Host}, Server={Server}, ScreenshotInterval={Interval}, IdleThreshold={Idle}",
            Environment.MachineName, _options.ServerUrl,
            _runtime.ScreenshotInterval, _options.IdleThreshold);

        if (Process.GetCurrentProcess().SessionId == 0)
        {
            _logger.LogWarning(
                "Agent is running in Session 0 (Windows Service). Screenshots will be black. " +
                "Deploy via Task Scheduler at user logon for Win10 — see README.");
        }

        _rdpMonitor.SessionEvent += OnRdpSessionEvent;
        _processMonitor.ProcessEvent += OnProcessEvent;
        _processMonitor.ForegroundChanged += OnForegroundChanged;

        _rdpMonitor.Start();
        _processMonitor.Start();

        EnqueueAgentEvent(ActivityEventType.AgentStarted);

        var heartbeat = RunHeartbeatLoop(stoppingToken);
        var screenshots = RunPeriodicScreenshotLoop(stoppingToken);
        var flush = RunEventFlushLoop(stoppingToken);

        await Task.WhenAll(heartbeat, screenshots, flush);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        EnqueueAgentEvent(ActivityEventType.AgentStopped);

        try
        {
            await FlushEventsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Final flush on stop failed");
        }

        _rdpMonitor.SessionEvent -= OnRdpSessionEvent;
        _processMonitor.ProcessEvent -= OnProcessEvent;
        _processMonitor.ForegroundChanged -= OnForegroundChanged;

        _rdpMonitor.Stop();
        _processMonitor.Stop();
        await base.StopAsync(cancellationToken);
    }

    private void OnRdpSessionEvent(object? sender, RdpSessionEvent evt)
    {
        _events.Enqueue(new ActivityEventDto(
            ClientEventId: Guid.NewGuid(),
            WtsSessionId: evt.WtsSessionId,
            SamAccountName: evt.SamAccountName,
            Type: evt.Type,
            TimestampUtc: evt.TimestampUtc,
            ProcessName: null,
            WindowTitle: null,
            Url: null,
            PayloadJson: evt.ClientName is null ? null : $"{{\"client\":\"{evt.ClientName}\"}}"));
    }

    private void OnProcessEvent(object? sender, ProcessEvent evt)
    {
        _events.Enqueue(new ActivityEventDto(
            ClientEventId: Guid.NewGuid(),
            WtsSessionId: evt.WtsSessionId,
            SamAccountName: Environment.UserName,
            Type: evt.IsStart ? ActivityEventType.ProcessStart : ActivityEventType.ProcessExit,
            TimestampUtc: evt.TimestampUtc,
            ProcessName: evt.ProcessName,
            WindowTitle: null,
            Url: null,
            PayloadJson: $"{{\"pid\":{evt.ProcessId}}}"));

        if (evt.IsStart && IsTriggerProcess(evt.ProcessName))
        {
            // Screenshot triggers fire at most once every 5s to avoid storms when
            // a parent process spawns many children with the same name.
            var now = DateTime.UtcNow;
            if ((now - _lastTriggerScreenshotAt).TotalSeconds < 5) return;
            _lastTriggerScreenshotAt = now;

            _ = Task.Run(() => CaptureTriggeredAsync(evt.ProcessName, ScreenshotTrigger.ProcessLaunched));
        }
    }

    private void OnForegroundChanged(object? sender, ForegroundChangedEvent evt)
    {
        _events.Enqueue(new ActivityEventDto(
            ClientEventId: Guid.NewGuid(),
            WtsSessionId: evt.WtsSessionId,
            SamAccountName: Environment.UserName,
            Type: ActivityEventType.ForegroundWindowChanged,
            TimestampUtc: evt.TimestampUtc,
            ProcessName: evt.ProcessName,
            WindowTitle: evt.WindowTitle,
            Url: null,
            PayloadJson: null));
    }

    private bool IsTriggerProcess(string processName) =>
        _screenshotTriggerNames.Contains(NormaliseProcessName(processName));

    private static string NormaliseProcessName(string raw)
    {
        var clean = raw.Trim();
        if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean[..^4];
        }
        return clean.ToLowerInvariant();
    }

    private async Task CaptureTriggeredAsync(string triggerProcess, ScreenshotTrigger trigger)
    {
        try
        {
            var sessionId = Process.GetCurrentProcess().SessionId;
            var capture = await _screenshots.CaptureSessionAsync(
                wtsSessionId: sessionId,
                trigger: trigger,
                triggerProcess: triggerProcess,
                CancellationToken.None);

            if (capture is null) return;

            var metadata = new ScreenshotMetadataDto(
                WtsSessionId: sessionId,
                SamAccountName: Environment.UserName,
                CapturedAtUtc: capture.CapturedAtUtc,
                Trigger: capture.Trigger,
                TriggerProcess: capture.TriggerProcess,
                Width: capture.Width,
                Height: capture.Height);

            await _uploader.UploadScreenshotAsync(metadata, capture.PngBytes, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Triggered screenshot for {Process} failed", triggerProcess);
        }
    }

    private void EnqueueAgentEvent(ActivityEventType type)
    {
        _events.Enqueue(new ActivityEventDto(
            ClientEventId: Guid.NewGuid(),
            WtsSessionId: Process.GetCurrentProcess().SessionId,
            SamAccountName: Environment.UserName,
            Type: type,
            TimestampUtc: DateTime.UtcNow,
            ProcessName: null,
            WindowTitle: null,
            Url: null,
            PayloadJson: null));
    }

    private async Task RunHeartbeatLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _uploader.SendHeartbeatAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat failed");
            }

            try { await Task.Delay(_options.HeartbeatInterval, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task RunPeriodicScreenshotLoop(CancellationToken ct)
    {
        var sessionId = Process.GetCurrentProcess().SessionId;
        var sam = Environment.UserName;

        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(_runtime.ScreenshotInterval, ct); }
            catch (OperationCanceledException) { return; }

            try
            {
                var capture = await _screenshots.CaptureSessionAsync(
                    wtsSessionId: sessionId,
                    trigger: ScreenshotTrigger.Periodic,
                    triggerProcess: null,
                    ct);

                if (capture is null)
                {
                    continue;
                }

                var metadata = new ScreenshotMetadataDto(
                    WtsSessionId: sessionId,
                    SamAccountName: sam,
                    CapturedAtUtc: capture.CapturedAtUtc,
                    Trigger: capture.Trigger,
                    TriggerProcess: capture.TriggerProcess,
                    Width: capture.Width,
                    Height: capture.Height);

                await _uploader.UploadScreenshotAsync(metadata, capture.PngBytes, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Periodic screenshot upload failed");
            }
        }
    }

    private async Task RunEventFlushLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(_options.EventBatchInterval, ct); }
            catch (OperationCanceledException) { return; }

            try
            {
                await FlushEventsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Event flush failed");
            }
        }
    }

    private async Task FlushEventsAsync(CancellationToken ct)
    {
        var batch = _events.DrainAll();
        if (batch.Count == 0)
        {
            return;
        }

        var dto = new ActivityEventBatchDto(
            Hostname: Environment.MachineName,
            Events: batch);

        await _uploader.SendEventBatchAsync(dto, ct);
    }
}
