using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Univalle.Emergencias.Contracts.Identity;

namespace Univalle.Emergencias.Api.Diana;

/// <summary>Utiliza exclusivamente la identidad real de SA-01 en cada solicitud.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class DianaAccessAttribute(string permission, bool physician = false) : Attribute, IAsyncAuthorizationFilter
{
    public const string ActorKey = "Diana.AuthenticatedActor";
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var identity = context.HttpContext.RequestServices.GetService<IIdentityContext>();
        if (identity is null) { Reject(context, 503, "identity_not_integrated", "El servicio de acceso todavía no está disponible."); return; }
        var user = await identity.GetCurrentAsync(context.HttpContext.RequestAborted);
        if (user is null) { Reject(context, 401, "unauthenticated", "Inicia sesión."); return; }
        if (!Allows(user, permission, physician)) {
            Reject(context, 403, "forbidden", "Tu cuenta no está autorizada para esta operación."); return;
        }
        context.HttpContext.Items[ActorKey] = user;
    }
    public static bool Allows(AuthenticatedUser user, string permission, bool physician) =>
        user.UserId != Guid.Empty && user.IsActive && user.Permissions.Contains(permission)
        && (physician ? user.Role == UserRole.Medico : user.Role is UserRole.SySO or UserRole.Mantenimiento);
    private static void Reject(AuthorizationFilterContext context, int status, string code, string title)
    {
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;
        context.Result = new ObjectResult(problem) { StatusCode = status };
    }
}
