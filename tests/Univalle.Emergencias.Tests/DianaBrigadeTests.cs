using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Diana;
using Univalle.Emergencias.Api.Services;
using Univalle.Emergencias.Contracts.Brigades;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Diana;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Tests.Incidents;
using Xunit;

namespace Univalle.Emergencias.Tests;

public sealed class DianaBrigadeTests
{
    [Fact]
    public async Task OneBrigadeCoversSeveralModulesAndModuleQueryShowsItsLeader()
    {
        await using var f = await BrigadeFixture.SqliteAsync();
        var view = Assert.IsType<BrigadeView>(Assert.IsType<OkObjectResult>(await f.Controller.Create(f.Input("Brigada de ensayo"), default)).Value);
        Assert.Equal(2, view.ModuleIds.Count);
        foreach (var module in f.ModuleIds) {
            var leader = await f.Lookup.GetCurrentLeaderAsync(module, f.Clock.UtcNow);
            Assert.NotNull(leader); Assert.Equal(f.Users.Accounts[0].UserId, leader.UserId);
            Assert.Equal(3, leader.Members.Count);
            var status = Assert.IsType<ModuleBrigadeStatus>(Assert.IsType<OkObjectResult>(await f.Controller.ModuleLeader(module, default)).Value);
            Assert.Equal("Brigada de ensayo", status.Leader!.BrigadeName);
            Assert.Equal("Líder de ensayo A", status.Leader.LeaderName);
            Assert.Equal(3, status.Leader.Members.Count);
        }
        Assert.Single(await f.Db.AuditEntries.ToListAsync());
    }

    [Fact]
    public async Task ChangingLeaderAndModuleKeepsPreviousAssignmentAndDeactivationKeepsHistory()
    {
        await using var f = await BrigadeFixture.SqliteAsync();
        var initial = Assert.IsType<BrigadeView>(Assert.IsType<OkObjectResult>(await f.Controller.Create(f.Input("Brigada histórica"), default)).Value);
        var initialTime = f.Clock.UtcNow;
        f.Clock.Now += TimeSpan.FromMinutes(1);
        var changed = f.Input("Brigada histórica") with { LeaderUserId = f.Users.Accounts[1].UserId, ModuleIds = [f.ModuleIds[0]] };
        Assert.IsType<OkObjectResult>(await f.Controller.Update(initial.BrigadeId, changed, default));
        Assert.Equal(f.Users.Accounts[0].UserId, (await f.Lookup.GetCurrentLeaderAsync(f.ModuleIds[0], initialTime))!.UserId);
        Assert.Equal(f.Users.Accounts[1].UserId, (await f.Lookup.GetCurrentLeaderAsync(f.ModuleIds[0], f.Clock.UtcNow))!.UserId);
        Assert.Null(await f.Lookup.GetCurrentLeaderAsync(f.ModuleIds[1], f.Clock.UtcNow));
        f.Clock.Now += TimeSpan.FromMinutes(1);
        Assert.IsType<NoContentResult>(await f.Controller.Deactivate(initial.BrigadeId, default));
        Assert.Null(await f.Lookup.GetCurrentLeaderAsync(f.ModuleIds[0], f.Clock.UtcNow));
        Assert.NotNull(await f.Lookup.GetCurrentLeaderAsync(f.ModuleIds[0], initialTime));
        Assert.Equal(3, await f.Db.Set<BrigadeModuleAssignment>().CountAsync());
        Assert.Equal(3, await f.Db.Set<BrigadeMembership>().CountAsync());
        Assert.False((await f.Db.Set<Brigade>().SingleAsync()).IsActive);
        Assert.Equal(3, await f.Db.AuditEntries.CountAsync());
    }

    [Fact]
    public async Task InvalidSpecialtyNullMemberInactiveAccountAndDuplicateModuleAreRejected()
    {
        await using var f = await BrigadeFixture.SqliteAsync();
        var input = f.Input("Brigada de ensayo");
        var first = Assert.IsType<BrigadeView>(Assert.IsType<OkObjectResult>(await f.Controller.Create(input, default)).Value);
        Assert.Equal(409, Assert.IsType<ObjectResult>(await f.Controller.Create(input with { Name = "Otra brigada" }, default)).StatusCode);
        var badSpecialty = input with { Members = [new(input.LeaderUserId, "Especialidad inexistente")] };
        Assert.Equal(400, Assert.IsType<ObjectResult>(await f.Controller.Update(first.BrigadeId, badSpecialty, default)).StatusCode);
        Assert.Equal(400, Assert.IsType<ObjectResult>(await f.Controller.Create(input with { Members = [null!] }, default)).StatusCode);
        f.Users.Accounts[0] = f.Users.Accounts[0] with { IsActive = false };
        Assert.Equal(400, Assert.IsType<ObjectResult>(await f.Controller.Update(first.BrigadeId, input, default)).StatusCode);
        Assert.Null(await f.Lookup.GetCurrentLeaderAsync(f.ModuleIds[0], f.Clock.UtcNow));
        Assert.Single(await f.Db.Set<Brigade>().ToListAsync());
        Assert.Equal(2, await f.Db.Set<BrigadeModuleAssignment>().CountAsync());
    }
}

// El almacenamiento UTC entero es solo de la prueba: SQLite no compara DateTimeOffset nativamente.
// La consulta probada sigue siendo la misma de producción; SQL Server usa datetimeoffset sin conversión.
public sealed class BrigadeSqliteModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder builder, DbContext context) {
        base.Customize(builder, context);
        foreach (var property in builder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
            if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) == typeof(DateTimeOffset))
                property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
    }
}

