using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Server;
using WorkTimeTracker.Server.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));

builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Stage 1 bootstrap: ensure schema + storage directory exist.
// TODO (stage 4): replace EnsureCreated with proper EF migrations.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var storage = scope.ServiceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<StorageOptions>>().Value;

    try
    {
        db.Database.EnsureCreated();
        Directory.CreateDirectory(storage.ScreenshotsPath);
        app.Logger.LogInformation(
            "Schema ready. Screenshots directory: {Path}", storage.ScreenshotsPath);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Bootstrap failed (DB or storage path)");
        throw;
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
