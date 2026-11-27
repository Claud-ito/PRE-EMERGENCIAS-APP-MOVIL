using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Diana;
using Univalle.Emergencias.Api.Services;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Diana;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Incidents;
using Xunit;

namespace Univalle.Emergencias.Tests;

public sealed class DianaPersistenceTests
{
    [Fact]
    public async Task DeactivatedModuleHidesFloorsButPreservesBothReferencesAndAudit() {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var clock = new Clock(); var module = new LocationModule { ModuleId = Guid.NewGuid(), Code = "M1", Name = "Módulo 1" };
        var floor = new LocationFloor { FloorId = Guid.NewGuid(), ModuleId = module.ModuleId, Name = "Piso 1" };
        db.AddRange(module, floor); await db.SaveChangesAsync();
        var catalog = new LocationCatalog(db);
        Assert.True(await catalog.IsActiveFloorInModuleAsync(module.ModuleId, floor.FloorId));
        Assert.False(await catalog.IsActiveFloorInModuleAsync(Guid.NewGuid(), floor.FloorId));
        var controller = new LocationsController(db, new EfAuditTrail(db, clock)); Attach(controller);
        Assert.IsType<NoContentResult>(await controller.DeactivateModule(module.ModuleId, default));
        Assert.Empty(await catalog.GetActiveModulesAsync()); Assert.Empty(await catalog.GetActiveFloorsAsync(module.ModuleId));
        Assert.False(await catalog.IsActiveFloorInModuleAsync(module.ModuleId, floor.FloorId));
        Assert.NotNull(await catalog.GetModuleAsync(module.ModuleId)); Assert.NotNull(await catalog.GetFloorAsync(floor.FloorId));
        Assert.Single(await db.AuditEntries.ToListAsync()); Assert.Single(await db.Set<LocationFloor>().ToListAsync());
    }
    [Fact]
    public async Task InactiveModuleAndFloorCanBeReactivatedInDependencyOrder() {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var module = new LocationModule { ModuleId = Guid.NewGuid(), Code = "M1", Name = "Módulo 1", IsActive = false };
        var floor = new LocationFloor { FloorId = Guid.NewGuid(), ModuleId = module.ModuleId, Name = "Piso 1", IsActive = false };
        db.AddRange(module, floor); await db.SaveChangesAsync();
        var controller = new LocationsController(db, new EfAuditTrail(db, new Clock())); Attach(controller);
        var catalog = new LocationCatalog(db);
        // Un piso no se reactiva dentro de un módulo inactivo.
        var blocked = Assert.IsType<ObjectResult>(await controller.ReactivateFloor(floor.FloorId, default));
        Assert.Equal(409, blocked.StatusCode);
        Assert.IsType<NoContentResult>(await controller.ReactivateModule(module.ModuleId, default));
        Assert.IsType<NoContentResult>(await controller.ReactivateFloor(floor.FloorId, default));
        Assert.True(await catalog.IsActiveFloorInModuleAsync(module.ModuleId, floor.FloorId));
        Assert.Equal(2, await db.AuditEntries.CountAsync(x => x.Action.EndsWith(".reactivate")));
    }
    [Fact]
    public async Task ContactSoftDeleteRetainsIdAndDataForHistoricalReferences() {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var contact = new SupportContact { ContactId = Guid.NewGuid(), Name = "Contacto de prueba", Phone = "123", Kind = SupportContactKind.Bomberos };
        db.Add(contact); await db.SaveChangesAsync();
        var controller = new SupportContactsController(db, new EfAuditTrail(db, new Clock())); Attach(controller);
        Assert.IsType<NoContentResult>(await controller.Deactivate(contact.ContactId, default));
        var saved = await db.Set<SupportContact>().SingleAsync(); Assert.False(saved.IsActive); Assert.Equal(contact.ContactId, saved.ContactId); Assert.Equal("123", saved.Phone);
        var active = Assert.IsType<OkObjectResult>(await controller.List()); Assert.Empty(Assert.IsAssignableFrom<IEnumerable<SupportContactView>>(active.Value));
        Assert.IsType<OkObjectResult>(await controller.Get(contact.ContactId, default));
    }
    [Fact]
    public async Task IncidentCanHaveZeroAndSeveralRecordsWithoutInventedClinicalFields() {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var clock = new Clock(); var incidents = new Incidents();
        db.Incidents.Add(new Univalle.Emergencias.Api.Incidents.Incident {
            IncidentId = incidents.Id, ModuleId = Guid.NewGuid(), FloorId = Guid.NewGuid(),
            OpenedAtUtc = clock.UtcNow, InitialAlertEventId = Guid.NewGuid(), ConcurrencyStamp = Guid.NewGuid()
        });
        await db.SaveChangesAsync();
        var controller = new InjuryRecordsController(db, new EfAuditTrail(db, clock), clock);
        Attach(controller, new ServiceCollection().AddSingleton<IIncidentReader>(incidents).BuildServiceProvider());
        Assert.Equal(0, await db.Set<InjuryRecord>().CountAsync());
        Assert.IsType<OkObjectResult>(await controller.Create(incidents.Id, new(true), default));
        Assert.IsType<OkObjectResult>(await controller.Create(incidents.Id, new(true), default));
        Assert.Equal(2, await db.Set<InjuryRecord>().CountAsync());
        Assert.Equal(2, await db.AuditEntries.CountAsync());
        Assert.All(await db.Set<InjuryRecord>().ToListAsync(), x => Assert.Equal(clock.UtcNow, x.CreatedAtUtc));
    }
    private static void Attach(ControllerBase controller, IServiceProvider? services = null) {
        var context = new DefaultHttpContext(); if (services is not null) context.RequestServices = services;
        context.Items[DianaAccessAttribute.ActorKey] = new AuthenticatedUser(Guid.NewGuid(), true, UserRole.Medico, new HashSet<string> { PermissionCodes.RecordInjury });
        controller.ControllerContext = new ControllerContext { HttpContext = context };
    }
    private sealed class Clock : IServerClock { public DateTimeOffset UtcNow => new(2026, 11, 20, 13, 0, 0, TimeSpan.Zero); }
    private sealed class Incidents : IIncidentReader {
        public Guid Id { get; } = Guid.NewGuid();
        public Task<IncidentSnapshot?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<IncidentSnapshot?>(id == Id ? new(Id, Guid.NewGuid(), Guid.NewGuid(), "Active", null, DateTimeOffset.UtcNow, Guid.NewGuid(), null) : null);
    }
}
