using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Brigades;
using Univalle.Emergencias.Contracts.Diana;

namespace Univalle.Emergencias.Api.Diana;

public sealed class BrigadeLeaderLookup(AppDbContext db, IServiceProvider services) : IBrigadeLeaderLookup
{
    public async Task<ModuleLeader?> GetCurrentLeaderAsync(Guid moduleId, DateTimeOffset instantUtc, CancellationToken ct = default)
    {
        if (!await db.Set<LocationModule>().AnyAsync(x => x.ModuleId == moduleId && x.IsActive, ct)) return null;
        var instant = instantUtc.ToUniversalTime();
        var assignment = await db.Set<BrigadeModuleAssignment>().AsNoTracking().SingleOrDefaultAsync(x =>
            x.ModuleId == moduleId && x.StartedAtUtc <= instant && (x.EndedAtUtc == null || instant < x.EndedAtUtc), ct);
        if (assignment is null) return null;
        var directory = services.GetService<IBrigadeUserDirectory>()
            ?? throw new InvalidOperationException("Falta integrar IBrigadeUserDirectory (SA-02).");
        var users = await directory.GetAssignableUsersAsync(ct);
        if (!users.Any(x => x.UserId == assignment.LeaderUserId && x.IsActive && x.Role == Contracts.Identity.UserRole.LiderBrigada)) return null;
        var members = JsonSerializer.Deserialize<BrigadeMemberInput[]>(assignment.MembersJson) ?? [];
        return new(assignment.LeaderUserId, assignment.BrigadeId, moduleId,
            members.Where(m => users.Any(u => u.UserId == m.UserId && u.IsActive)).Select(x => new BrigadeMember(x.UserId, x.Specialty)).ToArray());
    }
}
