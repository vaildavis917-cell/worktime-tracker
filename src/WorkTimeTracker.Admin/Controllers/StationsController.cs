using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;

namespace WorkTimeTracker.Admin.Controllers;

[ApiController]
[Authorize]
[Route("admin-api/stations")]
public class StationsController : ControllerBase
{
    private static readonly TimeSpan LiveViewWindow = TimeSpan.FromMinutes(10);

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<StationsController> _logger;

    public StationsController(
        IDbContextFactory<AppDbContext> dbFactory,
        ILogger<StationsController> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    [HttpPost("{hostname}/live-view")]
    public async Task<IActionResult> EnableLiveView(string hostname, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var host = await db.ServerHosts.FirstOrDefaultAsync(h => h.Hostname == hostname, ct);
        if (host is null)
        {
            return NotFound(new { error = "host not found" });
        }

        host.LiveViewUntil = DateTime.UtcNow.Add(LiveViewWindow);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Live-view enabled for {Host} until {Until} by {User}",
            hostname, host.LiveViewUntil, User.Identity?.Name);

        return Ok(new { until = host.LiveViewUntil });
    }

    [HttpDelete("{hostname}/live-view")]
    public async Task<IActionResult> DisableLiveView(string hostname, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var host = await db.ServerHosts.FirstOrDefaultAsync(h => h.Hostname == hostname, ct);
        if (host is null)
        {
            return NotFound();
        }

        host.LiveViewUntil = null;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{hostname}/latest-screenshot")]
    public async Task<IActionResult> LatestScreenshot(string hostname, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var host = await db.ServerHosts.FirstOrDefaultAsync(h => h.Hostname == hostname, ct);
        if (host is null)
        {
            return NotFound();
        }

        var shot = await db.Screenshots
            .AsNoTracking()
            .Where(s => s.RdpSession!.ServerHostId == host.Id)
            .OrderByDescending(s => s.CapturedAt)
            .Select(s => new { s.Id, s.CapturedAt, s.Width, s.Height })
            .FirstOrDefaultAsync(ct);

        if (shot is null)
        {
            return NotFound(new { error = "no screenshots yet" });
        }

        return Ok(shot);
    }

    [HttpGet("{hostname}/shadow-command")]
    public async Task<ActionResult<object>> ShadowCommand(string hostname, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var host = await db.ServerHosts
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Hostname == hostname, ct);
        if (host is null)
        {
            return NotFound();
        }

        var sessionId = host.CurrentSessionId ?? 1;
        var command = $"mstsc /shadow:{sessionId} /v:{host.Hostname} /control /noconsentprompt";
        return Ok(new { command, sessionId, currentUser = host.CurrentSamAccountName });
    }
}
