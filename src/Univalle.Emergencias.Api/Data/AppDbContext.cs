using Microsoft.EntityFrameworkCore;

namespace Univalle.Emergencias.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("AuditEntries");
            entity.HasKey(entry => entry.AuditEntryId);
            entity.Property(entry => entry.AuditEntryId).ValueGeneratedNever();
            entity.Property(entry => entry.ActorKind)
                .HasConversion<string>()
                .HasMaxLength(16)
                .IsRequired();
            entity.Property(entry => entry.Action).HasMaxLength(120).IsRequired();
            entity.Property(entry => entry.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(entry => entry.OccurredAtUtc).IsRequired();
            entity.Property(entry => entry.MetadataJson).HasMaxLength(4000);
            entity.HasIndex(entry => entry.OccurredAtUtc);
            entity.HasIndex(entry => entry.IncidentId);
        });
    }
}
