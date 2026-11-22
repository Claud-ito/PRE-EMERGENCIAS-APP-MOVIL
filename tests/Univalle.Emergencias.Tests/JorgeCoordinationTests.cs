using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Univalle.Emergencias.Api.Coordination;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Identity;
using Univalle.Emergencias.Api.Incidents;
using Univalle.Emergencias.Api.Incidents.Development;
using Univalle.Emergencias.Api.Models;
using Univalle.Emergencias.Api.Notifications;
using Univalle.Emergencias.Api.Services;
using Univalle.Emergencias.Contracts.Brigades;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Notifications;
using Univalle.Emergencias.Contracts.Turns;
using Univalle.Emergencias.Tests.Incidents;
using Xunit;
using Xunit.Abstractions;

namespace Univalle.Emergencias.Tests;

public sealed class JorgeCoordinationTests(ITestOutputHelper output)
{
    private readonly Guid coordinator = Guid.NewGuid();
    private readonly Guid leader = Guid.NewGuid();
    private readonly BrigadeClock clock = new();

    private sealed class Lookup(Guid id) : IBrigadeLeaderLookup
    {
        public Task<ModuleLeader?> GetCurrentLeaderAsync(Guid module, DateTimeOffset instant, CancellationToken ct) =>
            Task.FromResult<ModuleLeader?>(new(id, Guid.NewGuid(), module, []));
    }

    private sealed class Identity(Guid id) : IIdentityContext
    {
        public Task<AuthenticatedUser?> GetCurrentAsync(CancellationToken ct) =>
            Task.FromResult<AuthenticatedUser?>(new(id, true, UserRole.SySO, RolePermissions.For(UserRole.SySO)));
    }

    private CalloutService Calls(AppDbContext db) => new(db, new Lookup(leader),
        new PersistentNotificationSender(db, clock), clock, new EfAuditTrail(db, clock));

