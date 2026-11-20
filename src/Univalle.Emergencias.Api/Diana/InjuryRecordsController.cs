using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Diana;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Incidents;

namespace Univalle.Emergencias.Api.Diana;

[ApiController, Route("api/v1/incidents/{incidentId:guid}/injury-records"), DianaAccess(PermissionCodes.RecordInjury, physician: true)]
public sealed class InjuryRecordsController(AppDbContext db, IAuditTrail audit, IServerClock clock) : DianaControllerBase(db, audit)
{
    private async Task<IActionResult?> CheckIncident(Guid id, CancellationToken ct) {
        var incidents = HttpContext.RequestServices.GetService<IIncidentReader>();
        if (incidents is null) return Error(503, "incidents_not_integrated", "La consulta de incidentes todavía no está disponible.");
        return id == Guid.Empty || await incidents.GetByIdAsync(id, ct) is null ? NotFound() : null;
    }
    [HttpGet]
    public async Task<IActionResult> List(Guid incidentId, CancellationToken ct) {
        var error = await CheckIncident(incidentId, ct); if (error is not null) return error;
        var records = await Db.Set<InjuryRecord>().AsNoTracking().Where(x => x.IncidentId == incidentId)
            .Select(x => new InjuryRecordView(x.InjuryRecordId, x.IncidentId, x.PhysicianUserId, x.CreatedAtUtc)).ToListAsync(ct);
        return Ok(records.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.InjuryRecordId).ToArray());
    }
    [HttpPost]
    public async Task<IActionResult> Create(Guid incidentId, CreateInjuryRecordInput input, CancellationToken ct) {
        var error = await CheckIncident(incidentId, ct); if (error is not null) return error;
        if (!input.EvaluatedOrAttended) return Error(400, "evaluation_required", "El registro se realiza después de atender o evaluar al accidentado.");
        var actor = (AuthenticatedUser)HttpContext.Items[DianaAccessAttribute.ActorKey]!;
        var item = new InjuryRecord { InjuryRecordId = Guid.NewGuid(), IncidentId = incidentId,
            PhysicianUserId = actor.UserId, CreatedAtUtc = clock.UtcNow.ToUniversalTime() };
        Db.Add(item);
        var result = await SaveAsync("injury-record.create", "InjuryRecord", item.InjuryRecordId, ct, incidentId);
        return result is NoContentResult ? Ok(new InjuryRecordView(item.InjuryRecordId, incidentId, actor.UserId, item.CreatedAtUtc)) : result;
    }
}
