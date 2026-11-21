using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Diana;
using Univalle.Emergencias.Api.Identity;
using Univalle.Emergencias.Api.Incidents;
using Univalle.Emergencias.Api.Services;
using Univalle.Emergencias.Api.Suggestions;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Incidents;
using Univalle.Emergencias.Contracts.Suggestions;
using Xunit;

namespace Univalle.Emergencias.Tests;

public sealed class SuggestionTests
{
    [Fact]
    public void WindowOpensAtClosureAndClosesAtExactlySixHours()
    {
        var closed = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        Assert.False(SuggestionWindow.CanWrite(closed, closed.AddTicks(-1)));
        Assert.True(SuggestionWindow.CanWrite(closed, closed));
        Assert.True(SuggestionWindow.CanWrite(closed, closed.AddHours(6).AddTicks(-1)));
        Assert.False(SuggestionWindow.CanWrite(closed, closed.AddHours(6)));
        Assert.False(SuggestionWindow.CanWrite(closed, closed.AddHours(6).AddTicks(1)));
    }

    [Fact]
    public async Task AnyActiveAccountCanWriteThenEveryoneCanReadAfterDeadline()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var closed = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        var provider = new ManualTimeProvider(closed.AddHours(5));
        var clock = new SystemServerClock(provider);
        var authorId = Guid.NewGuid();
        var incidentId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var floorId = Guid.NewGuid();
        var author = new UserAccount
        {
            UserId = authorId,
            Username = "testigo",
            NormalizedUsername = "TESTIGO",
            Email = "testigo@example.test",
            NormalizedEmail = "TESTIGO@EXAMPLE.TEST",
            FullName = "Persona de prueba",
            Role = UserRole.ComunidadTestigo,
            CreatedAtUtc = closed,
            UpdatedAtUtc = closed
        };
        PasswordCredentials.Set(author, "clave-de-prueba-segura");
        db.Users.Add(author);
        db.Set<LocationModule>().Add(new LocationModule
        {
            ModuleId = moduleId, Code = "M1", Name = "Módulo Uno"
        });
        db.Set<LocationFloor>().Add(new LocationFloor
        {
            FloorId = floorId, ModuleId = moduleId, Name = "Planta baja"
        });
        db.Incidents.Add(new Incident
        {
            IncidentId = incidentId,
            ModuleId = moduleId,
            FloorId = floorId,
            Type = IncidentType.Incendio,
            Status = IncidentStatuses.Closed,
            OpenedAtUtc = closed.AddHours(-2),
            ClosedAtUtc = closed,
            InitialAlertEventId = Guid.NewGuid(),
            InitialAlertState = InitialAlertStates.NoCoverage,
            ConcurrencyStamp = Guid.NewGuid()
        });
        await db.SaveChangesAsync();

        var reader = new EfIncidentReader(db);
        var incident = await reader.GetByIdAsync(incidentId);
        Assert.NotNull(incident);
        var service = new SuggestionService(
            db, new LocationCatalog(db), clock, new EfAuditTrail(db, clock));
        var optionsList = await service.ListClosedAsync(CancellationToken.None);
        Assert.Equal("Módulo Uno", Assert.Single(optionsList).ModuleName);

        var written = await service.WriteAsync(
            incident, authorId, "  Avisar al personal de recepción.  ".Trim(), CancellationToken.None);
        Assert.Null(written.ErrorCode);
        Assert.Equal(provider.GetUtcNow(), written.Value?.CreatedAtUtc);
        Assert.Equal(authorId, written.Value?.AuthorUserId);

        provider.Set(closed.AddHours(6));
        var rejected = await service.WriteAsync(
            incident, authorId, "Fuera de plazo", CancellationToken.None);
        Assert.Equal(SuggestionErrorCodes.WindowClosed, rejected.ErrorCode);
        var thread = await service.ReadAsync(incident, CancellationToken.None);
        Assert.False(thread.CanWrite);
        Assert.Equal(closed.AddHours(6), thread.WriteUntilUtc);
        Assert.Equal("Persona de prueba", Assert.Single(thread.Suggestions).AuthorName);
        Assert.Single(await db.IncidentSuggestions.ToListAsync());
        Assert.Single(await db.AuditEntries.Where(x => x.Action == "suggestion.create").ToListAsync());

        author.IsActive = false;
        await db.SaveChangesAsync();
        provider.Set(closed.AddHours(1));
        Assert.Equal("unauthenticated", (await service.WriteAsync(
            incident, authorId, "No debe entrar", CancellationToken.None)).ErrorCode);
    }

    private sealed class ManualTimeProvider(DateTimeOffset initial) : TimeProvider
    {
        private DateTimeOffset _now = initial;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Set(DateTimeOffset instant) => _now = instant;
    }
}
