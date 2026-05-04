using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WorkTimeTracker.Data;

namespace WorkTimeTracker.Server;

/// <summary>
/// Validates the X-Agent-Hostname + X-Agent-Token pair on every agent
/// endpoint. Rules:
///
///   1. Both headers must be present and the token must meet
///      ServerOptions.MinTokenLength.
///   2. If a ServerHost row already exists for the hostname, the stored
///      AgentToken must match the presented one. Mismatch -> 401.
///   3. If no row exists yet, accept iff ServerOptions.AllowAutoRegister
///      is true. The controller is responsible for creating the row on
///      first contact.
///
/// On success, the resolved hostname is stashed in HttpContext.Items
/// under "AgentHostname" so the controller doesn't need to re-read the
/// header.
/// </summary>
public class AgentTokenAuthFilter : IAsyncActionFilter
{
    public const string HostnameHeader = "X-Agent-Hostname";
    public const string TokenHeader = "X-Agent-Token";
    public const string HostnameItemKey = "AgentHostname";

    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ServerOptions _options;
    private readonly ILogger<AgentTokenAuthFilter> _logger;

    public AgentTokenAuthFilter(
        IDbContextFactory<AppDbContext> dbFactory,
        IOptions<ServerOptions> options,
        ILogger<AgentTokenAuthFilter> logger)
    {
        _dbFactory = dbFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;

        var hostname = http.Request.Headers[HostnameHeader].ToString();
        var token = http.Request.Headers[TokenHeader].ToString();

        if (string.IsNullOrWhiteSpace(hostname))
        {
            context.Result = new UnauthorizedObjectResult(new { error = $"Missing {HostnameHeader}" });
            return;
        }

        if (string.IsNullOrWhiteSpace(token) || token.Length < _options.MinTokenLength)
        {
            _logger.LogWarning("Rejecting agent request from {Host}: token missing or too short", hostname);
            context.Result = new UnauthorizedObjectResult(new
            {
                error = $"Token must be at least {_options.MinTokenLength} characters"
            });
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync(http.RequestAborted);
        var host = await db.ServerHosts
            .AsNoTracking()
            .Select(h => new { h.Hostname, h.AgentToken })
            .FirstOrDefaultAsync(h => h.Hostname == hostname, http.RequestAborted);

        if (host is not null)
        {
            if (!FixedTimeEquals(host.AgentToken, token))
            {
                _logger.LogWarning("Rejecting agent request from {Host}: token mismatch", hostname);
                context.Result = new UnauthorizedObjectResult(new { error = "Token does not match registered host" });
                return;
            }
        }
        else if (!_options.AllowAutoRegister)
        {
            _logger.LogWarning("Rejecting agent request from {Host}: unknown host and auto-register disabled", hostname);
            context.Result = new UnauthorizedObjectResult(new { error = "Unknown host and auto-register disabled" });
            return;
        }

        http.Items[HostnameItemKey] = hostname;
        await next();
    }

    /// <summary>
    /// Constant-time string compare so the server doesn't leak token
    /// length / prefix via response timing.
    /// </summary>
    private static bool FixedTimeEquals(string? a, string? b)
    {
        if (a is null || b is null) return false;
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }
}
