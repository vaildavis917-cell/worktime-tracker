using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkTimeTracker.Agent.Services;

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
        _logger.LogInformation("Agent starting. Server={Server}", _options.ServerUrl);

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

            await Task.Delay(_options.HeartbeatInterval, ct);
        }
    }

    private async Task RunPeriodicScreenshotLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(_options.PeriodicScreenshotInterval, ct);
            // TODO: capture screenshot for each active session and queue it for upload.
        }
    }

    private async Task RunEventFlushLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(_options.EventBatchInterval, ct);
            // TODO: drain queued events and POST batch to /api/agents/events.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _rdpMonitor.Stop();
        _processMonitor.Stop();
        await base.StopAsync(cancellationToken);
    }
}
