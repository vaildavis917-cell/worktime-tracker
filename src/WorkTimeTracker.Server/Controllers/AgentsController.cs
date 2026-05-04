using Microsoft.AspNetCore.Mvc;
using WorkTimeTracker.Server.Data;
using WorkTimeTracker.Shared.Dtos;

namespace WorkTimeTracker.Server.Controllers;

[ApiController]
[Route("api/agents")]
public class AgentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<AgentsController> _logger;

    public AgentsController(AppDbContext db, ILogger<AgentsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpPost("heartbeat")]
    public ActionResult<AgentHeartbeatResponse> Heartbeat([FromBody] AgentHeartbeatDto dto)
    {
        // TODO: validate X-Agent-Token, upsert ServerHost, update LastHeartbeatAt
        _logger.LogDebug("Heartbeat from {Host} v{Ver}", dto.Hostname, dto.AgentVersion);
        return Ok(new AgentHeartbeatResponse(Guid.Empty, RequestFullSync: false));
    }

    [HttpPost("events")]
    public IActionResult Events([FromBody] ActivityEventBatchDto batch)
    {
        // TODO: resolve employee by SAM, attach RdpSession, persist events
        _logger.LogDebug("Events batch from {Host}: {Count}", batch.Hostname, batch.Events.Count);
        return Accepted();
    }

    [HttpPost("screenshots")]
    [RequestSizeLimit(20_000_000)]
    public IActionResult Screenshots()
    {
        // TODO: read multipart form, persist PNG to storage, write Screenshot row
        return Accepted();
    }
}
