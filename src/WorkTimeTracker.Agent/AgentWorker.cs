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
    private readonly IRdpSessionMonitor _rdpMonitor;
    private readonly IScreenshotService _screenshots;
    private readonly IProcessMonitor _processMonitor;
    private readonly IEventUploader _uploader;

    public AgentWorker(
        ILogger<AgentWorker> logger,
        IOptions<AgentOptions> options,
        IRdpSessionMonitor rdpMonitor,
        IScreenshotService screenshots,
        IProcessMonitor processMonitor,
        IEventUploader uploader)
    {
        _logger = logger;
        _options = options.Value;
        _rdpMonitor = rdpMonitor;
        _screenshots = screenshots;
        _processMonitor = processMonitor;
        _uploader = uploader;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Agent starting. Host={Host}, Server={Server}, ScreenshotInterval={Interval}",
            Environment.MachineName, _options.ServerUrl, _options.PeriodicScreenshotInterval);

        if (Process.GetCurrentProcess().SessionId == 0)
        {
            _logger.LogWarning(
                "Agent is running in Session 0 (Windows Service). Screenshots will be black. " +
                "Deploy via Task Scheduler at user logon for Win10 — see README.");
        }

        _rdpMonitor.Start();
        _processMonitor.Start();

        var heartbeat = RunHeartbeatLoop(stoppingToken);
        var screenshots = RunPeriodicScreenshotLoop(stoppingToken);
        var flush = RunEventFlushLoop(stoppingToken);

        await Task.WhenAll(heartbeat, screenshots, flush);
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
            try { await Task.Delay(_options.PeriodicScreenshotInterval, ct); }
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

            // TODO (stage 3+): drain queued events from RdpMonitor / ProcessMonitor / Keylogger
            // and POST batch to /api/agents/events.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _rdpMonitor.Stop();
        _processMonitor.Stop();
        await base.StopAsync(cancellationToken);
    }
}
