using WorkTimeTracker.Shared.Models;

namespace WorkTimeTracker.Agent.Services;

public interface IScreenshotService
{
    Task<ScreenshotCapture?> CaptureSessionAsync(int wtsSessionId, ScreenshotTrigger trigger, string? triggerProcess, CancellationToken ct);
}

public record ScreenshotCapture(
    byte[] PngBytes,
    int Width,
    int Height,
    DateTime CapturedAtUtc,
    ScreenshotTrigger Trigger,
    string? TriggerProcess);
