using Univalle.Emergencias.Contracts.Common;

namespace Univalle.Emergencias.Contracts.Identity;

public static class AuthRoutes
{
    public const string Base = ApiRoutes.ApiV1 + "/auth";
    public const string Login = Base + "/login";
    public const string Me = Base + "/me";
    public const string Logout = Base + "/logout";
}

public static class UserRoutes
{
    public const string Base = ApiRoutes.ApiV1 + "/users";
}

public sealed record LoginRequest(string? Username, string? Password);

public sealed record SessionUser(
    Guid UserId,
    string Username,
    string FullName,
    UserRole Role,
    IReadOnlyList<string> Permissions);

public sealed record LoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    SessionUser User);

public sealed record CurrentSessionResponse(SessionUser User, DateTimeOffset ServerTimeUtc);

public sealed record UserView(
    Guid UserId,
    string Username,
    string Email,
    string FullName,
    UserRole Role,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? DeactivatedAtUtc);

public sealed record CreateUserRequest(
    string? Username,
    string? Email,
    string? FullName,
    UserRole Role,
    string? Password);

public sealed record UpdateUserRequest(
    string? Username,
    string? Email,
    string? FullName,
    UserRole Role,
    string? NewPassword);
