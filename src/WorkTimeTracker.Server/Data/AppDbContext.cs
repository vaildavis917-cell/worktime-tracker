using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Shared.Models;

namespace WorkTimeTracker.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<ServerHost> ServerHosts => Set<ServerHost>();
    public DbSet<RdpSession> RdpSessions => Set<RdpSession>();
    public DbSet<ActivityEvent> ActivityEvents => Set<ActivityEvent>();
    public DbSet<Screenshot> Screenshots => Set<Screenshot>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Employee>(e =>
        {
            e.HasIndex(x => x.SamAccountName).IsUnique();
            e.Property(x => x.SamAccountName).HasMaxLength(256);
            e.Property(x => x.DisplayName).HasMaxLength(256);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Department).HasMaxLength(256);
        });

        b.Entity<ServerHost>(e =>
        {
            e.HasIndex(x => x.Hostname).IsUnique();
            e.Property(x => x.Hostname).HasMaxLength(256);
            e.Property(x => x.AgentToken).HasMaxLength(128);
        });

        b.Entity<RdpSession>(e =>
        {
            e.HasIndex(x => new { x.ServerHostId, x.WtsSessionId, x.StartedAt });
            e.HasOne(x => x.Employee).WithMany(x => x.Sessions).HasForeignKey(x => x.EmployeeId);
            e.HasOne(x => x.ServerHost).WithMany().HasForeignKey(x => x.ServerHostId);
            e.Property(x => x.ClientName).HasMaxLength(256);
            e.Property(x => x.ClientAddress).HasMaxLength(64);
        });

        b.Entity<ActivityEvent>(e =>
        {
            e.HasIndex(x => new { x.RdpSessionId, x.Timestamp });
            e.HasOne(x => x.RdpSession).WithMany().HasForeignKey(x => x.RdpSessionId);
            e.Property(x => x.ProcessName).HasMaxLength(256);
            e.Property(x => x.WindowTitle).HasMaxLength(512);
            e.Property(x => x.Url).HasMaxLength(2048);
        });

        b.Entity<Screenshot>(e =>
        {
            e.HasIndex(x => new { x.RdpSessionId, x.CapturedAt });
            e.HasOne(x => x.RdpSession).WithMany().HasForeignKey(x => x.RdpSessionId);
            e.Property(x => x.StorageKey).HasMaxLength(512);
            e.Property(x => x.TriggerProcess).HasMaxLength(256);
        });

        base.OnModelCreating(b);
    }
}
