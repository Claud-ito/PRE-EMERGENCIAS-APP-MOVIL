using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Identity;

namespace Univalle.Emergencias.Api.Identity;

/// <summary>Comandos explícitos de consola; nunca exponen un endpoint de creación sin sesión.</summary>
public static class AccountBootstrapper
{
    public static async Task RunAsync(WebApplication app, string[] args)
    {
        var seedDevelopment = args.Contains("--seed-development-users", StringComparer.Ordinal);
        if (seedDevelopment && !app.Environment.IsDevelopment())
            throw new InvalidOperationException("Las cuentas de ensayo solo se crean en Development.");

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IServerClock>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditTrail>();
        if (seedDevelopment)
        {
            var password = app.Configuration["DevelopmentAccounts:Password"];
            EnsurePassword(password);
            var accounts = new (string Username, string Email, string FullName, UserRole Role)[]
            {
                ("testigo.prueba", "testigo@example.test", "Testigo de prueba", UserRole.ComunidadTestigo),
                ("siso.prueba", "siso@example.test", "SySO de prueba", UserRole.SySO),
                ("mantenimiento.prueba", "mantenimiento@example.test", "Mantenimiento de prueba", UserRole.Mantenimiento),
                ("lider.prueba", "lider@example.test", "Líder de prueba", UserRole.LiderBrigada),
                ("brigadista.prueba", "brigadista@example.test", "Brigadista de prueba", UserRole.Brigadista),
                ("medico.prueba", "medico@example.test", "Médico de prueba", UserRole.Medico)
            };
            foreach (var account in accounts)
            {
                if (await db.Users.AnyAsync(x => x.NormalizedUsername == account.Username.ToUpperInvariant()))
                    continue;
                await AddAsync(db, audit, clock, account.Username, account.Email,
                    account.FullName, account.Role, password!);
                Console.WriteLine($"Cuenta de ensayo lista: {account.Username} ({account.Role})");
            }
            return;
        }

        if (await db.Users.AnyAsync())
            throw new InvalidOperationException("El bootstrap inicial exige una base sin cuentas.");
        var username = app.Configuration["Bootstrap:Username"]?.Trim();
        var email = app.Configuration["Bootstrap:Email"]?.Trim();
        var fullName = app.Configuration["Bootstrap:FullName"]?.Trim();
        var initialPassword = app.Configuration["Bootstrap:Password"];
        EnsurePassword(initialPassword);
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(fullName))
            throw new InvalidOperationException(
                "Configura Bootstrap__Username, Bootstrap__Email y Bootstrap__FullName.");
        await AddAsync(db, audit, clock, username, email, fullName, UserRole.SySO, initialPassword!);
        Console.WriteLine($"Cuenta SySO inicial creada: {username}");
    }

    private static void EnsurePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length is < 12 or > 128)
            throw new InvalidOperationException(
                "Configura una contraseña de 12 a 128 caracteres en una variable de entorno.");
    }

    private static async Task AddAsync(
        AppDbContext db, IAuditTrail audit, IServerClock clock,
        string username, string email, string fullName, UserRole role, string password)
    {
        var now = clock.UtcNow.ToUniversalTime();
        var user = new UserAccount
        {
            UserId = Guid.CreateVersion7(now),
            Username = username,
            NormalizedUsername = username.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FullName = fullName,
            Role = role,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        PasswordCredentials.Set(user, password);
        db.Users.Add(user);
        await audit.RecordAsync(new AuditWriteRequest(
            null, AuditActorKind.System, "user.bootstrap", "UserAccount", user.UserId));
    }
}
