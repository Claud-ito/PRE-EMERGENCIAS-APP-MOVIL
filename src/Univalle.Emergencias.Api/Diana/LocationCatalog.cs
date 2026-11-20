using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Locations;

namespace Univalle.Emergencias.Api.Diana;

public sealed class LocationCatalog(AppDbContext db) : ILocationCatalog
{
    public async Task<IReadOnlyList<ModuleLocation>> GetActiveModulesAsync(CancellationToken ct = default) =>
        await db.Set<LocationModule>().AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Code)
            .Select(x => new ModuleLocation(x.ModuleId, x.Code, x.Name, x.IsActive)).ToListAsync(ct);
    public async Task<IReadOnlyList<FloorLocation>> GetActiveFloorsAsync(Guid moduleId, CancellationToken ct = default) =>
        await db.Set<LocationFloor>().AsNoTracking().Where(x => x.ModuleId == moduleId && x.IsActive
            && db.Set<LocationModule>().Any(m => m.ModuleId == moduleId && m.IsActive))
            .OrderBy(x => x.Name).Select(x => new FloorLocation(x.FloorId, x.ModuleId, x.Name, x.IsActive)).ToListAsync(ct);
    public Task<ModuleLocation?> GetModuleAsync(Guid id, CancellationToken ct = default) =>
        db.Set<LocationModule>().AsNoTracking().Where(x => x.ModuleId == id)
            .Select(x => new ModuleLocation(x.ModuleId, x.Code, x.Name, x.IsActive)).SingleOrDefaultAsync(ct);
    public Task<FloorLocation?> GetFloorAsync(Guid id, CancellationToken ct = default) =>
        db.Set<LocationFloor>().AsNoTracking().Where(x => x.FloorId == id)
            .Select(x => new FloorLocation(x.FloorId, x.ModuleId, x.Name, x.IsActive)).SingleOrDefaultAsync(ct);
    public Task<bool> IsActiveFloorInModuleAsync(Guid moduleId, Guid floorId, CancellationToken ct = default) =>
        db.Set<LocationFloor>().AnyAsync(x => x.FloorId == floorId && x.ModuleId == moduleId && x.IsActive
            && db.Set<LocationModule>().Any(m => m.ModuleId == moduleId && m.IsActive), ct);
}
