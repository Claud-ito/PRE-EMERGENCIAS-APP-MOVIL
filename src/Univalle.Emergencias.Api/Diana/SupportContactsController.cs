using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Diana;
using Univalle.Emergencias.Contracts.Identity;

namespace Univalle.Emergencias.Api.Diana;

[ApiController, Route("api/v1/support-contacts"), DianaAccess(PermissionCodes.ManageSupportContacts)]
public sealed class SupportContactsController(AppDbContext db, IAuditTrail audit) : DianaControllerBase(db, audit)
{
    [HttpGet]
    public async Task<IActionResult> List(bool includeInactive = false, CancellationToken ct = default) =>
        Ok(await Db.Set<SupportContact>().AsNoTracking().Where(x => includeInactive || x.IsActive).OrderBy(x => x.Name)
            .Select(x => new SupportContactView(x.ContactId, x.Name, x.Kind, x.Phone, x.IsActive)).ToListAsync(ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) {
        var x = await Db.Set<SupportContact>().FindAsync([id], ct);
        return x is null ? NotFound() : Ok(new SupportContactView(x.ContactId, x.Name, x.Kind, x.Phone, x.IsActive));
    }
    [HttpPost]
    public async Task<IActionResult> Create(SupportContactInput input, CancellationToken ct) {
        if (!Valid(input)) return Error(400, "invalid_input", "Revisa el tipo de contacto y el teléfono.");
        var item = new SupportContact { ContactId = Guid.NewGuid(), Name = input.Name.Trim(), Kind = input.Kind, Phone = input.Phone.Trim() };
        Db.Add(item);
        var result = await SaveAsync("support-contact.create", "SupportContact", item.ContactId, ct);
        return result is NoContentResult ? Ok(new SupportContactView(item.ContactId, item.Name, item.Kind, item.Phone, true)) : result;
    }
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SupportContactInput input, CancellationToken ct) {
        if (!Valid(input)) return Error(400, "invalid_input", "Revisa el tipo de contacto y el teléfono.");
        var item = await Db.Set<SupportContact>().FindAsync([id], ct);
        if (item is null) return NotFound();
        if (!item.IsActive) return Error(409, "inactive_contact", "El contacto está inactivo.");
        item.Name = input.Name.Trim(); item.Kind = input.Kind; item.Phone = input.Phone.Trim();
        return await SaveAsync("support-contact.update", "SupportContact", id, ct);
    }
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct) {
        var item = await Db.Set<SupportContact>().FindAsync([id], ct);
        if (item is null) return NotFound();
        if (!item.IsActive) return NoContent();
        item.IsActive = false;
        return await SaveAsync("support-contact.deactivate", "SupportContact", id, ct);
    }
    private static bool Valid(SupportContactInput input) => Enum.IsDefined(input.Kind)
        && input.Phone.Any(char.IsDigit) && input.Phone.All(c => char.IsDigit(c) || "+-() .".Contains(c));
}
