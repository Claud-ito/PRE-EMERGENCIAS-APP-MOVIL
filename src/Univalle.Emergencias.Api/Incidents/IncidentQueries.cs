using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Incidents;
using Univalle.Emergencias.Contracts.Locations;

namespace Univalle.Emergencias.Api.Incidents;

/// <summary>Implementación real del contrato de lectura del incidente (AL-01).</summary>
public sealed class EfIncidentReader(AppDbContext db) : IIncidentReader
{
    public Task<IncidentSnapshot?> GetByIdAsync(Guid incidentId, CancellationToken cancellationToken = default) =>
        db.Incidents.AsNoTracking()
            .Where(incident => incident.IncidentId == incidentId)
            .Select(incident => new IncidentSnapshot(
                incident.IncidentId,
                incident.ModuleId,
                incident.FloorId,
                incident.Status,
                incident.CurrentCoordinatorUserId,
                incident.OpenedAtUtc,
                incident.InitialAlertEventId,
                incident.ClosedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);
}

public abstract record CoordinatorQueryResult
{
    private CoordinatorQueryResult() { }

    public sealed record Found(CoordinatedIncidentView Incident) : CoordinatorQueryResult;

    public sealed record NotFound : CoordinatorQueryResult;

    public sealed record NotCoordinator : CoordinatorQueryResult;
}

/// <summary>Consultas del coordinador y opciones del formulario de reporte.</summary>
public sealed class IncidentQueries(AppDbContext db, ILocationCatalog locations)
{
    public async Task<ReportOptionsResponse> GetReportOptionsAsync(CancellationToken cancellationToken)
    {
        var modules = await locations.GetActiveModulesAsync(cancellationToken);
        var options = new List<ReportModuleOption>(modules.Count);
        foreach (var module in modules.Where(module => module.IsActive).OrderBy(module => module.Code))
        {
            var floors = await locations.GetActiveFloorsAsync(module.ModuleId, cancellationToken);
            var floorOptions = floors
                .Where(floor => floor.IsActive && floor.ModuleId == module.ModuleId)
                .Select(floor => new ReportFloorOption(floor.FloorId, floor.Name))
                .ToList();
            if (floorOptions.Count > 0)
            {
                options.Add(new ReportModuleOption(module.ModuleId, module.Code, module.Name, floorOptions));
            }
        }

        return new ReportOptionsResponse(options);
    }

    /// <summary>Incidentes activos cuyo coordinador vigente es el usuario.</summary>
    public async Task<IReadOnlyList<CoordinatedIncidentView>> GetAssignedAsync(
        Guid coordinatorUserId,
        CancellationToken cancellationToken)
    {
        var incidents = await db.Incidents.AsNoTracking()
            .Include(incident => incident.Reports)
            .Where(incident => incident.CurrentCoordinatorUserId == coordinatorUserId
                && incident.Status == IncidentStatuses.Active)
            .ToListAsync(cancellationToken);

        var views = new List<CoordinatedIncidentView>(incidents.Count);
        foreach (var incident in incidents.OrderByDescending(incident => incident.OpenedAtUtc))
        {
            views.Add(await ToViewAsync(incident, cancellationToken));
        }

        return views;
    }

    public async Task<CoordinatorQueryResult> GetForCoordinatorAsync(
        Guid incidentId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var incident = await db.Incidents.AsNoTracking()
            .Include(candidate => candidate.Reports)
            .SingleOrDefaultAsync(candidate => candidate.IncidentId == incidentId, cancellationToken);
        if (incident is null)
        {
            return new CoordinatorQueryResult.NotFound();
        }

        return incident.CurrentCoordinatorUserId == userId
            ? new CoordinatorQueryResult.Found(await ToViewAsync(incident, cancellationToken))
            : new CoordinatorQueryResult.NotCoordinator();
    }

    private async Task<CoordinatedIncidentView> ToViewAsync(Incident incident, CancellationToken cancellationToken)
    {
        // Las consultas por ID devuelven también ubicaciones dadas de baja (historial).
        var module = await locations.GetModuleAsync(incident.ModuleId, cancellationToken);
        var floor = await locations.GetFloorAsync(incident.FloorId, cancellationToken);
        var reports = incident.Reports
            .OrderBy(report => report.ReportedAtUtc)
            .Select(report => new IncidentReportView(
                report.ReportId,
                report.ReporterUserId,
                report.Type,
                report.PreliminarySeverity,
                report.Details,
                report.ReportedAtUtc,
                report.IsInitial))
            .ToList();

        return new CoordinatedIncidentView(
            incident.IncidentId,
            incident.ModuleId,
            module?.Name ?? "Módulo no disponible",
            incident.FloorId,
            floor?.Name ?? "Piso no disponible",
            incident.Type,
            incident.Status,
            incident.OpenedAtUtc,
            reports.Count,
            reports);
    }
}
