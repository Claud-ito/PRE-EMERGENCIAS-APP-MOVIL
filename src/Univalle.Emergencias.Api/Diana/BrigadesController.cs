using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Brigades;
using Univalle.Emergencias.Contracts.Diana;
using Univalle.Emergencias.Contracts.Identity;

namespace Univalle.Emergencias.Api.Diana;

[ApiController, Route("api/v1/brigades"), DianaAccess(PermissionCodes.ManageBrigades)]
public sealed class BrigadesController(AppDbContext db, IAuditTrail audit, IServerClock clock,
    IConfiguration configuration) : DianaControllerBase(db, audit)
{
    private string[] Specialties => configuration.GetSection("Diana:BrigadeSpecialties").Get<string[]>() ?? [];
    [HttpGet("specialties")]
    public IActionResult GetSpecialties() => SpecialtiesValid() ? Ok(Specialties) :
        Error(503, "specialties_pending_confirmation", "Falta confirmar las tres especialidades del Sprint 0.");
    private bool SpecialtiesValid() => Specialties.Length == 3 && Specialties.All(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 100)
        && Specialties.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 3;
    [HttpGet("users")]
    public async Task<IActionResult> Users(CancellationToken ct) {
        var users = HttpContext.RequestServices.GetService<IBrigadeUserDirectory>();
        return users is null ? Error(503, "users_not_integrated", "La consulta de cuentas todavía no está disponible.") :
            Ok((await users.GetAssignableUsersAsync(ct)).Where(x => x.IsActive && x.Role is UserRole.LiderBrigada or UserRole.Brigadista));
    }
    [HttpGet("modules/{moduleId:guid}/leader")]
    public async Task<IActionResult> ModuleLeader(Guid moduleId, CancellationToken ct) {
        if (!await Db.Set<LocationModule>().AnyAsync(x => x.ModuleId == moduleId, ct)) return NotFound();
        var directory = HttpContext.RequestServices.GetService<IBrigadeUserDirectory>();
        if (directory is null) return Error(503, "users_not_integrated", "La consulta de cuentas todavía no está disponible.");
        var lookup = HttpContext.RequestServices.GetRequiredService<IBrigadeLeaderLookup>();
        var leader = await lookup.GetCurrentLeaderAsync(moduleId, clock.UtcNow, ct);
        if (leader is null) return Ok(new ModuleBrigadeStatus(moduleId, null));
        var brigade = await Db.Set<Brigade>().AsNoTracking().SingleAsync(x => x.BrigadeId == leader.BrigadeId, ct);
        var users = await directory.GetAssignableUsersAsync(ct);
        var leaderName = users.FirstOrDefault(x => x.UserId == leader.UserId)?.DisplayName ?? leader.UserId.ToString();
        var members = leader.Members.Select(m => new BrigadeMemberView(m.UserId,
            users.FirstOrDefault(u => u.UserId == m.UserId)?.DisplayName ?? m.UserId.ToString(), m.Specialty)).ToArray();
        return Ok(new ModuleBrigadeStatus(moduleId, new BrigadeLeaderView(brigade.BrigadeId, brigade.Name,
            leader.UserId, leaderName, members)));
    }
    [HttpGet]
    public async Task<IActionResult> List(bool includeInactive = false, CancellationToken ct = default) {
        var brigades = await Db.Set<Brigade>().AsNoTracking().Include(x => x.Members).Where(x => includeInactive || x.IsActive).OrderBy(x => x.Name).ToListAsync(ct);
        var assignments = await Db.Set<BrigadeModuleAssignment>().AsNoTracking().Where(x => x.EndedAtUtc == null).ToListAsync(ct);
        return Ok(brigades.Select(x => new BrigadeView(x.BrigadeId, x.Name, x.LeaderUserId, x.IsActive,
            x.Members.Select(m => new BrigadeMemberInput(m.UserId, m.Specialty)).ToArray(), assignments.Where(a => a.BrigadeId == x.BrigadeId).Select(a => a.ModuleId).ToArray())));
    }
    [HttpPost]
    public Task<IActionResult> Create(BrigadeInput input, CancellationToken ct) => Store(null, input, ct);
    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, BrigadeInput input, CancellationToken ct) => Store(id, input, ct);
    private async Task<IActionResult> Store(Guid? id, BrigadeInput input, CancellationToken ct) {
        if (!SpecialtiesValid()) return GetSpecialties();
        if (input.Members is null || input.ModuleIds is null || input.LeaderUserId == Guid.Empty
            || input.Members.Count == 0 || input.Members.Any(x => x is null || x.UserId == Guid.Empty || !Specialties.Contains(x.Specialty))
            || input.Members.Select(x => x.UserId).Distinct().Count() != input.Members.Count
            || input.ModuleIds.Any(x => x == Guid.Empty) || input.ModuleIds.Distinct().Count() != input.ModuleIds.Count
            || !input.Members.Any(x => x.UserId == input.LeaderUserId)) return Error(400, "invalid_brigade", "Revisa integrantes, especialidades, líder y módulos.");
        var directory = HttpContext.RequestServices.GetService<IBrigadeUserDirectory>();
        if (directory is null) return Error(503, "users_not_integrated", "La consulta de cuentas todavía no está disponible.");
        var users = await directory.GetAssignableUsersAsync(ct);
        if (!users.Any(x => x.UserId == input.LeaderUserId && x.IsActive && x.Role == UserRole.LiderBrigada)
            || input.Members.Any(m => !users.Any(x => x.UserId == m.UserId && x.IsActive && x.Role is UserRole.LiderBrigada or UserRole.Brigadista)))
            return Error(400, "invalid_members", "Selecciona cuentas activas de líder y brigadistas.");
        if (await Db.Set<LocationModule>().CountAsync(x => input.ModuleIds.Contains(x.ModuleId) && x.IsActive, ct) != input.ModuleIds.Count)
            return Error(400, "invalid_modules", "Selecciona módulos activos.");
        if (await Db.Set<BrigadeModuleAssignment>().AnyAsync(x => input.ModuleIds.Contains(x.ModuleId) && x.EndedAtUtc == null && x.BrigadeId != id, ct))
            return Error(409, "module_already_assigned", "Un módulo ya tiene otra brigada vigente.");
        var name = input.Name.Trim();
        if (await Db.Set<Brigade>().AnyAsync(x => x.Name == name && x.BrigadeId != id, ct)) return Error(409, "duplicate_brigade", "El nombre ya existe.");
        Brigade? brigade = id.HasValue ? await Db.Set<Brigade>().Include(x => x.Members).SingleOrDefaultAsync(x => x.BrigadeId == id, ct) : new() { BrigadeId = Guid.NewGuid() };
        if (brigade is null) return NotFound();
        if (!brigade.IsActive) return Error(409, "inactive_brigade", "La brigada está inactiva.");
        if (!id.HasValue) Db.Add(brigade);
        brigade.Name = name; brigade.LeaderUserId = input.LeaderUserId;
        foreach (var member in brigade.Members.ToArray()) {
            var current = input.Members.SingleOrDefault(x => x.UserId == member.UserId);
            if (current is null) { Db.Remove(member); brigade.Members.Remove(member); }
            else member.Specialty = current.Specialty;
        }
        foreach (var member in input.Members.Where(x => !brigade.Members.Any(m => m.UserId == x.UserId)))
            brigade.Members.Add(new() { BrigadeId = brigade.BrigadeId, UserId = member.UserId, Specialty = member.Specialty });
        var now = clock.UtcNow.ToUniversalTime();
        var old = await Db.Set<BrigadeModuleAssignment>().Where(x => x.BrigadeId == brigade.BrigadeId && x.EndedAtUtc == null).ToListAsync(ct);
        // Cerrar antes de insertar permite el índice único filtrado; la transacción incluye ambos y auditoría.
        await using var transaction = await Db.Database.BeginTransactionAsync(ct);
        foreach (var assignment in old) assignment.EndedAtUtc = now;
        try { await Db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException sql && sql.Number is 2601 or 2627) {
            return Error(409, "duplicate_or_assignment_conflict", "El nombre o módulo ya está asignado. Actualiza los datos.");
        }
        foreach (var moduleId in input.ModuleIds) Db.Add(new BrigadeModuleAssignment {
            AssignmentId = Guid.NewGuid(), BrigadeId = brigade.BrigadeId, ModuleId = moduleId,
            LeaderUserId = brigade.LeaderUserId, MembersJson = JsonSerializer.Serialize(input.Members), StartedAtUtc = now });
        var result = await SaveAsync(id.HasValue ? "brigade.update" : "brigade.create", "Brigade", brigade.BrigadeId, ct);
        if (result is NoContentResult) await transaction.CommitAsync(ct);
        return result is NoContentResult ? Ok(new BrigadeView(brigade.BrigadeId, name, brigade.LeaderUserId, true, input.Members, input.ModuleIds)) : result;
    }
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) {
        var brigade = await Db.Set<Brigade>().FindAsync([id], ct);
        if (brigade is null) return NotFound();
        if (!brigade.IsActive) return NoContent();
        brigade.IsActive = false;
        var now = clock.UtcNow.ToUniversalTime();
        foreach (var assignment in await Db.Set<BrigadeModuleAssignment>().Where(x => x.BrigadeId == id && x.EndedAtUtc == null).ToListAsync(ct)) assignment.EndedAtUtc = now;
        return await SaveAsync("brigade.deactivate", "Brigade", id, ct);
    }
}
