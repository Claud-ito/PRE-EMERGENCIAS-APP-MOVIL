using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Incidents;
using Univalle.Emergencias.Contracts.Locations;
using Univalle.Emergencias.Contracts.Turns;

namespace Univalle.Emergencias.Api.Incidents;

public abstract record SubmitReportResult
{
    private SubmitReportResult() { }

    public sealed record Accepted(SubmitIncidentReportResponse Response) : SubmitReportResult;

    public sealed record Invalid(IReadOnlyDictionary<string, string[]> Errors) : SubmitReportResult;

    public sealed record LocationUnavailable : SubmitReportResult;
}

/// <summary>
/// RF07 y RF08: registra el reporte y lo une al incidente activo de su módulo y piso, o abre uno nuevo.
/// La unicidad la garantiza la base de datos (índice único filtrado), no la interfaz.
/// </summary>
public sealed class IncidentReportService(
    AppDbContext db,
    IServerClock clock,
    ILocationCatalog locations,
    ITurnResolver turns,
    IAuditTrail audit,
    IInitialAlertDispatcher alerts,
    ILogger<IncidentReportService> logger)
{
    /// <summary>Intentos ante la carrera por abrir el mismo incidente. El segundo ya encuentra el caso activo.</summary>
    private const int MaxAttempts = 3;

    public async Task<SubmitReportResult> SubmitAsync(
        Guid reporterUserId,
        SubmitIncidentReportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (reporterUserId == Guid.Empty)
        {
            throw new ArgumentException("El informante debe venir de la sesión.", nameof(reporterUserId));
        }

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return new SubmitReportResult.Invalid(errors);
        }

        var input = new ValidReport(
            reporterUserId,
            request.ClientRequestId!.Value,
            request.Type!.Value,
            request.ModuleId!.Value,
            request.FloorId!.Value,
            request.PreliminarySeverity!.Value,
            string.IsNullOrWhiteSpace(request.Details) ? null : request.Details.Trim());

        var replay = await FindReplayAsync(input, cancellationToken);
        if (replay is not null)
        {
            return new SubmitReportResult.Accepted(replay);
        }

        if (!await locations.IsActiveFloorInModuleAsync(input.ModuleId, input.FloorId, cancellationToken))
        {
            return new SubmitReportResult.LocationUnavailable();
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var (response, dispatchAlert) = await SubmitOnceAsync(input, cancellationToken);
                if (dispatchAlert)
                {
                    // Tras el commit. Sin el token de la solicitud: si el cliente se desconecta, el aviso sigue.
                    await alerts.TryDispatchAsync(response.IncidentId, CancellationToken.None);
                }

                return new SubmitReportResult.Accepted(response);
            }
            catch (DbUpdateException ex) when (attempt < MaxAttempts)
            {
                db.ChangeTracker.Clear();
                logger.LogInformation(
                    ex,
                    "Carrera al registrar el reporte en {ModuleId}/{FloorId}; intento {Attempt}.",
                    input.ModuleId,
                    input.FloorId,
                    attempt);

                replay = await FindReplayAsync(input, cancellationToken);
                if (replay is not null)
                {
                    return new SubmitReportResult.Accepted(replay);
                }
            }
        }
    }

    private async Task<(SubmitIncidentReportResponse Response, bool DispatchAlert)> SubmitOnceAsync(
        ValidReport input,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = clock.UtcNow.ToUniversalTime();

        var incident = await db.Incidents.SingleOrDefaultAsync(
            candidate => candidate.ModuleId == input.ModuleId
                && candidate.FloorId == input.FloorId
                && candidate.Status == IncidentStatuses.Active,
            cancellationToken);

        var created = incident is null;
        if (incident is null)
        {
            var coverage = await turns.ResolveAsync(now, cancellationToken);
            incident = new Incident
            {
                IncidentId = Guid.CreateVersion7(now),
                ModuleId = input.ModuleId,
                FloorId = input.FloorId,
                Type = input.Type,
                Status = IncidentStatuses.Active,
                OpenedAtUtc = now,
                CurrentCoordinatorUserId = coverage?.CoordinatorUserId,
                InitialAlertEventId = Guid.NewGuid(),
                InitialAlertRecipientUserId = coverage?.CoordinatorUserId,
                InitialAlertState = coverage is null ? InitialAlertStates.NoCoverage : InitialAlertStates.Pending,
                ConcurrencyStamp = Guid.NewGuid()
            };
            db.Incidents.Add(incident);
        }

        var report = new IncidentReport
        {
            ReportId = Guid.CreateVersion7(now),
            IncidentId = incident.IncidentId,
            ReporterUserId = input.ReporterUserId,
            ClientRequestId = input.ClientRequestId,
            Type = input.Type,
            ModuleId = input.ModuleId,
            FloorId = input.FloorId,
            PreliminarySeverity = input.Severity,
            Details = input.Details,
            ReportedAtUtc = now,
            IsInitial = created
        };
        db.IncidentReports.Add(report);
        await db.SaveChangesAsync(cancellationToken);

        if (created)
        {
            await audit.RecordAsync(
                new AuditWriteRequest(
                    input.ReporterUserId,
                    AuditActorKind.User,
                    "incident.opened",
                    "Incident",
                    incident.IncidentId,
                    incident.IncidentId,
                    JsonSerializer.Serialize(new
                    {
                        incident.ModuleId,
                        incident.FloorId,
                        incident.InitialAlertEventId,
                        CoordinatorAssigned = incident.CurrentCoordinatorUserId is not null
                    })),
                cancellationToken);
        }

        await audit.RecordAsync(
            new AuditWriteRequest(
                input.ReporterUserId,
                AuditActorKind.User,
                "incident.report-added",
                "IncidentReport",
                report.ReportId,
                incident.IncidentId,
                JsonSerializer.Serialize(new { report.IsInitial, Severity = report.PreliminarySeverity.ToString() })),
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return (
            new SubmitIncidentReportResponse(incident.IncidentId, report.ReportId, created, report.ReportedAtUtc),
            created && incident.InitialAlertState == InitialAlertStates.Pending);
    }

    private async Task<SubmitIncidentReportResponse?> FindReplayAsync(
        ValidReport input,
        CancellationToken cancellationToken)
    {
        var existing = await db.IncidentReports.AsNoTracking()
            .Where(report => report.ReporterUserId == input.ReporterUserId
                && report.ClientRequestId == input.ClientRequestId)
            .Select(report => new { report.IncidentId, report.ReportId, report.IsInitial, report.ReportedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);

        return existing is null
            ? null
            : new SubmitIncidentReportResponse(
                existing.IncidentId,
                existing.ReportId,
                existing.IsInitial,
                existing.ReportedAtUtc);
    }

    private static Dictionary<string, string[]> Validate(SubmitIncidentReportRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.ClientRequestId is null || request.ClientRequestId == Guid.Empty)
        {
            errors[nameof(request.ClientRequestId)] = ["Falta el identificador del envío."];
        }

        if (request.Type is null || !Enum.IsDefined(request.Type.Value))
        {
            errors[nameof(request.Type)] = ["Selecciona el tipo de incidente."];
        }

        if (request.ModuleId is null || request.ModuleId == Guid.Empty)
        {
            errors[nameof(request.ModuleId)] = ["Selecciona el módulo."];
        }

        if (request.FloorId is null || request.FloorId == Guid.Empty)
        {
            errors[nameof(request.FloorId)] = ["Selecciona el piso."];
        }

        if (request.PreliminarySeverity is null || !Enum.IsDefined(request.PreliminarySeverity.Value))
        {
            errors[nameof(request.PreliminarySeverity)] = ["Selecciona la gravedad preliminar."];
        }

        if (request.Details is { Length: > IncidentReportConfiguration.DetailsMaxLength })
        {
            errors[nameof(request.Details)] =
                [$"El detalle admite hasta {IncidentReportConfiguration.DetailsMaxLength} caracteres."];
        }

        return errors;
    }

    private sealed record ValidReport(
        Guid ReporterUserId,
        Guid ClientRequestId,
        IncidentType Type,
        Guid ModuleId,
        Guid FloorId,
        PreliminarySeverity Severity,
        string? Details);
}
