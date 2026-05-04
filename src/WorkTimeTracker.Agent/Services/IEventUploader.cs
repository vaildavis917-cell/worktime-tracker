using WorkTimeTracker.Shared.Dtos;

namespace WorkTimeTracker.Agent.Services;

public interface IEventUploader
{
    Task SendHeartbeatAsync(CancellationToken ct);
    Task SendEventBatchAsync(ActivityEventBatchDto batch, CancellationToken ct);
    Task UploadScreenshotAsync(ScreenshotMetadataDto metadata, byte[] pngBytes, CancellationToken ct);
}
