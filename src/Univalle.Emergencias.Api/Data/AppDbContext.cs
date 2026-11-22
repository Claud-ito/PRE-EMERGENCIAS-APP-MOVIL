
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Incidents;
using Univalle.Emergencias.Api.Identity;
using Univalle.Emergencias.Api.Suggestions;
using Univalle.Emergencias.Api.Models;
using Univalle.Emergencias.Api.Notifications;

namespace Univalle.Emergencias.Api.Data;

public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    // ==========================================
    // AUDITORIA - SAID
    // ==========================================

    public DbSet<AuditEntry> AuditEntries =>
        Set<AuditEntry>();

    // ==========================================
    // INCIDENTES - ALEJANDRO
    // ==========================================

    public DbSet<Incident> Incidents =>
        Set<Incident>();

    public DbSet<IncidentReport> IncidentReports =>
        Set<IncidentReport>();

    // ==========================================
    // USUARIOS Y AUTENTICACION - SAID
    // ==========================================

    public DbSet<UserAccount> Users =>
        Set<UserAccount>();

    public DbSet<UserSession> UserSessions =>
        Set<UserSession>();

    // ==========================================
    // BUZON DE SUGERENCIAS
    // ==========================================

    public DbSet<IncidentSuggestion> IncidentSuggestions =>
        Set<IncidentSuggestion>();

    // ==========================================
    // TURNOS - JORGE
    // ==========================================

    public DbSet<WorkSchedule> WorkSchedules =>
        Set<WorkSchedule>();

    public DbSet<ScheduleException> ScheduleExceptions =>
        Set<ScheduleException>();

    public DbSet<Coordination.AutoPreference> AutoPreferences => Set<Coordination.AutoPreference>();
    public DbSet<Coordination.Callout> Callouts => Set<Coordination.Callout>();
    public DbSet<Coordination.Handoff> Handoffs => Set<Coordination.Handoff>();

    // JO-02: Notificaciones persistentes
    public DbSet<NotificationEntry> NotificationEntries =>
        Set<NotificationEntry>();

    // ==========================================
    // CONFIGURACION DE ENTIDADES
    // ==========================================

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Coordination.Handoff>().HasIndex(x => x.IncidentId)
            .IsUnique().HasFilter("[AcceptedAtUtc] IS NULL");
        modelBuilder.Entity<Incident>().HasIndex(x => x.AutoDueAtUtc)
            .HasFilter("[AutoDueAtUtc] IS NOT NULL");

        // Configuraciones de los otros modulos
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly);

        // Configuraciones del modulo de Diana
        Univalle.Emergencias.Api.Diana.DianaModel.Configure(
            modelBuilder);

        // ======================================
        // AUDITORIA
        // ======================================

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

        // ======================================
        // HORARIOS - JORGE
        // ======================================

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

        // ======================================
        // EXCEPCIONES DE HORARIOS - JORGE
        // ======================================

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
