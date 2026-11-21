using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Diana;
using Univalle.Emergencias.Contracts.Identity;

namespace Univalle.Emergencias.Api.Identity;

public sealed class BrigadeUserDirectory(AppDbContext db) : IBrigadeUserDirectory
{
    public async Task<IReadOnlyList<BrigadeUser>> GetAssignableUsersAsync(
        CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking()
            .Where(x => x.IsActive && (x.Role == UserRole.LiderBrigada || x.Role == UserRole.Brigadista))
            .OrderBy(x => x.FullName)
            .Select(x => new BrigadeUser(x.UserId, x.FullName, x.Role, x.IsActive))
            .ToListAsync(cancellationToken);
}
