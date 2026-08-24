using GvrLicense.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GvrLicense.Infrastructure;

public sealed class LicenseDbContext(DbContextOptions<LicenseDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<UsageCounter> UsageCounters => Set<UsageCounter>();
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();
    public DbSet<Release> Releases => Set<Release>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<License>()
            .HasIndex(l => l.Key)
            .IsUnique();

        modelBuilder.Entity<Plan>()
            .Property(p => p.Features)
            .HasColumnType("jsonb");

        modelBuilder.Entity<Device>()
            .HasIndex(d => new { d.LicenseId, d.Fingerprint })
            .IsUnique();

        modelBuilder.Entity<UsageCounter>()
            .HasIndex(c => new { c.LicenseId, c.FeatureCode, c.Period })
            .IsUnique();

        // UsageEvent.Id ES el EventId que manda el cliente: la PK ya basta como constraint de
        // idempotencia (docs/LICENSING_PLAN.md, "Dónde vive la lógica: app vs Postgres"). No se
        // genera con ValueGeneratedOnAdd -- el valor siempre viene del cliente.
        modelBuilder.Entity<UsageEvent>()
            .Property(e => e.Id)
            .ValueGeneratedNever();

        // consume_quota() y el trigger de auditoría se crean vía migración con Sql(...) apuntando a
        // los scripts en Sql/ -- no tienen equivalente Fluent API porque no son mapeo de entidades.
    }
}
