using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Identity;

namespace Univalle.Emergencias.Api.Identity;

public static class RolePermissions
{
    public static IReadOnlySet<string> For(UserRole role)
    {
        var permissions = new HashSet<string>
        {
            PermissionCodes.ReportIncident,
            PermissionCodes.ReadSuggestions,
            PermissionCodes.WriteSuggestions
        };
        switch (role)
        {
            case UserRole.SySO:
            case UserRole.Mantenimiento:
                permissions.UnionWith([
                    PermissionCodes.ManageUsers,
                    PermissionCodes.ManageLocations,
                    PermissionCodes.ManageBrigades,
                    PermissionCodes.ManageSupportContacts,
                    PermissionCodes.ConfigureOwnAvailability,
                    PermissionCodes.CoordinateAssignedIncident,
                    PermissionCodes.ReceiveNotifications
                ]);
                break;
            case UserRole.LiderBrigada:
            case UserRole.Brigadista:
                permissions.Add(PermissionCodes.ReceiveNotifications);
                break;
            case UserRole.Medico:
                permissions.Add(PermissionCodes.RecordInjury);
                permissions.Add(PermissionCodes.ReceiveNotifications);
                break;
        }
        return permissions;
    }

    public static SessionUser ToSessionUser(UserAccount user) =>
        new(user.UserId, user.Username, user.FullName, user.Role,
            For(user.Role).OrderBy(x => x, StringComparer.Ordinal).ToArray());
}

public static class PasswordCredentials
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static void Set(UserAccount user, string password)
    {
        user.PasswordSalt = RandomNumberGenerator.GetBytes(SaltSize);
        user.PasswordIterations = Iterations;
        user.PasswordHash = Rfc2898DeriveBytes.Pbkdf2(
            password, user.PasswordSalt, Iterations, HashAlgorithmName.SHA256, HashSize);
    }

    public static bool Verify(UserAccount user, string password)
    {
        if (user.PasswordSalt.Length != SaltSize
            || user.PasswordHash.Length != HashSize
            || user.PasswordIterations < 100_000)
        {
            return false;
        }
        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password, user.PasswordSalt, user.PasswordIterations, HashAlgorithmName.SHA256, HashSize);
        return CryptographicOperations.FixedTimeEquals(actual, user.PasswordHash);
    }
}

public static class BearerToken
{
    public static string Create() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static string? FromRequest(HttpRequest request)
    {
        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization.ToString(), out var header)
            || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(header.Parameter)
            || header.Parameter.Length != 43)
        {
            return null;
        }
        return header.Parameter;
    }
}

/// <summary>Consulta la sesión y el estado actual de la cuenta en cada solicitud.</summary>
public sealed class DatabaseIdentityContext(
    IHttpContextAccessor accessor,
    AppDbContext db,
    IServerClock clock) : IIdentityContext
{
    public async Task<AuthenticatedUser?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var request = accessor.HttpContext?.Request;
        var token = request is null ? null : BearerToken.FromRequest(request);
        if (token is null) return null;

        var hash = BearerToken.Hash(token);
        var session = await db.UserSessions.AsNoTracking()
            .Include(x => x.User)
            .SingleOrDefaultAsync(x => x.TokenHash == hash, cancellationToken);
        if (session is null || session.RevokedAtUtc is not null
            || session.ExpiresAtUtc <= clock.UtcNow
            || !session.User.IsActive
            || !Enum.IsDefined(session.User.Role))
        {
            return null;
        }
        return new AuthenticatedUser(
            session.UserId, true, session.User.Role, RolePermissions.For(session.User.Role));
    }
}

/// <summary>Filtro común para operaciones protegidas. Los módulos pueden usarlo directamente.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequirePermissionAttribute(string? permission = null) : Attribute, IAsyncAuthorizationFilter
{
    public const string ActorKey = "Identity.AuthenticatedActor";

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var identity = context.HttpContext.RequestServices.GetRequiredService<IIdentityContext>();
        var user = await identity.GetCurrentAsync(context.HttpContext.RequestAborted);
        if (user is null || !user.IsActive)
        {
            Reject(context, StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthenticated,
                "Se requiere una sesión activa.");
            return;
        }
        if (permission is not null && !user.Permissions.Contains(permission))
        {
            Reject(context, StatusCodes.Status403Forbidden, ApiErrorCodes.Forbidden,
                "La cuenta no tiene permiso para esta operación.");
            return;
        }
        context.HttpContext.Items[ActorKey] = user;
    }

    private static void Reject(AuthorizationFilterContext context, int status, string code, string title)
    {
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;
        context.Result = new ObjectResult(problem) { StatusCode = status };
    }
}

public sealed class SessionService(
    AppDbContext db,
    IServerClock clock,
    IAuditTrail audit)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    public async Task<LoginResponse?> LoginAsync(
        LoginRequest request, CancellationToken cancellationToken = default)
    {
        var username = request.Username?.Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(request.Password))
            return null;

        var normalized = username.ToUpperInvariant();
        var user = await db.Users.SingleOrDefaultAsync(
            x => x.NormalizedUsername == normalized, cancellationToken);
        if (user is null || !user.IsActive || !PasswordCredentials.Verify(user, request.Password))
            return null;

        var now = clock.UtcNow.ToUniversalTime();
        var token = BearerToken.Create();
        var expires = now.Add(Lifetime);
        db.UserSessions.Add(new UserSession
        {
            SessionId = Guid.CreateVersion7(now),
            UserId = user.UserId,
            TokenHash = BearerToken.Hash(token),
            CreatedAtUtc = now,
            ExpiresAtUtc = expires
        });
        await audit.RecordAsync(new AuditWriteRequest(
            user.UserId, AuditActorKind.User, "auth.login", "UserAccount", user.UserId),
            cancellationToken);
        return new LoginResponse(token, expires, RolePermissions.ToSessionUser(user));
    }

    public async Task RevokeAsync(string token, Guid userId, CancellationToken cancellationToken = default)
    {
        var hash = BearerToken.Hash(token);
        var session = await db.UserSessions.SingleOrDefaultAsync(
            x => x.TokenHash == hash && x.UserId == userId, cancellationToken);
        if (session is null || session.RevokedAtUtc is not null) return;
        session.RevokedAtUtc = clock.UtcNow.ToUniversalTime();
        await audit.RecordAsync(new AuditWriteRequest(
            userId, AuditActorKind.User, "auth.logout", "UserSession", session.SessionId),
            cancellationToken);
    }
}
