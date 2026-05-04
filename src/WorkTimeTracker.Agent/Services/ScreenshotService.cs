using Microsoft.Extensions.Logging;
using WorkTimeTracker.Shared.Models;

namespace WorkTimeTracker.Agent.Services;

// Stub for the screenshot service.
//
// Full implementation needs to:
//   1. Run inside the user's session (Service-to-Session-0 isolation prevents
//      a SYSTEM service from grabbing pixels of an RDP user). Options:
//        a. Have the agent service spawn a per-session helper exe via
//           CreateProcessAsUser into the target WTS session, OR
//        b. Use WTSQueryUserToken + DuplicateTokenEx + ImpersonateLoggedOnUser
//           and call BitBlt/PrintWindow.
//   2. Capture the desktop with Graphics.CopyFromScreen or PrintWindow.
//   3. Encode PNG with low quality / scale to reduce storage.
//   4. Return ScreenshotCapture for the uploader.

public class ScreenshotService : IScreenshotService
{
    private readonly ILogger<ScreenshotService> _logger;

    public ScreenshotService(ILogger<ScreenshotService> logger)
    {
        _logger = logger;
    }

    public Task<ScreenshotCapture?> CaptureSessionAsync(
        int wtsSessionId,
        ScreenshotTrigger trigger,
        string? triggerProcess,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Capture requested for session {SessionId}, trigger={Trigger}, process={Process} — TODO",
            wtsSessionId, trigger, triggerProcess);

        return Task.FromResult<ScreenshotCapture?>(null);
    }
}
