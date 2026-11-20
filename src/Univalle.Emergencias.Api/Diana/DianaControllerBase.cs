using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Identity;

namespace Univalle.Emergencias.Api.Diana;

public abstract class DianaControllerBase(AppDbContext db, IAuditTrail audit) : ControllerBase
{
    protected AppDbContext Db => db;
    protected ObjectResult Error(int status, string code, string title) {
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = status };
    }
    protected async Task<IActionResult> SaveAsync(string action, string entity, Guid id,
        CancellationToken ct, Guid? incidentId = null)
    {
        var actor = (AuthenticatedUser)HttpContext.Items[DianaAccessAttribute.ActorKey]!;
        // EfAuditTrail guarda los cambios y la auditoría en un mismo SaveChanges/transacción EF.
        try {
            await audit.RecordAsync(new(actor.UserId, AuditActorKind.User, action, entity, id, incidentId), ct);
            return NoContent();
        } catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && sql.Number is 2601 or 2627) {
            Db.ChangeTracker.Clear();
            return Error(409, "duplicate_or_assignment_conflict", "El código, nombre o módulo ya está asignado. Actualiza los datos.");
        }
    }
}
