using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using WorkTimeTracker.Shared.Dtos;

namespace WorkTimeTracker.Agent.Services;

public class EventUploader : IEventUploader
{
    private readonly HttpClient _http;
    private readonly ILogger<EventUploader> _logger;
    private readonly AgentRuntime _runtime;

    public EventUploader(HttpClient http, ILogger<EventUploader> logger, AgentRuntime runtime)
    {
        _http = http;
        _logger = logger;
        _runtime = runtime;
    }

    public async Task SendHeartbeatAsync(CancellationToken ct)
    {
        var dto = new AgentHeartbeatDto(
            Hostname: Environment.MachineName,
            AgentVersion: typeof(EventUploader).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            SentAtUtc: DateTime.UtcNow,
            ActiveSessions: 1,
            CurrentSessionId: Process.GetCurrentProcess().SessionId,
            CurrentSamAccountName: Environment.UserName,
            OsVersion: Environment.OSVersion.VersionString);

        var resp = await _http.PostAsJsonAsync("api/agents/heartbeat", dto, ct);
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<AgentHeartbeatResponse>(cancellationToken: ct);
        if (body is not null && body.ScreenshotIntervalSeconds > 0)
        {
            var newInterval = TimeSpan.FromSeconds(body.ScreenshotIntervalSeconds);
            if (newInterval != _runtime.ScreenshotInterval)
            {
                _logger.LogInformation(
                    "Server requested screenshot interval change: {Old}s -> {New}s",
                    (int)_runtime.ScreenshotInterval.TotalSeconds,
                    (int)newInterval.TotalSeconds);
                _runtime.SetScreenshotInterval(newInterval);
            }
        }
    }

    public async Task SendEventBatchAsync(ActivityEventBatchDto batch, CancellationToken ct)
    {
        var resp = await _http.PostAsJsonAsync("api/agents/events", batch, ct);
        resp.EnsureSuccessStatusCode();
        _logger.LogDebug("Sent batch with {Count} events", batch.Events.Count);
    }

    public async Task UploadScreenshotAsync(ScreenshotMetadataDto metadata, byte[] pngBytes, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        form.Add(JsonContent.Create(metadata), "metadata");
        var imageContent = new ByteArrayContent(pngBytes);
        imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(imageContent, "image", $"{Guid.NewGuid():N}.png");

        var resp = await _http.PostAsync("api/agents/screenshots", form, ct);
        resp.EnsureSuccessStatusCode();
    }
}
