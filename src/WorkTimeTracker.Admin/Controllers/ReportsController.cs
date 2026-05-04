using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;

namespace WorkTimeTracker.Admin.Controllers;

[ApiController]
[Authorize]
[Route("admin-api/reports")]
public class ReportsController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(IDbContextFactory<AppDbContext> dbFactory, ILogger<ReportsController> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Hours-by-day report. Each row: SamAccountName, DisplayName, Date, Hours.
    /// Hours = sum of all session durations whose interval intersects the
    /// day, clipped to that day. Active sessions are clipped to "now".
    /// Idle gaps are not subtracted at the moment — TODO when IdleStart /
    /// IdleEnd events have been collected long enough to be meaningful.
    /// </summary>
    [HttpGet("hours.csv")]
    public async Task<IActionResult> HoursCsv(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? employee,
        CancellationToken ct)
    {
        var fromUtc = (from ?? DateTime.UtcNow.Date.AddDays(-7)).Date;
        var toUtc = (to ?? DateTime.UtcNow.Date.AddDays(1)).Date;
        if (toUtc <= fromUtc)
        {
            return BadRequest("'to' must be after 'from'");
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var sessionsQuery = db.RdpSessions
            .AsNoTracking()
            .Where(s => s.StartedAt < toUtc && (s.EndedAt == null || s.EndedAt >= fromUtc));

        if (!string.IsNullOrWhiteSpace(employee))
        {
            sessionsQuery = sessionsQuery.Where(s => s.Employee!.SamAccountName == employee);
        }

        var sessions = await sessionsQuery
            .Select(s => new
            {
                s.StartedAt,
                s.EndedAt,
                Sam = s.Employee!.SamAccountName,
                Name = s.Employee!.DisplayName
            })
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var rows = new Dictionary<(string Sam, string Name, DateTime Day), TimeSpan>();

        foreach (var s in sessions)
        {
            var start = s.StartedAt > fromUtc ? s.StartedAt : fromUtc;
            var end = (s.EndedAt ?? now) < toUtc ? (s.EndedAt ?? now) : toUtc;

            for (var day = start.Date; day < end; day = day.AddDays(1))
            {
                var dayStart = day;
                var dayEnd = day.AddDays(1);
                var sliceStart = start > dayStart ? start : dayStart;
                var sliceEnd = end < dayEnd ? end : dayEnd;
                var slice = sliceEnd - sliceStart;
                if (slice <= TimeSpan.Zero) continue;

                var key = (s.Sam, s.Name, day);
                rows[key] = rows.TryGetValue(key, out var current) ? current + slice : slice;
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("SamAccountName,DisplayName,Date,Hours");
        foreach (var ((sam, name, day), duration) in rows.OrderBy(r => r.Key.Sam).ThenBy(r => r.Key.Day))
        {
            sb.Append(EscapeCsv(sam)).Append(',');
            sb.Append(EscapeCsv(name)).Append(',');
            sb.Append(day.ToString("yyyy-MM-dd")).Append(',');
            sb.AppendLine(duration.TotalHours.ToString("0.00", CultureInfo.InvariantCulture));
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();

        _logger.LogInformation(
            "Generated hours.csv: from={From} to={To} employee={Emp} rows={Rows}",
            fromUtc, toUtc, employee ?? "*", rows.Count);

        return File(bytes, "text/csv", $"hours-{fromUtc:yyyyMMdd}-{toUtc:yyyyMMdd}.csv");
    }

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
        {
            return value;
        }
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
