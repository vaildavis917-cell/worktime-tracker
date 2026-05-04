using WorkTimeTracker.Shared.Models;

namespace WorkTimeTracker.Shared.Dtos;

public record ActivityEventDto(
    Guid? ClientEventId,
    int WtsSessionId,
    string SamAccountName,
    ActivityEventType Type,
    DateTime TimestampUtc,
    string? ProcessName,
    string? WindowTitle,
    string? Url,
    string? PayloadJson);

public record ActivityEventBatchDto(
    string Hostname,
    IReadOnlyList<ActivityEventDto> Events);
