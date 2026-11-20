using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Univalle.Emergencias.Contracts.Incidents;

namespace Univalle.Emergencias.Api.Incidents;

public sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    /// <summary>
    /// Nombre del índice que garantiza un único incidente activo por módulo y piso (RF08).
    /// El filtro usa corchetes, válidos tanto en SQL Server como en SQLite (pruebas).
    /// </summary>
    public const string ActiveLocationIndexName = "UX_Incidents_ActiveLocation";

    public void Configure(EntityTypeBuilder<Incident> entity)
    {
        entity.ToTable("Incidents");
        entity.HasKey(incident => incident.IncidentId);
        entity.Property(incident => incident.IncidentId).ValueGeneratedNever();
        entity.Property(incident => incident.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(incident => incident.Status).HasMaxLength(16).IsRequired();
        entity.Property(incident => incident.OpenedAtUtc).IsRequired();
        entity.Property(incident => incident.InitialAlertEventId).IsRequired();
        entity.Property(incident => incident.InitialAlertState).HasMaxLength(16).IsRequired();
        entity.Property(incident => incident.InitialAlertDeliveryStatus).HasMaxLength(64);
        entity.Property(incident => incident.ConcurrencyStamp).IsConcurrencyToken();

        entity.HasIndex(incident => new { incident.ModuleId, incident.FloorId })
            .HasDatabaseName(ActiveLocationIndexName)
            .IsUnique()
            .HasFilter($"[Status] = '{IncidentStatuses.Active}'");
        entity.HasIndex(incident => incident.InitialAlertEventId).IsUnique();
        entity.HasIndex(incident => new { incident.CurrentCoordinatorUserId, incident.Status });
        entity.HasIndex(incident => new { incident.InitialAlertState, incident.InitialAlertLastAttemptAtUtc });

        entity.HasMany(incident => incident.Reports)
            .WithOne(report => report.Incident)
            .HasForeignKey(report => report.IncidentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class IncidentReportConfiguration : IEntityTypeConfiguration<IncidentReport>
{
    public const int DetailsMaxLength = 500;

    public void Configure(EntityTypeBuilder<IncidentReport> entity)
    {
        entity.ToTable("IncidentReports");
        entity.HasKey(report => report.ReportId);
        entity.Property(report => report.ReportId).ValueGeneratedNever();
        entity.Property(report => report.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(report => report.PreliminarySeverity).HasConversion<string>().HasMaxLength(16).IsRequired();
        entity.Property(report => report.Details).HasMaxLength(DetailsMaxLength);
        entity.Property(report => report.ReportedAtUtc).IsRequired();

        // Un mismo formulario reenviado (doble toque o reintento de red) no crea otro aporte.
        entity.HasIndex(report => new { report.ReporterUserId, report.ClientRequestId }).IsUnique();
        entity.HasIndex(report => new { report.IncidentId, report.ReportedAtUtc });
    }
}
