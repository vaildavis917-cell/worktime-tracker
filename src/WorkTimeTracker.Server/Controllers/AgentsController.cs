using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WorkTimeTracker.Data;
using WorkTimeTracker.Shared.Dtos;
using WorkTimeTracker.Shared.Models;

namespace WorkTimeTracker.Server.Controllers;

[ApiController]
[Route("api/agents")]
[ServiceFilter(typeof(AgentTokenAuthFilter))]
public class AgentsController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<ActivityEventType> SessionOpenEvents = new()
    {
        ActivityEventType.AgentStarted,
        ActivityEventType.SessionLogon,
        ActivityEventType.RemoteConnect,
        ActivityEventType.ConsoleConnect,
        ActivityEventType.SessionUnlock
    };

    private static readonly HashSet<ActivityEventType> SessionCloseEvents = new()
    {
        ActivityEventType.AgentStopped,
        ActivityEventType.SessionLogoff
    };

    private readonly AppDbContext _db;
    private readonly StorageOptions _storage;
    private readonly ILogger<AgentsController> _logger;

    public AgentsController(
        AppDbContext db,
        IOptions<StorageOptions> storage,
        ILogger<AgentsController> logger)
    {
        _db = db;
        _storage = storage.Value;
        _logger = logger;
    }

    [HttpPost("heartbeat")]
    public async Task<ActionResult<AgentHeartbeatResponse>> Heartbeat(
        [FromBody] AgentHeartbeatDto dto,
        CancellationToken ct)
    {
        // TODO (stage 4): validate X-Agent-Token against ServerHosts.AgentToken
        var host = await UpsertHost(dto.Hostname, dto.AgentVersion, ct);
        host.CurrentSessionId = dto.CurrentSessionId;
        host.CurrentSamAccountName = dto.CurrentSamAccountName;
        host.OsVersion = dto.OsVersion;
        await _db.SaveChangesAsync(ct);

        // Live-view: when an admin clicked "Watch" on the station within the
        // last LiveViewUntil window, ask the agent to ship screenshots more
        // often. The agent clamps to [1s, 10min].
        var liveActive = host.LiveViewUntil is { } expiry && expiry > DateTime.UtcNow;
        var interval = liveActive ? 2 : 30;

        _logger.LogDebug(
            "Heartbeat from {Host} v{Ver} session={Sid} sam={Sam} live={Live} interval={Interval}s",
            dto.Hostname, dto.AgentVersion, dto.CurrentSessionId, dto.CurrentSamAccountName,
            liveActive, interval);

        return Ok(new AgentHeartbeatResponse(host.Id, RequestFullSync: false, ScreenshotIntervalSeconds: interval));
    }

    [HttpPost("events")]
    public async Task<IActionResult> Events([FromBody] ActivityEventBatchDto batch, CancellationToken ct)
    {
        if (batch.Events is null || batch.Events.Count == 0)
        {
            return Accepted(new { saved = 0 });
        }

        var host = await UpsertHost(batch.Hostname, agentVersion: null, ct);
        await _db.SaveChangesAsync(ct);

        var employeeCache = new Dictionary<string, Employee>();
        var saved = 0;

        foreach (var dto in batch.Events.OrderBy(e => e.TimestampUtc))
        {
            if (!employeeCache.TryGetValue(dto.SamAccountName, out var emp))
            {
                emp = await UpsertEmployee(dto.SamAccountName, ct);
                await _db.SaveChangesAsync(ct);
                employeeCache[dto.SamAccountName] = emp;
            }

            var session = await GetOrOpenSession(emp.Id, host.Id, dto, ct);

            if (SessionCloseEvents.Contains(dto.Type))
            {
                session.EndedAt = dto.TimestampUtc;
                session.State = RdpSessionState.Ended;
            }

            _db.ActivityEvents.Add(new ActivityEvent
            {
                Id = Guid.NewGuid(),
                RdpSessionId = session.Id,
                Type = dto.Type,
                Timestamp = dto.TimestampUtc,
                ProcessName = dto.ProcessName,
                WindowTitle = dto.WindowTitle,
                Url = dto.Url,
                PayloadJson = dto.PayloadJson
            });
            saved++;
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Persisted {Count} events from {Host}", saved, batch.Hostname);

        return Accepted(new { saved });
    }

    [HttpPost("screenshots")]
    [RequestSizeLimit(50_000_000)]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Screenshots(CancellationToken ct)
    {
        var form = await Request.ReadFormAsync(ct);

        var metadataRaw = form["metadata"].ToString();
        if (string.IsNullOrWhiteSpace(metadataRaw))
        {
            return BadRequest("multipart field 'metadata' is missing");
        }

        ScreenshotMetadataDto? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<ScreenshotMetadataDto>(metadataRaw, JsonOpts);
        }
        catch (JsonException ex)
        {
            return BadRequest("metadata is not valid JSON: " + ex.Message);
        }
        if (metadata is null)
        {
            return BadRequest("metadata deserialized to null");
        }

        var file = form.Files.GetFile("image");
        if (file is null || file.Length == 0)
        {
            return BadRequest("multipart field 'image' is missing or empty");
        }

        var hostname = Request.Headers["X-Agent-Hostname"].ToString();
        if (string.IsNullOrWhiteSpace(hostname))
        {
            return BadRequest("X-Agent-Hostname header is required");
        }

        var host = await UpsertHost(hostname, agentVersion: null, ct);
        var employee = await UpsertEmployee(metadata.SamAccountName, ct);
        await _db.SaveChangesAsync(ct);

        var session = await ResolveOpenSession(employee.Id, host.Id, metadata.WtsSessionId, ct);

        var (storageKey, fullPath) = BuildStoragePath(metadata.CapturedAtUtc, hostname);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var fs = System.IO.File.Create(fullPath))
        {
            await file.CopyToAsync(fs, ct);
        }

        var shot = new Screenshot
        {
            Id = Guid.NewGuid(),
            RdpSessionId = session.Id,
            CapturedAt = metadata.CapturedAtUtc,
            Trigger = metadata.Trigger,
            TriggerProcess = metadata.TriggerProcess,
            StorageKey = storageKey,
            Width = metadata.Width,
            Height = metadata.Height,
            SizeBytes = file.Length
        };
        _db.Screenshots.Add(shot);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Saved screenshot {Id} ({W}x{H}, {Bytes} bytes) for {Sam}@{Host} -> {Key}",
            shot.Id, shot.Width, shot.Height, shot.SizeBytes,
            metadata.SamAccountName, hostname, storageKey);

        return Accepted(new { id = shot.Id, storageKey });
    }

    private async Task<ServerHost> UpsertHost(string hostname, string? agentVersion, CancellationToken ct)
    {
        var host = await _db.ServerHosts.FirstOrDefaultAsync(h => h.Hostname == hostname, ct);
        if (host is null)
        {
            // First contact — auth filter has already verified that
            // ServerOptions.AllowAutoRegister is true and the presented
            // token meets MinTokenLength. Persist the token so future
            // requests from the same hostname can be validated against it.
            var token = Request.Headers[AgentTokenAuthFilter.TokenHeader].ToString();
            host = new ServerHost
            {
                Id = Guid.NewGuid(),
                Hostname = hostname,
                AgentToken = token,
                CreatedAt = DateTime.UtcNow
            };
            _db.ServerHosts.Add(host);
            _logger.LogInformation("Auto-registered new host {Hostname}", hostname);
        }

        host.LastHeartbeatAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(agentVersion))
        {
            host.AgentVersion = agentVersion;
        }

        return host;
    }

    private async Task<Employee> UpsertEmployee(string sam, CancellationToken ct)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.SamAccountName == sam, ct);
        if (emp is null)
        {
            emp = new Employee
            {
                Id = Guid.NewGuid(),
                SamAccountName = sam,
                DisplayName = sam,
                CreatedAt = DateTime.UtcNow
            };
            _db.Employees.Add(emp);
        }
        return emp;
    }

    private async Task<RdpSession> GetOrOpenSession(Guid employeeId, Guid hostId, ActivityEventDto dto, CancellationToken ct)
    {
        var session = await FindOpenSession(employeeId, hostId, ct);

        var shouldOpenNew = session is null && SessionOpenEvents.Contains(dto.Type);

        if (session is null && !shouldOpenNew)
        {
            // Mid-stream event with no open session; open a synthetic one starting at this timestamp.
            shouldOpenNew = true;
        }

        if (shouldOpenNew)
        {
            session = new RdpSession
            {
                Id = Guid.NewGuid(),
                EmployeeId = employeeId,
                ServerHostId = hostId,
                WtsSessionId = dto.WtsSessionId,
                ClientName = dto.Type == ActivityEventType.RemoteConnect ? "RDP" : "console",
                StartedAt = dto.TimestampUtc,
                State = RdpSessionState.Active
            };
            _db.RdpSessions.Add(session);
        }

        return session!;
    }

    private async Task<RdpSession?> FindOpenSession(Guid employeeId, Guid hostId, CancellationToken ct) =>
        await _db.RdpSessions
            .Where(s => s.EmployeeId == employeeId
                     && s.ServerHostId == hostId
                     && s.EndedAt == null)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);

    private async Task<RdpSession> ResolveOpenSession(Guid employeeId, Guid hostId, int wtsSessionId, CancellationToken ct)
    {
        var session = await FindOpenSession(employeeId, hostId, ct);
        if (session is not null)
        {
            return session;
        }

        // Screenshot arrived before any session-open event — open a synthetic one.
        session = new RdpSession
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            ServerHostId = hostId,
            WtsSessionId = wtsSessionId,
            ClientName = "(synthesised)",
            StartedAt = DateTime.UtcNow,
            State = RdpSessionState.Active
        };
        _db.RdpSessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return session;
    }

    private (string StorageKey, string FullPath) BuildStoragePath(DateTime capturedAtUtc, string hostname)
    {
        var date = capturedAtUtc.ToString("yyyy-MM-dd");
        var safeHost = SanitizeForPath(hostname);
        var fileName = $"{capturedAtUtc:HHmmss-fff}-{Guid.NewGuid():N}.png";
        var relative = $"{date}/{safeHost}/{fileName}";
        var full = Path.Combine(_storage.ScreenshotsPath, date, safeHost, fileName);
        return (relative, full);
    }

    private static string SanitizeForPath(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "unknown-host" : clean;
    }
}
