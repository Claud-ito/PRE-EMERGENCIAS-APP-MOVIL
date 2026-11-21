
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Diana;
using Univalle.Emergencias.Contracts.Identity;

namespace Univalle.Emergencias.Api.Diana;

/// <summary>Selección mínima de incidentes para el médico; no expone aportes ni datos clínicos.</summary>
[ApiController, Route("api/v1/injury-records/incidents"), DianaAccess(PermissionCodes.RecordInjury, physician: true)]
public sealed class InjuryIncidentsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) {
        var cases = await (from incident in db.Incidents.AsNoTracking()
            join module in db.Set<LocationModule>() on incident.ModuleId equals module.ModuleId into moduleRows
            from module in moduleRows.DefaultIfEmpty()
            join floor in db.Set<LocationFloor>() on incident.FloorId equals floor.FloorId into floorRows
            from floor in floorRows.DefaultIfEmpty()
            select new InjuryIncidentView(incident.IncidentId,
                module == null ? incident.ModuleId.ToString() : module.Name,
                floor == null ? incident.FloorId.ToString() : floor.Name,
                incident.Status, incident.OpenedAtUtc)).ToListAsync(ct);
        return Ok(cases.OrderByDescending(x => x.OpenedAtUtc).ThenBy(x => x.IncidentId).ToArray());
    }
}
