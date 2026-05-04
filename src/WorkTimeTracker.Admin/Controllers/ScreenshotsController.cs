using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WorkTimeTracker.Data;

namespace WorkTimeTracker.Admin.Controllers;

[ApiController]
[Authorize]
[Route("admin-api/screenshots")]
public class ScreenshotsController : ControllerBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly AdminStorageOptions _storage;
    private readonly ILogger<ScreenshotsController> _logger;

    public ScreenshotsController(
        IDbContextFactory<AppDbContext> dbFactory,
        IOptions<AdminStorageOptions> storage,
        ILogger<ScreenshotsController> logger)
    {
        _dbFactory = dbFactory;
        _storage = storage.Value;
        _logger = logger;
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var shot = await db.Screenshots.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (shot is null)
        {
            return NotFound();
        }

        var fullPath = Path.Combine(_storage.ScreenshotsPath, shot.StorageKey.Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(fullPath))
        {
            _logger.LogWarning("Screenshot {Id} missing on disk: {Path}", id, fullPath);
            return NotFound();
        }

        var stream = System.IO.File.OpenRead(fullPath);
        return File(stream, "image/png");
    }
}