internal sealed class BrigadeFixture : IAsyncDisposable
{
    private readonly SqliteConnection? connection;
    private readonly ServiceProvider services;
    public AppDbContext Db { get; }
    public BrigadeClock Clock { get; } = new();
    public BrigadeUsers Users { get; } = new();
    public Guid[] ModuleIds { get; } = [Guid.NewGuid(), Guid.NewGuid()];
    public BrigadesController Controller { get; }
    public IBrigadeLeaderLookup Lookup => services.GetRequiredService<IBrigadeLeaderLookup>();
    public BrigadeFixture(DbContextOptions<AppDbContext> options, SqliteConnection? connection = null) {
        this.connection = connection; Db = new(options);
        services = new ServiceCollection().AddSingleton<IBrigadeUserDirectory>(Users)
            .AddScoped<IBrigadeLeaderLookup>(sp => new BrigadeLeaderLookup(Db, sp)).BuildServiceProvider();
        var settings = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Diana:BrigadeSpecialties:0"] = "Primeros auxilios", ["Diana:BrigadeSpecialties:1"] = "Evacuación",
            ["Diana:BrigadeSpecialties:2"] = "Extinción de fuegos"
        }).Build();
        Controller = new(Db, new EfAuditTrail(Db, Clock), Clock, settings) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } }
        };
        Controller.HttpContext.Items[DianaAccessAttribute.ActorKey] = new AuthenticatedUser(Guid.NewGuid(), true,
            UserRole.SySO, new HashSet<string> { PermissionCodes.ManageBrigades });
    }
    public static async Task<BrigadeFixture> SqliteAsync() {
        var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var f = new BrigadeFixture(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection)
            .ReplaceService<IModelCustomizer, BrigadeSqliteModelCustomizer>().Options, connection);
        await f.Db.Database.EnsureCreatedAsync(); await f.SeedModulesAsync(); return f;
    }
    public async Task SeedModulesAsync() {
        Db.AddRange(new LocationModule { ModuleId = ModuleIds[0], Code = "ENSAYO-A", Name = "Módulo de ensayo A" },
            new LocationModule { ModuleId = ModuleIds[1], Code = "ENSAYO-B", Name = "Módulo de ensayo B" });
        await Db.SaveChangesAsync();
    }
    public BrigadeInput Input(string name) => new(name, Users.Accounts[0].UserId,
        [new(Users.Accounts[0].UserId, "Primeros auxilios"), new(Users.Accounts[1].UserId, "Evacuación"),
         new(Users.Accounts[2].UserId, "Extinción de fuegos")], ModuleIds);
    public async ValueTask DisposeAsync() { await services.DisposeAsync(); await Db.DisposeAsync(); if (connection is not null) await connection.DisposeAsync(); }
}
internal sealed class BrigadeClock : IServerClock {
    public DateTimeOffset Now { get; set; } = new(2026, 11, 20, 21, 0, 0, TimeSpan.Zero);
    public DateTimeOffset UtcNow => Now;
}
internal sealed class BrigadeUsers : IBrigadeUserDirectory {
    public List<BrigadeUser> Accounts { get; } = [
        new(Guid.NewGuid(), "Líder de ensayo A", UserRole.LiderBrigada, true),
        new(Guid.NewGuid(), "Líder de ensayo B", UserRole.LiderBrigada, true),
        new(Guid.NewGuid(), "Brigadista de ensayo", UserRole.Brigadista, true)];
    public Task<IReadOnlyList<BrigadeUser>> GetAssignableUsersAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<BrigadeUser>>(Accounts);
}

public sealed class DianaBrigadeSqlServerTests : IAsyncLifetime
{
    private DbContextOptions<AppDbContext>? options;
    private string? databaseName;
    public async Task InitializeAsync() {
        var source = Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(source)) return;
        databaseName = "DianaBrigadesTest_" + Guid.NewGuid().ToString("N");
        var connection = new SqlConnectionStringBuilder(source) { InitialCatalog = databaseName };
        options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection.ConnectionString).Options;
        await using var db = new AppDbContext(options); await db.Database.MigrateAsync();
    }
    public async Task DisposeAsync() {
        if (options is null || databaseName is null || !databaseName.StartsWith("DianaBrigadesTest_", StringComparison.Ordinal)) return;
        await using var db = new AppDbContext(options); await db.Database.EnsureDeletedAsync();
    }
    [SqlServerFact]
    public async Task MigratedSqlServerPreservesModulesLeadersAndHistory() {
        await using var f = new BrigadeFixture(options!); await f.SeedModulesAsync();
        var result = Assert.IsType<BrigadeView>(Assert.IsType<OkObjectResult>(await f.Controller.Create(f.Input("Brigada SQL de ensayo"), default)).Value);
        foreach (var module in f.ModuleIds) Assert.NotNull(await f.Lookup.GetCurrentLeaderAsync(module, f.Clock.UtcNow));
        f.Clock.Now += TimeSpan.FromMinutes(1);
        Assert.IsType<NoContentResult>(await f.Controller.Deactivate(result.BrigadeId, default));
        Assert.Null(await f.Lookup.GetCurrentLeaderAsync(f.ModuleIds[0], f.Clock.UtcNow));
        Assert.Equal(2, await f.Db.Set<BrigadeModuleAssignment>().CountAsync());
        Assert.Equal(2, await f.Db.AuditEntries.CountAsync());
    }
}
