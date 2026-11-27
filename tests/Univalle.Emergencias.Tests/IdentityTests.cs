using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Identity;
using Univalle.Emergencias.Api.Services;
using Univalle.Emergencias.Contracts.Identity;
using Xunit;

namespace Univalle.Emergencias.Tests;

public sealed class IdentityTests
{
    [Fact]
    public void PasswordsUseDistinctSaltsAndRejectWrongValues()
    {
        var first = new UserAccount();
        var second = new UserAccount();
        PasswordCredentials.Set(first, "clave-de-prueba-segura");
        PasswordCredentials.Set(second, "clave-de-prueba-segura");

        Assert.NotEqual(first.PasswordSalt, second.PasswordSalt);
        Assert.NotEqual(first.PasswordHash, second.PasswordHash);
        Assert.True(PasswordCredentials.Verify(first, "clave-de-prueba-segura"));
        Assert.False(PasswordCredentials.Verify(first, "otra-clave-de-prueba"));
    }

    [Fact]
    public async Task ExistingSessionExpiresAndStopsWorkingAfterDeactivation()
    {
        await using var connection = await OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var provider = new ManualTimeProvider(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
        var clock = new SystemServerClock(provider);
        await using var db = new AppDbContext(options);
        var user = CreateUser(clock, "testigo", UserRole.ComunidadTestigo);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var sessions = new SessionService(db, clock, new EfAuditTrail(db, clock));

        Assert.Null(await sessions.LoginAsync(new LoginRequest("testigo", "incorrecta")));
        var login = await sessions.LoginAsync(new LoginRequest("testigo", "clave-de-prueba-segura"));
        Assert.NotNull(login);
        Assert.Equal(provider.GetUtcNow().AddHours(8), login.ExpiresAtUtc);
        Assert.NotEqual(db.UserSessions.Single().TokenHash, login.AccessToken);

        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = $"Bearer {login.AccessToken}";
        var identity = new DatabaseIdentityContext(
            new HttpContextAccessor { HttpContext = http }, db, clock);
        Assert.Equal(user.UserId, (await identity.GetCurrentAsync())?.UserId);

        user.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Null(await identity.GetCurrentAsync());
        user.IsActive = true;
        await db.SaveChangesAsync();
        provider.Advance(TimeSpan.FromHours(8));
        Assert.Null(await identity.GetCurrentAsync());
    }

    [Fact]
    public void ManagerAndPhysicianPermissionsStaySeparate()
    {
        Assert.Equal(6, Enum.GetValues<UserRole>().Length);
        foreach (var role in Enum.GetValues<UserRole>())
        {
            Assert.Contains("suggestions.read", RolePermissions.For(role));
            Assert.Contains("suggestions.write", RolePermissions.For(role));
        }
        Assert.Contains("users.manage", RolePermissions.For(UserRole.SySO));
        Assert.Contains("users.manage", RolePermissions.For(UserRole.Mantenimiento));
        Assert.DoesNotContain("injuries.record", RolePermissions.For(UserRole.SySO));
        Assert.DoesNotContain("injuries.record", RolePermissions.For(UserRole.Mantenimiento));
        Assert.Contains("injuries.record", RolePermissions.For(UserRole.Medico));
        Assert.DoesNotContain("users.manage", RolePermissions.For(UserRole.Medico));
    }

    [Fact]
    public async Task ManagersCreateUniqueAccountsAndDeactivationRevokesSessions()
    {
        await using var connection = await OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var clock = new SystemServerClock(
            new ManualTimeProvider(new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.Zero)));
        await using var db = new AppDbContext(options);
        var manager = CreateUser(clock, "siso", UserRole.SySO);
        db.Users.Add(manager);
        await db.SaveChangesAsync();
        var controller = new UsersController(db, clock, new EfAuditTrail(db, clock))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.Items[RequirePermissionAttribute.ActorKey] =
            new AuthenticatedUser(manager.UserId, true, manager.Role, RolePermissions.For(manager.Role));

        var input = new CreateUserRequest(
            "lider1", "lider@example.test", "Líder de prueba", UserRole.LiderBrigada,
            "clave-de-prueba-segura");
        var created = Assert.IsType<CreatedAtActionResult>(
            await controller.Create(input, CancellationToken.None));
        var view = Assert.IsType<UserView>(created.Value);
        Assert.True(view.IsActive);
        var listed = Assert.IsType<OkObjectResult>(
            await controller.List(cancellationToken: CancellationToken.None));
        Assert.Contains(Assert.IsAssignableFrom<IReadOnlyList<UserView>>(listed.Value),
            x => x.UserId == view.UserId);
        Assert.IsType<ObjectResult>(await controller.Create(
            input with { Username = "LIDER1", Email = "otro@example.test" },
            CancellationToken.None));
        Assert.IsType<ObjectResult>(await controller.Create(
            input with { Username = "otro1" },
            CancellationToken.None));

        var token = BearerToken.Create();
        db.UserSessions.Add(new UserSession
        {
            SessionId = Guid.NewGuid(),
            UserId = view.UserId,
            TokenHash = BearerToken.Hash(token),
            CreatedAtUtc = clock.UtcNow,
            ExpiresAtUtc = clock.UtcNow.AddHours(8)
        });
        await db.SaveChangesAsync();
        Assert.IsType<NoContentResult>(
            await controller.Deactivate(view.UserId, CancellationToken.None));
        Assert.False((await db.Users.SingleAsync(x => x.UserId == view.UserId)).IsActive);
        Assert.NotNull((await db.UserSessions.SingleAsync(x => x.UserId == view.UserId)).RevokedAtUtc);
        Assert.NotEmpty(await db.AuditEntries.Where(x => x.EntityId == view.UserId).ToListAsync());

        // RF03: la baja se puede revertir; la cuenta vuelve a estar activa con su mismo identificador.
        Assert.IsType<NoContentResult>(
            await controller.Reactivate(view.UserId, CancellationToken.None));
        var reactivated = await db.Users.SingleAsync(x => x.UserId == view.UserId);
        Assert.True(reactivated.IsActive);
        Assert.Null(reactivated.DeactivatedAtUtc);
        Assert.Contains(await db.AuditEntries.Where(x => x.EntityId == view.UserId).ToListAsync(),
            x => x.Action == "user.reactivate");
        Assert.IsType<NotFoundResult>(
            await controller.Reactivate(Guid.NewGuid(), CancellationToken.None));
    }

    private static UserAccount CreateUser(SystemServerClock clock, string name, UserRole role)
    {
        var user = new UserAccount
        {
            UserId = Guid.NewGuid(),
            Username = name,
            NormalizedUsername = name.ToUpperInvariant(),
            Email = $"{name}@example.test",
            NormalizedEmail = $"{name}@example.test".ToUpperInvariant(),
            FullName = $"Cuenta {name}",
            Role = role,
            CreatedAtUtc = clock.UtcNow,
            UpdatedAtUtc = clock.UtcNow
        };
        PasswordCredentials.Set(user, "clave-de-prueba-segura");
        return user;
    }

    private static async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return connection;
    }

    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now += delta;
    }
}
