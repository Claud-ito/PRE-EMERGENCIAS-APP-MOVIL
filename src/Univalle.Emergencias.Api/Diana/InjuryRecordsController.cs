using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
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
            .ToListAsync(ct);
        return Ok(records.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.InjuryRecordId).Select(View).ToArray());
    }
    [HttpPost]
    public async Task<IActionResult> Create(Guid incidentId, CreateInjuryRecordInput input, CancellationToken ct) {
        var error = await CheckIncident(incidentId, ct); if (error is not null) return error;
        if (!input.EvaluatedOrAttended) return Error(400, "evaluation_required", "El registro se realiza después de atender o evaluar al accidentado.");
        if (input.TestClinicalDraft is { } draft) {
            if (!HttpContext.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("Diana:ClinicalPrototype:Enabled"))
                return Error(409, "clinical_prototype_disabled", "La plantilla de prueba no está habilitada en este entorno.");
            if (!draft.IsTestData || string.IsNullOrWhiteSpace(draft.PatientAlias)
                || string.IsNullOrWhiteSpace(draft.Observations) || string.IsNullOrWhiteSpace(draft.CareProvided)
                || string.IsNullOrWhiteSpace(draft.Report))
                return Error(400, "test_data_required", "Esta plantilla provisional requiere datos ficticios identificados como prueba.");
        }
        var actor = (AuthenticatedUser)HttpContext.Items[DianaAccessAttribute.ActorKey]!;
        var item = new InjuryRecord { InjuryRecordId = Guid.NewGuid(), IncidentId = incidentId,
            PhysicianUserId = actor.UserId, CreatedAtUtc = clock.UtcNow.ToUniversalTime(),
            TestClinicalDraftJson = input.TestClinicalDraft is null ? null : JsonSerializer.Serialize(input.TestClinicalDraft) };
        Db.Add(item);
        var result = await SaveAsync("injury-record.create", "InjuryRecord", item.InjuryRecordId, ct, incidentId);
        return result is NoContentResult ? Ok(View(item)) : result;
    }
    [HttpGet("{recordId:guid}/test-report")]
    public async Task<IActionResult> Report(Guid incidentId, Guid recordId, CancellationToken ct) {
        var error = await CheckIncident(incidentId, ct); if (error is not null) return error;
        var item = await Db.Set<InjuryRecord>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.IncidentId == incidentId && x.InjuryRecordId == recordId, ct);
        if (item is null) return NotFound();
        var draft = View(item).TestClinicalDraft;
        if (draft is null) return Error(409, "no_test_clinical_draft", "La ficha base no contiene una plantilla de prueba.");
        var text = $"INFORME DE ENSAYO — DATOS FICTICIOS — SIN VALIDEZ CLÍNICA\n" +
            $"Ficha: {item.InjuryRecordId}\nIncidente: {item.IncidentId}\nMédico: {item.PhysicianUserId}\n" +
            $"Fecha UTC: {item.CreatedAtUtc:O}\nAlias ficticio: {draft.PatientAlias}\nEdad de ensayo: {draft.Age?.ToString() ?? "No indicada"}\n" +
            $"Observaciones de ensayo: {draft.Observations}\nAtención de ensayo: {draft.CareProvided}\nInforme de ensayo: {draft.Report}";
        return Ok(new TestClinicalReport(item.InjuryRecordId, incidentId, item.PhysicianUserId,
            item.CreatedAtUtc, "SYSO-ENSAYO-v1", true, text));
    }
    private static InjuryRecordView View(InjuryRecord item) => new(item.InjuryRecordId, item.IncidentId,
        item.PhysicianUserId, item.CreatedAtUtc, item.TestClinicalDraftJson is null ? null :
            JsonSerializer.Deserialize<TestClinicalDraft>(item.TestClinicalDraftJson));
}