    private CoordinationController Controller(AppDbContext db, Guid actor)
    {
        var controller = new CoordinationController(db, Calls(db), clock,
            new EfAuditTrail(db, clock)) {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.HttpContext.Items[RequirePermissionAttribute.ActorKey] =
            new AuthenticatedUser(actor, true, UserRole.SySO, RolePermissions.For(UserRole.SySO));
        return controller;
    }

    [Theory]
    [InlineData(true, true, false, true)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    public async Task ReportToInitialAlertToCallout(bool enabled, bool covered, bool manual, bool expectAuto)
    {
        await using var database = await SqliteIncidentDatabase.CreateAsync();
        Guid incidentId;
        await using (var db = database.NewContext())
        {
            await Controller(db, coordinator).Configure(new(enabled), default);
            var audit = new EfAuditTrail(db, clock);
            var alerts = new InitialAlertDispatcher(db, new PersistentNotificationSender(db, clock),
                clock, audit, NullLogger<InitialAlertDispatcher>.Instance);
            var report = new IncidentReportService(db, clock, new DevLocationCatalog(),
                new FixedTurnResolver(covered ? coordinator : null), audit, alerts, new RaceCountingLogger());
            var request = IncidentHarness.Request(DevLocationCatalog.ModuleA, DevLocationCatalog.ModuleAFloor0, Guid.NewGuid());
            var result = Assert.IsType<SubmitReportResult.Accepted>(await report.SubmitAsync(Guid.NewGuid(), request));
            incidentId = result.Response.IncidentId;
            clock.Now += TimeSpan.FromSeconds(119);
            await report.SubmitAsync(Guid.NewGuid(), request with { ClientRequestId = Guid.NewGuid() });
            Assert.False(await Calls(db).RegisterAsync(incidentId, null, default));
            var initial = await db.NotificationEntries.AsNoTracking().SingleOrDefaultAsync();
            Assert.Equal(covered, initial is not null);
            if (initial is not null)
            {
                Assert.Equal("Pending", initial.DeliveryStatus);
                var inbox = new NotificationsController(db, new Identity(coordinator), clock);
                await inbox.MarkAsRead(initial.EventId, default);
            }
            if (manual) Assert.True(await Calls(db).RegisterAsync(incidentId, coordinator, default));
        }
        // A new scope reconstructs the pending deadline from storage.
        clock.Now += TimeSpan.FromSeconds(1);
        await using var recovered = database.NewContext();
        Assert.Equal(expectAuto, await Calls(recovered).RegisterAsync(incidentId, null, default));
        Assert.False(await Calls(recovered).RegisterAsync(incidentId, null, default));
        Assert.Equal(covered ? coordinator : (Guid?)null, (await recovered.Incidents.SingleAsync()).CurrentCoordinatorUserId);
        var call = await recovered.Callouts.SingleOrDefaultAsync();
        Assert.Equal(manual || expectAuto, call != null);
        if (call != null)
        {
            Assert.Equal(leader, call.RecipientUserId);
            Assert.Equal(manual ? coordinator : (Guid?)null, call.SenderUserId);
            output.WriteLine($"{call.Channel} {call.SenderUserId?.ToString() ?? "Sistema"} -> {call.RecipientUserId} {call.OccurredAtUtc:O}");
        }
        Assert.Equal(expectAuto ? 1 : 0, await recovered.NotificationEntries.CountAsync(x => x.Type == "incident.auto"));
        Assert.Equal(manual || expectAuto ? 1 : 0,
            await recovered.AuditEntries.CountAsync(x => x.Action.StartsWith("incident.callout.")));
    }

    [Fact]
    public async Task RetriesKeepIdentityRecipientAndFirstAcknowledgements()
    {
        await using var database = await SqliteIncidentDatabase.CreateAsync();
        var request = new NotificationRequest(Guid.NewGuid(), Guid.NewGuid(), "incident.initial-alert", coordinator);
        await using (var db = database.NewContext())
        {
            var sender = new PersistentNotificationSender(db, clock);
            var original = await sender.RegisterAndSendAsync(request);
            clock.Now += TimeSpan.FromSeconds(1);
            Assert.Equal(original, await sender.RegisterAndSendAsync(request));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                sender.RegisterAndSendAsync(request with { RecipientUserId = leader }));
            var other = new NotificationsController(db, new Identity(leader), clock);
            Assert.IsType<NotFoundResult>(await other.ConfirmDelivery(request.EventId, default));
            Assert.IsType<NotFoundResult>(await other.MarkAsRead(request.EventId, default));
        }
        await using var next = database.NewContext();
        var mine = new NotificationsController(next, new Identity(coordinator), clock);
        var delivered = clock.Now;
        Assert.IsType<NoContentResult>(await mine.ConfirmDelivery(request.EventId, default));
        clock.Now += TimeSpan.FromSeconds(1);
        await mine.ConfirmDelivery(request.EventId, default);
        await mine.MarkAsRead(request.EventId, default);
        var read = clock.Now;
        clock.Now += TimeSpan.FromHours(1);
        await mine.MarkAsRead(request.EventId, default);
        var item = await next.NotificationEntries.AsNoTracking().SingleAsync();
        Assert.Equal(delivered, item.DeliveredAtUtc);
        Assert.Equal(read, item.ReadAtUtc);
        Assert.Equal("Delivered", item.DeliveryStatus);
    }

    [Fact]
    public async Task OnlyRecipientAcceptsAndCalendarEditsDoNotTransferCommand()
    {
        await using var database = await SqliteIncidentDatabase.CreateAsync();
        await using var db = database.NewContext();
        var next = Guid.NewGuid();
        db.Users.Add(new UserAccount { UserId = next, Role = UserRole.Mantenimiento });
        var incident = new Incident { IncidentId = Guid.NewGuid(), CurrentCoordinatorUserId = coordinator };
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();
        var schedules = new ScheduleManagementService(db, new Identity(coordinator),
            new EfAuditTrail(db, clock));
        var shift = await schedules.CreateScheduleAsync(new(DayOfWeek.Monday, new(8, 0), new(16, 0)));
        await schedules.UpdateScheduleAsync(shift.WorkScheduleId, new(DayOfWeek.Monday, new(9, 0), new(17, 0)));
        await schedules.DeactivateScheduleAsync(shift.WorkScheduleId);
        Assert.Equal(coordinator, incident.CurrentCoordinatorUserId);
        Assert.IsType<NoContentResult>(await Controller(db, coordinator).Propose(incident.IncidentId, new(next), default));
        var handoff = await db.Handoffs.SingleAsync();
        Assert.Equal(coordinator, incident.CurrentCoordinatorUserId);
        Assert.IsType<NotFoundResult>(await Controller(db, leader).Accept(handoff.Id, default));
        Assert.IsType<NoContentResult>(await Controller(db, next).Accept(handoff.Id, default));
        Assert.Equal(next, (await db.Incidents.AsNoTracking().SingleAsync()).CurrentCoordinatorUserId);
        Assert.NotNull(handoff.AcceptedAtUtc);
        Assert.Contains(db.AuditEntries, x => x.Action == "schedule.create" && x.ActorUserId == coordinator);
        Assert.Contains(db.AuditEntries, x => x.Action == "schedule.update" && x.ActorUserId == coordinator);
        Assert.Contains(db.AuditEntries, x => x.Action == "schedule.deactivate" && x.ActorUserId == coordinator);
        Assert.Contains(db.AuditEntries, x => x.Action == "coordination.handoff.propose" && x.IncidentId == incident.IncidentId);
        Assert.Contains(db.AuditEntries, x => x.Action == "coordination.handoff.accept" && x.ActorUserId == next);
    }

    [Fact]
    public async Task TurningAutoOffCancelsPendingDeadlineEvenAfterEnablingAgain()
    {
        await using var database = await SqliteIncidentDatabase.CreateAsync();
        await using var db = database.NewContext();
        var incident = new Incident { IncidentId = Guid.NewGuid(), CurrentCoordinatorUserId = coordinator,
            InitialAlertRecipientUserId = coordinator, AutoDueAtUtc = clock.Now.UtcDateTime.AddSeconds(120) };
        db.Incidents.Add(incident);
        await db.SaveChangesAsync();
        var api = Controller(db, coordinator);
        await api.Configure(new(false), default);
        await api.Configure(new(true), default);
        clock.Now += TimeSpan.FromMinutes(5);
        Assert.False(await Calls(db).RegisterAsync(incident.IncidentId, null, default));
        Assert.Empty(await db.Callouts.ToListAsync());
        Assert.Contains(db.AuditEntries, x => x.Action == "coordination.auto.disable" && x.ActorUserId == coordinator);
        Assert.Contains(db.AuditEntries, x => x.Action == "coordination.auto.enable" && x.ActorUserId == coordinator);
    }

    [Fact]
    public async Task ExceptionAppearsOnceAndRejectsOverlapWithAnotherCoordinator()
    {
        await using var database = await SqliteIncidentDatabase.CreateAsync();
        await using var db = database.NewContext();
        var manager = new ScheduleManagementService(db, new Identity(coordinator),
            new EfAuditTrail(db, clock));
        var monday = new DateOnly(2026, 11, 23);
        await manager.CreateScheduleAsync(new(DayOfWeek.Monday, new(8, 0), new(12, 0)));
        await manager.CreateScheduleAsync(new(DayOfWeek.Wednesday, new(8, 0), new(12, 0)));
        var special = await manager.CreateExceptionAsync(monday, true, new(12, 0), new(14, 0), null);
        var day = await manager.CalendarAsync(monday, default);
        Assert.Equal(3, day.Count);
        Assert.Equal(coordinator, day[1].CoordinatorUserId);
        Assert.Null(day[0].CoordinatorUserId);
        Assert.Null(day[2].CoordinatorUserId);
        var other = new ScheduleManagementService(db, new Identity(Guid.NewGuid()),
            new EfAuditTrail(db, clock));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            other.CreateExceptionAsync(monday, true, new(13, 0), new(15, 0), null));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => other.RemoveExceptionAsync(special.ScheduleExceptionId, default));
        await manager.CreateExceptionAsync(monday, false, null, null, "Ensayo", default, special.ScheduleExceptionId);
        Assert.Null(Assert.Single(await manager.CalendarAsync(monday, default)).CoordinatorUserId);
        await manager.RemoveExceptionAsync(special.ScheduleExceptionId, default);
        Assert.Equal(coordinator, (await manager.CalendarAsync(monday, default))[1].CoordinatorUserId);
        Assert.Contains(db.AuditEntries, x => x.Action == "schedule.exception.create" && x.ActorUserId == coordinator);
        Assert.Contains(db.AuditEntries, x => x.Action == "schedule.exception.update" && x.ActorUserId == coordinator);
        Assert.Contains(db.AuditEntries, x => x.Action == "schedule.exception.deactivate" && x.ActorUserId == coordinator);
    }

    [SqlServerFact]
    public async Task ConcurrentManualAndAutoReserveOneCalloutOnSqlServer()
    {
        var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvironmentVariable))
            { InitialCatalog = "JorgeRaceTest_" + Guid.NewGuid().ToString("N") };
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection.ConnectionString).Options;
        await using var db = new AppDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
            var incident = new Incident { IncidentId = Guid.NewGuid(), CurrentCoordinatorUserId = coordinator,
                InitialAlertRecipientUserId = coordinator, AutoDueAtUtc = clock.Now.UtcDateTime };
            db.Add(incident);
            await db.SaveChangesAsync();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<bool> Attempt(Guid? actor)
            {
                await using var fresh = new AppDbContext(options);
                await gate.Task;
                return await Calls(fresh).RegisterAsync(incident.IncidentId, actor, default);
            }
            var attempts = new[] { Attempt(coordinator), Attempt(null) };
            gate.SetResult();
            Assert.Single(await Task.WhenAll(attempts), x => x);
            var call = await db.Callouts.SingleAsync();
            Assert.Equal(call.Channel == "AUTO" ? 1 : 0, await db.NotificationEntries.CountAsync());

            var notice = new NotificationRequest(Guid.NewGuid(), incident.IncidentId, "incident.initial-alert", coordinator);
            async Task<NotificationResult> RetryNotice()
            {
                await using var fresh = new AppDbContext(options);
                return await new PersistentNotificationSender(fresh, clock).RegisterAndSendAsync(notice);
            }
            var receipts = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => RetryNotice()));
            Assert.Single(receipts.Distinct());
            Assert.Equal(1, await db.NotificationEntries.CountAsync(x => x.EventId == notice.EventId));
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }
}
