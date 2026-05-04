using WorkTimeTracker.Shared.Models;

namespace WorkTimeTracker.Shared.Dtos;

public record ScreenshotMetadataDto(
    int WtsSessionId,
    string SamAccountName,
    DateTime CapturedAtUtc,
    ScreenshotTrigger Trigger,
    string? TriggerProcess,
    int Width,
    int Height);
