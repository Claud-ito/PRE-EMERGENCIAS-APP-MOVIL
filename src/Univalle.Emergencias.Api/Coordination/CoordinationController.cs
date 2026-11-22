using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Identity;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Incidents;
using Univalle.Emergencias.Contracts.Turns;

namespace Univalle.Emergencias.Api.Coordination;

[ApiController, Route("api/v1/coordination"), RequirePermission(PermissionCodes.CoordinateAssignedIncident)]
public sealed class CoordinationController(AppDbContext db, CalloutService calls,
    IServerClock clock, IAuditTrail audit) : ControllerBase
{
    private Guid Actor => ((AuthenticatedUser)HttpContext.Items[RequirePermissionAttribute.ActorKey]!).UserId;

    [HttpGet("auto")]
    public async Task<AutoSetting> Preference(CancellationToken ct) =>
        new(await db.AutoPreferences.AnyAsync(x => x.CoordinatorUserId == Actor && x.Enabled, ct));

    [HttpPut("auto")]
    public async Task<IActionResult> Configure(AutoSetting setting, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var preference = await db.AutoPreferences.FindAsync([Actor], ct);
        if (preference is null)
            db.AutoPreferences.Add(preference = new AutoPreference { CoordinatorUserId = Actor });
        preference.Enabled = setting.Enabled;
        if (!setting.Enabled)
        {
            var pending = await db.Incidents.Where(x => x.InitialAlertRecipientUserId == Actor &&
                x.AutoDueAtUtc != null).ToListAsync(ct);
            foreach (var incident in pending)
            {
                incident.AutoDueAtUtc = null;
                incident.ConcurrencyStamp = Guid.NewGuid();
            }
        }
        await audit.RecordAsync(new AuditWriteRequest(Actor, AuditActorKind.User,
            setting.Enabled ? "coordination.auto.enable" : "coordination.auto.disable",
            "AutoPreference", Actor), ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/manual")]
    public async Task<IActionResult> Manual(Guid id, CancellationToken ct)
    {
        try { return await calls.RegisterAsync(id, Actor, ct) ? NoContent() : Conflict(); }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
    }

    [HttpGet("callouts")]
    public Task<List<CalloutView>> Callouts(CancellationToken ct) =>
        (from call in db.Callouts
         join incident in db.Incidents on call.IncidentId equals incident.IncidentId
         where incident.CurrentCoordinatorUserId == Actor || call.SenderUserId == Actor
         orderby call.OccurredAtUtc descending
         select new CalloutView(call.IncidentId, call.SenderUserId, call.RecipientUserId,
             call.Channel, call.OccurredAtUtc)).Take(100).ToListAsync(ct);

    [HttpGet("coordinators")]
    public Task<List<CoordinatorOption>> Coordinators(CancellationToken ct) =>
        db.Users.Where(x => x.UserId != Actor && x.IsActive &&
            (x.Role == UserRole.SySO || x.Role == UserRole.Mantenimiento))
            .OrderBy(x => x.FullName).Select(x => new CoordinatorOption(x.UserId, x.FullName)).ToListAsync(ct);

    [HttpGet("handoffs")]
    public Task<List<HandoffView>> History(CancellationToken ct) =>
        db.Handoffs.Where(x => x.ToUserId == Actor || x.FromUserId == Actor)
            .Select(x => new HandoffView(x.Id, x.IncidentId, x.FromUserId, x.ToUserId,
                x.ProposedAtUtc, x.AcceptedAtUtc)).ToListAsync(ct);

    [HttpPost("{id:guid}/handoffs")]
    public async Task<IActionResult> Propose(Guid id, CoordinationRecipient target, CancellationToken ct)
    {
        var incident = await db.Incidents.SingleOrDefaultAsync(x => x.IncidentId == id &&
            x.Status == IncidentStatuses.Active && x.CurrentCoordinatorUserId == Actor, ct);
        if (incident is null) return NotFound();
        if (target.UserId == Actor || !await db.Users.AnyAsync(x => x.UserId == target.UserId && x.IsActive &&
            (x.Role == UserRole.SySO || x.Role == UserRole.Mantenimiento), ct)) return BadRequest();
        if (await db.Handoffs.AnyAsync(x => x.IncidentId == id && x.AcceptedAtUtc == null, ct)) return Conflict();
        incident.ConcurrencyStamp = Guid.NewGuid();
        var proposal = new Handoff { Id = Guid.NewGuid(), IncidentId = id,
            FromUserId = Actor, ToUserId = target.UserId, ProposedAtUtc = clock.UtcNow };
        db.Handoffs.Add(proposal);
        try
        {
            await audit.RecordAsync(new AuditWriteRequest(Actor, AuditActorKind.User,
                "coordination.handoff.propose", "Handoff", proposal.Id, id), ct);
        }
        catch (DbUpdateConcurrencyException) { return Conflict(); }
        catch (DbUpdateException) when (db.Database.IsSqlServer())
        {
            if (await db.Handoffs.AnyAsync(x => x.IncidentId == id && x.AcceptedAtUtc == null, ct)) return Conflict();
            throw;
        }
        return NoContent();
    }

    [HttpPost("handoffs/{id:guid}/accept")]
    public async Task<IActionResult> Accept(Guid id, CancellationToken ct)
    {
        var handoff = await db.Handoffs.SingleOrDefaultAsync(x => x.Id == id && x.ToUserId == Actor, ct);
        if (handoff is null) return NotFound();
        if (handoff.AcceptedAtUtc.HasValue) return NoContent();
        var incident = await db.Incidents.SingleOrDefaultAsync(x => x.IncidentId == handoff.IncidentId &&
            x.Status == IncidentStatuses.Active && x.CurrentCoordinatorUserId == handoff.FromUserId, ct);
        if (incident is null) return Conflict();
        incident.CurrentCoordinatorUserId = Actor;
        incident.ConcurrencyStamp = Guid.NewGuid();
        handoff.AcceptedAtUtc = clock.UtcNow;
        try
        {
            await audit.RecordAsync(new AuditWriteRequest(Actor, AuditActorKind.User,
                "coordination.handoff.accept", "Handoff", handoff.Id,
                handoff.IncidentId), ct);
        }
        catch (DbUpdateConcurrencyException) { return Conflict(); }
        return NoContent();
    }
}
