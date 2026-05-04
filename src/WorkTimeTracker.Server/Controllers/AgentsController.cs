using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WorkTimeTracker.Server.Data;
using WorkTimeTracker.Shared.Dtos;
using WorkTimeTracker.Shared.Models;

namespace WorkTimeTracker.Server.Controllers;

[ApiController]
[Route("api/agents")]
public class AgentsController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
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
        await _db.SaveChangesAsync(ct);

        _logger.LogDebug("Heartbeat from {Host} v{Ver} (sessions={Active})",
            dto.Hostname, dto.AgentVersion, dto.ActiveSessions);

        return Ok(new AgentHeartbeatResponse(host.Id, RequestFullSync: false));
    }

    [HttpPost("events")]
    public IActionResult Events([FromBody] ActivityEventBatchDto batch)
    {
        // TODO (stage 2/4): persist events; for now just acknowledge so the
        // agent's flush loop can be wired up incrementally.
        _logger.LogDebug("Events batch from {Host}: {Count}", batch.Hostname, batch.Events.Count);
        return Accepted();
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
            host = new ServerHost
            {
                Id = Guid.NewGuid(),
                Hostname = hostname,
                AgentToken = "auto-registered",
                CreatedAt = DateTime.UtcNow
            };
            _db.ServerHosts.Add(host);
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

    private async Task<RdpSession> ResolveOpenSession(Guid employeeId, Guid hostId, int wtsSessionId, CancellationToken ct)
    {
        var session = await _db.RdpSessions
            .Where(s => s.EmployeeId == employeeId
                     && s.ServerHostId == hostId
                     && s.EndedAt == null)
            .OrderByDescending(s => s.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (session is not null)
        {
            return session;
        }

        // Stage 1 placeholder: stage 2 (WTS monitor) will replace this with
        // real session lifecycle from SESSION_LOGON / REMOTE_CONNECT events.
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
