using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Identity;

namespace Univalle.Emergencias.Api.Identity;

[ApiController]
[Route(AuthRoutes.Base)]
public sealed class AuthController(
    AppDbContext db,
    SessionService sessions,
    IServerClock clock) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await sessions.LoginAsync(request, cancellationToken);
        return result is null
            ? ProblemResult(StatusCodes.Status401Unauthorized, ApiErrorCodes.Unauthenticated,
                "Credenciales inválidas o cuenta inactiva.")
            : Ok(result);
    }

    [HttpGet("me")]
    [RequirePermission]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var actor = (AuthenticatedUser)HttpContext.Items[RequirePermissionAttribute.ActorKey]!;
        var user = await db.Users.AsNoTracking().SingleAsync(
            x => x.UserId == actor.UserId, cancellationToken);
        return Ok(new CurrentSessionResponse(RolePermissions.ToSessionUser(user), clock.UtcNow));
    }

    [HttpPost("logout")]
    [RequirePermission]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var actor = (AuthenticatedUser)HttpContext.Items[RequirePermissionAttribute.ActorKey]!;
        var token = BearerToken.FromRequest(Request)!;
        await sessions.RevokeAsync(token, actor.UserId, cancellationToken);
        return NoContent();
    }

    internal static ObjectResult ProblemResult(int status, string code, string title)
    {
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = status };
    }
}

