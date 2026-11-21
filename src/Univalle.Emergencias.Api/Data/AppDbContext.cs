
using Microsoft.EntityFrameworkCore;
<<<<<<< Updated upstream
using Univalle.Emergencias.Api.Incidents;
using Univalle.Emergencias.Api.Identity;
using Univalle.Emergencias.Api.Suggestions;
=======
using Univalle.Emergencias.Api.Models;
>>>>>>> Stashed changes

namespace Univalle.Emergencias.Api.Data;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
<<<<<<< Updated upstream
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentReport> IncidentReports => Set<IncidentReport>();
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<IncidentSuggestion> IncidentSuggestions => Set<IncidentSuggestion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Cada módulo mantiene sus IEntityTypeConfiguration en su carpeta (reparto, regla 10.3).
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        Univalle.Emergencias.Api.Diana.DianaModel.Configure(modelBuilder);

=======
    public DbSet<AuditEntry> AuditEntries =>
        Set<AuditEntry>();

    // Modulo de turnos - Jorge
    public DbSet<WorkSchedule> WorkSchedules =>
        Set<WorkSchedule>();

    public DbSet<ScheduleException> ScheduleExceptions =>
        Set<ScheduleException>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configuracion existente de auditoria
>>>>>>> Stashed changes
        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("AuditEntries");

            entity.HasKey(entry => entry.AuditEntryId);

            entity.Property(entry => entry.AuditEntryId)
                .ValueGeneratedNever();

            entity.Property(entry => entry.ActorKind)
                .HasConversion<string>()
                .HasMaxLength(16)
                .IsRequired();

            entity.Property(entry => entry.Action)
                .HasMaxLength(120)
                .IsRequired();

            entity.Property(entry => entry.EntityType)
                .HasMaxLength(80)
                .IsRequired();

            entity.Property(entry => entry.OccurredAtUtc)
                .IsRequired();

            entity.Property(entry => entry.MetadataJson)
                .HasMaxLength(4000);

            entity.HasIndex(entry => entry.OccurredAtUtc);
            entity.HasIndex(entry => entry.IncidentId);
        });

        // Configuracion de horarios
        modelBuilder.Entity<WorkSchedule>(entity =>
        {
            entity.ToTable("WorkSchedules");

            entity.HasKey(x => x.WorkScheduleId);

            entity.Property(x => x.CoordinatorUserId)
                .IsRequired();

            entity.Property(x => x.DayOfWeek)
                .HasConversion<int>()
                .IsRequired();

            entity.Property(x => x.StartTime)
                .IsRequired();

            entity.Property(x => x.EndTime)
                .IsRequired();

            entity.Property(x => x.IsActive)
                .IsRequired();

            entity.HasIndex(x => new
            {
                x.CoordinatorUserId,
                x.DayOfWeek
            });
        });

        // Configuracion de excepciones
        modelBuilder.Entity<ScheduleException>(entity =>
        {
            entity.ToTable("ScheduleExceptions");

            entity.HasKey(x => x.ScheduleExceptionId);

            entity.Property(x => x.CoordinatorUserId)
                .IsRequired();

            entity.Property(x => x.Date)
                .IsRequired();

            entity.Property(x => x.Reason)
                .HasMaxLength(250);

            entity.Property(x => x.IsAvailable)
                .IsRequired();

            entity.Property(x => x.IsActive)
                .IsRequired();

            entity.HasIndex(x => new
            {
                x.CoordinatorUserId,
                x.Date
            });
        });
    }
}
