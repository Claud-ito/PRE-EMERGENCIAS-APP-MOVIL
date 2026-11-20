using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Diana;
using Univalle.Emergencias.Api.Incidents;
using Univalle.Emergencias.Api.Services;
using Univalle.Emergencias.Contracts.Diana;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Incidents;
using Univalle.Emergencias.Contracts.Locations;
using Univalle.Emergencias.Tests.Incidents;
using Xunit;

namespace Univalle.Emergencias.Tests;

public sealed class DianaIncidentIntegrationTests
{
    [Fact]
    public async Task RealCatalogAndIncidentReaderSupportZeroAndMultipleInjuryRecords()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var module = new LocationModule { ModuleId = Guid.NewGuid(), Code = "INTEGRACION", Name = "Módulo de ensayo" };
        var floor = new LocationFloor { FloorId = Guid.NewGuid(), ModuleId = module.ModuleId, Name = "Piso de ensayo" };
        db.AddRange(module, floor); await db.SaveChangesAsync();
        var clock = new SystemServerClock(new ManualClock(IncidentHarness.Start));
        var audit = new EfAuditTrail(db, clock);
        var locations = new LocationCatalog(db);
        var sender = new RecordingNotificationSender();
        var alerts = new InitialAlertDispatcher(db, sender, clock, audit, NullLogger<InitialAlertDispatcher>.Instance);
        var reports = new IncidentReportService(db, clock, locations, new FixedTurnResolver(null), audit, alerts, NullLogger<IncidentReportService>.Instance);
        var reporter = Guid.NewGuid();
        var first = Assert.IsType<SubmitReportResult.Accepted>(await reports.SubmitAsync(reporter,
            IncidentHarness.Request(module.ModuleId, floor.FloorId, Guid.NewGuid()))).Response;
        var second = Assert.IsType<SubmitReportResult.Accepted>(await reports.SubmitAsync(Guid.NewGuid(),
            IncidentHarness.Request(module.ModuleId, floor.FloorId, Guid.NewGuid()))).Response;
        Assert.Equal(first.IncidentId, second.IncidentId);
        Assert.Single(await db.Incidents.ToListAsync());
        Assert.Equal(2, await db.IncidentReports.CountAsync());
        var reader = new EfIncidentReader(db);
        Assert.NotNull(await reader.GetByIdAsync(first.IncidentId));
        var physician = Guid.NewGuid();
        using var services = new ServiceCollection().AddSingleton<IIncidentReader>(reader).BuildServiceProvider();
        var controller = new InjuryRecordsController(db, audit, clock) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services } }
        };
        controller.HttpContext.Items[DianaAccessAttribute.ActorKey] = new AuthenticatedUser(physician, true, UserRole.Medico, new HashSet<string> { PermissionCodes.RecordInjury });
        var initial = Assert.IsType<OkObjectResult>(await controller.List(first.IncidentId, default));
        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<InjuryRecordView>>(initial.Value));
        Assert.IsType<OkObjectResult>(await controller.Create(first.IncidentId, new(true), default));
        Assert.IsType<OkObjectResult>(await controller.Create(first.IncidentId, new(true), default));
        var list = Assert.IsType<OkObjectResult>(await controller.List(first.IncidentId, default));
        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<InjuryRecordView>>(list.Value).Count());
        Assert.All(await db.Set<InjuryRecord>().ToListAsync(), item => {
            Assert.Equal(first.IncidentId, item.IncidentId); Assert.Equal(physician, item.PhysicianUserId);
            Assert.Equal(clock.UtcNow, item.CreatedAtUtc);
        });
        var choices = Assert.IsType<OkObjectResult>(await new InjuryIncidentsController(db).List(default));
        var choice = Assert.Single(Assert.IsAssignableFrom<IEnumerable<InjuryIncidentView>>(choices.Value));
        Assert.Equal(module.Name, choice.ModuleName); Assert.Equal(floor.Name, choice.FloorName);
        module.IsActive = false; await db.SaveChangesAsync();
        Assert.IsType<SubmitReportResult.LocationUnavailable>(await reports.SubmitAsync(reporter,
            IncidentHarness.Request(module.ModuleId, floor.FloorId, Guid.NewGuid())));
        Assert.Equal(2, await db.Set<InjuryRecord>().CountAsync());
        Assert.NotNull(await locations.GetModuleAsync(module.ModuleId));
    }

    [Fact]
    public async Task InjuryForeignKeyRejectsAnIncidentThatDoesNotExist()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Add(new InjuryRecord { InjuryRecordId = Guid.NewGuid(), IncidentId = Guid.NewGuid(), PhysicianUserId = Guid.NewGuid(), CreatedAtUtc = IncidentHarness.Start });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public void RealLocationsTakePrecedenceOverAlejandrosDevelopmentSubstitute()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer("Server=localhost;Database=NotUsed;Integrated Security=true"));
        services.AddDianaModules();
        services.AddIncidentModule();
        services.AddIncidentDevelopmentSubstitutes();
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        Assert.IsType<LocationCatalog>(scope.ServiceProvider.GetRequiredService<ILocationCatalog>());
        Assert.Single(scope.ServiceProvider.GetServices<ILocationCatalog>());
        Assert.IsType<EfIncidentReader>(scope.ServiceProvider.GetRequiredService<IIncidentReader>());
    }
}