[ApiController]
[Route(UserRoutes.Base)]
[RequirePermission(PermissionCodes.ManageUsers)]
public sealed class UsersController(
    AppDbContext db,
    IServerClock clock,
    IAuditTrail audit) : ControllerBase
{
    private static readonly Regex UsernamePattern =
        new("^[A-Za-z0-9._-]{3,50}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [HttpGet]
    public async Task<IActionResult> List(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var users = await db.Users.AsNoTracking()
            .Where(x => includeInactive || x.IsActive)
            .OrderBy(x => x.Username)
            .Select(x => new UserView(
                x.UserId, x.Username, x.Email, x.FullName, x.Role,
                x.IsActive, x.CreatedAtUtc, x.DeactivatedAtUtc))
            .ToListAsync(cancellationToken);
        return Ok(users);
    }

    [HttpGet("{userId:guid}")]
    public async Task<IActionResult> Get(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            x => x.UserId == userId, cancellationToken);
        return user is null ? NotFound() : Ok(ToView(user));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var error = Validate(request.Username, request.Email, request.FullName, request.Role, request.Password, true);
        if (error is not null) return BadInput(error);
        var duplicate = await DuplicateAsync(request.Username!, request.Email!, null, cancellationToken);
        if (duplicate is not null) return ConflictInput(duplicate);

        var now = clock.UtcNow.ToUniversalTime();
        var user = new UserAccount
        {
            UserId = Guid.CreateVersion7(now),
            Username = request.Username!.Trim(),
            NormalizedUsername = Normalize(request.Username),
            Email = request.Email!.Trim(),
            NormalizedEmail = Normalize(request.Email),
            FullName = request.FullName!.Trim(),
            Role = request.Role,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        PasswordCredentials.Set(user, request.Password!);
        db.Users.Add(user);
        try
        {
            await RecordAsync("user.create", user.UserId, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicate(ex))
        {
            db.ChangeTracker.Clear();
            return ConflictInput("El nombre de usuario o el correo ya existe.");
        }
        return CreatedAtAction(nameof(Get), new { userId = user.UserId }, ToView(user));
    }

    [HttpPut("{userId:guid}")]
    public async Task<IActionResult> Update(
        Guid userId, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var error = Validate(request.Username, request.Email, request.FullName,
            request.Role, request.NewPassword, false);
        if (error is not null) return BadInput(error);
        var user = await db.Users.Include(x => x.Sessions).SingleOrDefaultAsync(
            x => x.UserId == userId, cancellationToken);
        if (user is null) return NotFound();
        if (!user.IsActive) return ConflictInput("La cuenta está desactivada.");
        var actor = (AuthenticatedUser)HttpContext.Items[RequirePermissionAttribute.ActorKey]!;
        if (actor.UserId == userId && actor.Role != request.Role)
            return ConflictInput("No puedes cambiar tu propio rol.");
        var duplicate = await DuplicateAsync(request.Username!, request.Email!, userId, cancellationToken);
        if (duplicate is not null) return ConflictInput(duplicate);

        user.Username = request.Username!.Trim();
        user.NormalizedUsername = Normalize(request.Username);
        user.Email = request.Email!.Trim();
        user.NormalizedEmail = Normalize(request.Email);
        user.FullName = request.FullName!.Trim();
        var roleChanged = user.Role != request.Role;
        user.Role = request.Role;
        user.UpdatedAtUtc = clock.UtcNow.ToUniversalTime();
        if (request.NewPassword is not null) PasswordCredentials.Set(user, request.NewPassword);
        if (roleChanged || request.NewPassword is not null)
        {
            foreach (var session in user.Sessions.Where(x => x.RevokedAtUtc is null))
                session.RevokedAtUtc = user.UpdatedAtUtc;
        }
        try
        {
            await RecordAsync("user.update", userId, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicate(ex))
        {
            db.ChangeTracker.Clear();
            return ConflictInput("El nombre de usuario o el correo ya existe.");
        }
        return Ok(ToView(user));
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> Deactivate(Guid userId, CancellationToken cancellationToken)
    {
        var actor = (AuthenticatedUser)HttpContext.Items[RequirePermissionAttribute.ActorKey]!;
        if (actor.UserId == userId) return ConflictInput("No puedes desactivar tu propia cuenta.");
        var user = await db.Users.Include(x => x.Sessions).SingleOrDefaultAsync(
            x => x.UserId == userId, cancellationToken);
        if (user is null) return NotFound();
        if (!user.IsActive) return NoContent();
        var now = clock.UtcNow.ToUniversalTime();
        user.IsActive = false;
        user.DeactivatedAtUtc = now;
        user.UpdatedAtUtc = now;
        foreach (var session in user.Sessions.Where(x => x.RevokedAtUtc is null))
            session.RevokedAtUtc = now;
        await RecordAsync("user.deactivate", userId, cancellationToken);
        return NoContent();
    }

    /// <summary>Revierte una baja: la cuenta recupera el acceso con sus mismos datos e historial.</summary>
    [HttpPost("{userId:guid}/reactivate")]
    public async Task<IActionResult> Reactivate(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (user is null) return NotFound();
        if (user.IsActive) return NoContent();
        user.IsActive = true;
        user.DeactivatedAtUtc = null;
        user.UpdatedAtUtc = clock.UtcNow.ToUniversalTime();
        await RecordAsync("user.reactivate", userId, cancellationToken);
        return NoContent();
    }

    private async Task RecordAsync(string action, Guid userId, CancellationToken cancellationToken)
    {
        var actor = (AuthenticatedUser)HttpContext.Items[RequirePermissionAttribute.ActorKey]!;
        await audit.RecordAsync(new AuditWriteRequest(
            actor.UserId, AuditActorKind.User, action, "UserAccount", userId),
            cancellationToken);
    }

    private async Task<string?> DuplicateAsync(
        string username, string email, Guid? excludeId, CancellationToken cancellationToken)
    {
        var normalizedUsername = Normalize(username);
        var normalizedEmail = Normalize(email);
        if (await db.Users.AnyAsync(x => x.NormalizedUsername == normalizedUsername
            && x.UserId != excludeId, cancellationToken))
            return "El nombre de usuario ya existe.";
        if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalizedEmail
            && x.UserId != excludeId, cancellationToken))
            return "El correo ya existe.";
        return null;
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();

    private static string? Validate(
        string? username, string? email, string? fullName, UserRole role,
        string? password, bool requirePassword)
    {
        if (username is null || !UsernamePattern.IsMatch(username.Trim()))
            return "El usuario debe tener 3 a 50 caracteres: letras, números, punto, guion o guion bajo.";
        var cleanEmail = email?.Trim();
        if (string.IsNullOrEmpty(cleanEmail) || cleanEmail.Length > 254
            || !MailAddress.TryCreate(cleanEmail, out var address)
            || !string.Equals(address.Address, cleanEmail, StringComparison.OrdinalIgnoreCase))
            return "Escribe un correo válido de hasta 254 caracteres.";
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length is < 2 or > 150)
            return "El nombre completo debe tener entre 2 y 150 caracteres.";
        if (!Enum.IsDefined(role)) return "Selecciona uno de los seis roles operativos.";
        if ((requirePassword || password is not null)
            && (password is null || password.Length is < 12 or > 128
                || string.IsNullOrWhiteSpace(password)))
            return "La contraseña debe tener entre 12 y 128 caracteres.";
        return null;
    }

    private static UserView ToView(UserAccount user) =>
        new(user.UserId, user.Username, user.Email, user.FullName,
            user.Role, user.IsActive, user.CreatedAtUtc, user.DeactivatedAtUtc);

    private static ObjectResult BadInput(string title) =>
        AuthController.ProblemResult(400, ApiErrorCodes.InvalidInput, title);

    private static ObjectResult ConflictInput(string title) =>
        AuthController.ProblemResult(409, "user_conflict", title);

    private static bool IsDuplicate(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
