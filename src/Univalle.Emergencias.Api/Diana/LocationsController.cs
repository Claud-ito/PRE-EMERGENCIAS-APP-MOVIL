using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Diana;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Locations;

namespace Univalle.Emergencias.Api.Diana;

[ApiController, Route("api/v1/locations"), DianaAccess(PermissionCodes.ManageLocations)]
public sealed class LocationsController(AppDbContext db, IAuditTrail audit) : DianaControllerBase(db, audit)
{
    [HttpGet("modules")]
    public async Task<IActionResult> Modules(bool includeInactive = false, CancellationToken ct = default) =>
        Ok(await Db.Set<LocationModule>().AsNoTracking().Where(x => includeInactive || x.IsActive)
            .OrderBy(x => x.Code).Select(x => new ModuleLocation(x.ModuleId, x.Code, x.Name, x.IsActive)).ToListAsync(ct));
    [HttpPost("modules")]
    public async Task<IActionResult> CreateModule(ModuleInput input, CancellationToken ct) {
        var code = input.Code.Trim().ToUpperInvariant();
        if (await Db.Set<LocationModule>().AnyAsync(x => x.Code == code, ct)) return Error(409, "duplicate_module", "El código ya existe, incluso entre módulos inactivos.");
        var item = new LocationModule { ModuleId = Guid.NewGuid(), Code = code, Name = input.Name.Trim() };
        Db.Add(item);
        var result = await SaveAsync("location.module.create", "LocationModule", item.ModuleId, ct);
        return result is NoContentResult ? Ok(new ModuleLocation(item.ModuleId, item.Code, item.Name, true)) : result;
    }
    [HttpPut("modules/{id:guid}")]
    public async Task<IActionResult> UpdateModule(Guid id, ModuleInput input, CancellationToken ct) {
        var item = await Db.Set<LocationModule>().FindAsync([id], ct);
        if (item is null) return NotFound();
        if (!item.IsActive) return Error(409, "inactive_module", "El módulo está inactivo.");
        var code = input.Code.Trim().ToUpperInvariant();
        if (await Db.Set<LocationModule>().AnyAsync(x => x.ModuleId != id && x.Code == code, ct)) return Error(409, "duplicate_module", "El código ya existe.");
        item.Code = code; item.Name = input.Name.Trim();
        return await SaveAsync("location.module.update", "LocationModule", id, ct);
    }
    [HttpDelete("modules/{id:guid}")]
    public async Task<IActionResult> DeactivateModule(Guid id, CancellationToken ct) {
        var item = await Db.Set<LocationModule>().FindAsync([id], ct);
        if (item is null) return NotFound();
        if (!item.IsActive) return NoContent();
        item.IsActive = false;
        return await SaveAsync("location.module.deactivate", "LocationModule", id, ct);
    }
    [HttpGet("modules/{moduleId:guid}/floors")]
    public async Task<IActionResult> Floors(Guid moduleId, bool includeInactive = false, CancellationToken ct = default) {
        var module = await Db.Set<LocationModule>().FindAsync([moduleId], ct);
        if (module is null) return NotFound();
        return Ok(await Db.Set<LocationFloor>().AsNoTracking().Where(x => x.ModuleId == moduleId && (includeInactive || (x.IsActive && module.IsActive)))
            .OrderBy(x => x.Name).Select(x => new FloorLocation(x.FloorId, x.ModuleId, x.Name, x.IsActive)).ToListAsync(ct));
    }
    [HttpPost("modules/{moduleId:guid}/floors")]
    public async Task<IActionResult> CreateFloor(Guid moduleId, FloorInput input, CancellationToken ct) {
        if (!await Db.Set<LocationModule>().AnyAsync(x => x.ModuleId == moduleId && x.IsActive, ct)) return Error(409, "inactive_module", "Selecciona un módulo activo.");
        var name = input.Name.Trim();
        if (await Db.Set<LocationFloor>().AnyAsync(x => x.ModuleId == moduleId && x.Name == name, ct)) return Error(409, "duplicate_floor", "El piso ya existe en este módulo.");
        var item = new LocationFloor { FloorId = Guid.NewGuid(), ModuleId = moduleId, Name = name };
        Db.Add(item);
        var result = await SaveAsync("location.floor.create", "LocationFloor", item.FloorId, ct);
        return result is NoContentResult ? Ok(new FloorLocation(item.FloorId, moduleId, name, true)) : result;
    }
    [HttpPut("floors/{id:guid}")]
    public async Task<IActionResult> UpdateFloor(Guid id, FloorInput input, CancellationToken ct) {
        var item = await Db.Set<LocationFloor>().FindAsync([id], ct);
        if (item is null) return NotFound();
        if (!item.IsActive || !await Db.Set<LocationModule>().AnyAsync(x => x.ModuleId == item.ModuleId && x.IsActive, ct)) return Error(409, "inactive_location", "La ubicación está inactiva.");
        var name = input.Name.Trim();
        if (await Db.Set<LocationFloor>().AnyAsync(x => x.ModuleId == item.ModuleId && x.FloorId != id && x.Name == name, ct)) return Error(409, "duplicate_floor", "El piso ya existe en este módulo.");
        item.Name = name;
        return await SaveAsync("location.floor.update", "LocationFloor", id, ct);
    }
    [HttpDelete("floors/{id:guid}")]
    public async Task<IActionResult> DeactivateFloor(Guid id, CancellationToken ct) {
        var item = await Db.Set<LocationFloor>().FindAsync([id], ct);
        if (item is null) return NotFound();
        if (!item.IsActive) return NoContent();
        item.IsActive = false;
        return await SaveAsync("location.floor.deactivate", "LocationFloor", id, ct);
    }
}
