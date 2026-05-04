using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
using WorkTimeTracker.Shared.Models;

namespace WorkTimeTracker.Agent.Services;

// Multi-monitor screenshot capture.
//
// Deployment note: when the agent runs as a Windows Service in Session 0,
// CopyFromScreen returns black pixels because the GDI primary desktop in
// session 0 has no user UI. Two production options:
//   (a) deploy via Task Scheduler "At log on of any user" with "Run with
//       highest privileges" — process lives in the user's session, captures
//       the real desktop directly. Recommended for Win10 single-session
//       boxes (this is what we document in README).
//   (b) deploy as a service that spawns a per-session helper via
//       WTSQueryUserToken + CreateProcessAsUser. More complex; future work.
//
// This implementation captures whichever desktop the current process is
// attached to. SystemInformation.VirtualScreen covers the union of every
// monitor — the produced PNG includes them all in their relative positions.

[SupportedOSPlatform("windows")]
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
        if (!OperatingSystem.IsWindows())
        {
            _logger.LogWarning("Screenshot capture skipped: not running on Windows");
            return Task.FromResult<ScreenshotCapture?>(null);
        }

        try
        {
            var capture = CaptureVirtualScreen(trigger, triggerProcess);
            return Task.FromResult<ScreenshotCapture?>(capture);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Screenshot capture failed for session {SessionId}", wtsSessionId);
            return Task.FromResult<ScreenshotCapture?>(null);
        }
    }

    private ScreenshotCapture CaptureVirtualScreen(ScreenshotTrigger trigger, string? triggerProcess)
    {
        var bounds = SystemInformation.VirtualScreen;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new InvalidOperationException(
                $"Virtual screen has non-positive size {bounds.Width}x{bounds.Height} " +
                "— likely running in Session 0 with no interactive desktop.");
        }

        var sw = Stopwatch.StartNew();

        using var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        }

        using var ms = new MemoryStream(capacity: 256 * 1024);
        bmp.Save(ms, ImageFormat.Png);

        sw.Stop();

        _logger.LogInformation(
            "Captured {W}x{H} screenshot ({Bytes} bytes, {MonitorCount} monitor(s), trigger={Trigger}) in {Ms}ms",
            bounds.Width, bounds.Height, ms.Length, Screen.AllScreens.Length, trigger, sw.ElapsedMilliseconds);

        return new ScreenshotCapture(
            PngBytes: ms.ToArray(),
            Width: bounds.Width,
            Height: bounds.Height,
            CapturedAtUtc: DateTime.UtcNow,
            Trigger: trigger,
            TriggerProcess: triggerProcess);
    }
}
