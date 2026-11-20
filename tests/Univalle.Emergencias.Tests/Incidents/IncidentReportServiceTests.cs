using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Incidents;
using Univalle.Emergencias.Api.Incidents.Development;
using Univalle.Emergencias.Contracts.Incidents;
using Xunit;

namespace Univalle.Emergencias.Tests.Incidents;

/// <summary>RF07 y RF08 sobre SQLite en memoria (reglas y persistencia).</summary>
public sealed class IncidentReportServiceTests
{
    private static readonly Guid ModuleA = DevLocationCatalog.ModuleA;
    private static readonly Guid FloorA0 = DevLocationCatalog.ModuleAFloor0;
    private static readonly Guid FloorA1 = DevLocationCatalog.ModuleAFloor1;

    [Fact]
    public async Task FirstReportOpensIncidentWithServerDataAndSendsOneInitialAlert()
    {
        await using var database = await SqliteIncidentDatabase.CreateAsync();
        var coordinator = Guid.NewGuid();
        var reporter = Guid.NewGuid();
        var harness = new IncidentHarness(database.NewContext, coordinator);

        var response = await harness.SubmitAsync(reporter, ModuleA, FloorA0, severity: PreliminarySeverity.Grave);

        Assert.True(response.IncidentCreated);
        Assert.Equal(IncidentHarness.Start, response.ReportedAtUtc);

        await using var db = database.NewContext();
        var incident = await db.Incidents.Include(i => i.Reports).SingleAsync();
        Assert.Equal(IncidentStatuses.Active, incident.Status);
        Assert.Equal(coordinator, incident.CurrentCoordinatorUserId);
        Assert.Equal(IncidentHarness.Start, incident.OpenedAtUtc);
        Assert.Equal(InitialAlertStates.Dispatched, incident.InitialAlertState);
        var report = Assert.Single(incident.Reports);
        Assert.Equal(reporter, report.ReporterUserId);
        Assert.Equal(PreliminarySeverity.Grave, report.PreliminarySeverity);
        Assert.True(report.IsInitial);

        var alert = Assert.Single(harness.Sender.Calls);
        Assert.Equal(incident.InitialAlertEventId, alert.EventId);
        Assert.Equal(coordinator, alert.RecipientUserId);
        Assert.Equal(IncidentNotificationTypes.InitialAlert, alert.Type);
    }

    [Fact]
    public async Task LaterReportFromSameLocationJoinsAsContributionWithoutNewAlert()
    {
        await using var database = await SqliteIncidentDatabase.CreateAsync();
        var coordinator = Guid.NewGuid();
        var harness = new IncidentHarness(database.NewContext, coordinator);

        var first = await harness.SubmitAsync(Guid.NewGuid(), ModuleA, FloorA0);
        harness.Clock.Advance(TimeSpan.FromSeconds(30));
        var second = await harness.SubmitAsync(Guid.NewGuid(), ModuleA, FloorA0, severity: PreliminarySeverity.MuyGrave);

        Assert.False(second.IncidentCreated);
        Assert.Equal(first.IncidentId, second.IncidentId);
        Assert.Single(harness.Sender.Calls);

        await using var db = database.NewContext();
        var incident = await db.Incidents.Include(i => i.Reports).SingleAsync();
        Assert.Equal(2, incident.Reports.Count);
        Assert.Equal(coordinator, incident.CurrentCoordinatorUserId);
        Assert.Equal(IncidentHarness.Start, incident.OpenedAtUtc); // el aporte no reinicia el plazo AUTO
        var contribution = incident.Reports.Single(r => !r.IsInitial);
        Assert.Equal(IncidentHarness.Start.AddSeconds(30), contribution.ReportedAtUtc);
        Assert.Equal(PreliminarySeverity.MuyGrave, contribution.PreliminarySeverity);
    }

    [Fact]
    public async Task AnotherFloorOpensItsOwnIncident()
    {
        await using var database = await SqliteIncidentDatabase.CreateAsync();
        var harness = new IncidentHarness(database.NewContext, Guid.NewGuid());

        var first = await harness.SubmitAsync(Guid.NewGuid(), ModuleA, FloorA0);
        var other = await harness.SubmitAsync(Guid.NewGuid(), ModuleA, FloorA1);

        Assert.True(other.IncidentCreated);
        Assert.NotEqual(first.IncidentId, other.IncidentId);
        Assert.Equal(2, harness.Sender.Calls.Count);
    }

    [Fact]
    public async Task ReportAfterSimulatedClosureOpensNewIncidentAndNewAlert()
    {
        await using var database = await SqliteIncidentDatabase.CreateAsync();
        var harness = new IncidentHarness(database.NewContext, Guid.NewGuid());
        var first = await harness.SubmitAsync(Guid.NewGuid(), ModuleA, FloorA0);

        // Cierre de ensayo (RF19 es de Sprint 3; reparto 7.1).
        await using (var db = database.NewContext())
        {
            var incident = await db.Incidents.SingleAsync();
            incident.Status = IncidentStatuses.Closed;
            incident.ClosedAtUtc = IncidentHarness.Start.AddMinutes(40);
            incident.ConcurrencyStamp = Guid.NewGuid();
            await db.SaveChangesAsync();
        }

        harness.Clock.Advance(TimeSpan.FromHours(1));
        var next = await harness.SubmitAsync(Guid.NewGuid(), ModuleA, FloorA0);

        Assert.True(next.IncidentCreated);
        Assert.NotEqual(first.IncidentId, next.IncidentId);
        var alerts = harness.Sender.Calls.ToArray();
        Assert.Equal(2, alerts.Length);
        Assert.NotEqual(alerts[0].EventId, alerts[1].EventId);
    }

    [Fact]
    public async Task InactiveFloorOrFloorFromAnotherModuleIsRejected()
    {
        await using var database = await SqliteIncidentDatabase.CreateAsync();
        var harness = new IncidentHarness(database.NewContext, Guid.NewGuid());
        var (service, db) = harness.NewService();
        await using (db)
        {
            var inactive = await service.SubmitAsync(
                Guid.NewGuid(),
                IncidentHarness.Request(ModuleA, DevLocationCatalog.ModuleAFloor2Inactive, Guid.NewGuid()));
            var mismatched = await service.SubmitAsync(
                Guid.NewGuid(),
                IncidentHarness.Request(ModuleA, DevLocationCatalog.ModuleBFloor0, Guid.NewGuid()));

            Assert.IsType<SubmitReportResult.LocationUnavailable>(inactive);
            Assert.IsType<SubmitReportResult.LocationUnavailable>(mismatched);
        }

        await using var check = database.NewContext();
        Assert.Equal(0, await check.Incidents.CountAsync());
        Assert.Empty(harness.Sender.Calls);
    }

    [Fact]
    public async Task MissingFieldsAreReportedIndividually()
    {
        await using var database = await SqliteIncidentDatabase.CreateAsync();
        var harness = new IncidentHarness(database.NewContext, Guid.NewGuid());
        var (service, db) = harness.NewService();
        await using (db)
        {
            var result = await service.SubmitAsync(
                Guid.NewGuid(),
                new SubmitIncidentReportRequest(Guid.NewGuid(), null, ModuleA, null, null, null));

            var invalid = Assert.IsType<SubmitReportResult.Invalid>(result);
            Assert.Equal(
                ["FloorId", "PreliminarySeverity", "Type"],
                invalid.Errors.Keys.Order().ToArray());
        }
    }

    [Fact]
    public async Task ResubmittingSameFormDoesNotDuplicateContribution()
    {
        await using var database = await SqliteIncidentDatabase.CreateAsync();
        var harness = new IncidentHarness(database.NewContext, Guid.NewGuid());
        var reporter = Guid.NewGuid();
        var clientRequestId = Guid.NewGuid();

        var first = await harness.SubmitAsync(reporter, ModuleA, FloorA0, clientRequestId);
        var repeated = await harness.SubmitAsync(reporter, ModuleA, FloorA0, clientRequestId);

        Assert.Equal(first, repeated);
        await using var db = database.NewContext();
        Assert.Equal(1, await db.IncidentReports.CountAsync());
        Assert.Single(harness.Sender.Calls);
    }

    [Fact]
    public async Task WithoutCoverageIncidentIsKeptAndNoAlertIsSent()
    {
        await using var database = await SqliteIncidentDatabase.CreateAsync();
        var harness = new IncidentHarness(database.NewContext, coordinatorUserId: null);

        var response = await harness.SubmitAsync(Guid.NewGuid(), ModuleA, FloorA0);

        Assert.True(response.IncidentCreated);
        Assert.Empty(harness.Sender.Calls);
        await using var db = database.NewContext();
        var incident = await db.Incidents.SingleAsync();
        Assert.Null(incident.CurrentCoordinatorUserId);
        Assert.Equal(InitialAlertStates.NoCoverage, incident.InitialAlertState);
    }

    [Fact]
    public async Task FailedAlertIsRetriedWithSameEventIdAndStopsAfterSuccess()
    {
        await using var database = await SqliteIncidentDatabase.CreateAsync();
        var harness = new IncidentHarness(database.NewContext, Guid.NewGuid(), senderFailures: 1);

        var response = await harness.SubmitAsync(Guid.NewGuid(), ModuleA, FloorA0);

        await using (var db = database.NewContext())
        {
            var incident = await db.Incidents.SingleAsync();
            Assert.Equal(InitialAlertStates.Failed, incident.InitialAlertState);
            Assert.Equal(1, incident.InitialAlertAttempts);
        }

        harness.Clock.Advance(TimeSpan.FromSeconds(20));
        await using (var db = database.NewContext())
        {
            Assert.True(await harness.NewDispatcher(db).TryDispatchAsync(response.IncidentId));
        }

        await using (var db = database.NewContext())
        {
            Assert.False(await harness.NewDispatcher(db).TryDispatchAsync(response.IncidentId));
            var incident = await db.Incidents.SingleAsync();
            Assert.Equal(InitialAlertStates.Dispatched, incident.InitialAlertState);
            Assert.Equal(2, incident.InitialAlertAttempts);
        }

        var calls = harness.Sender.Calls.ToArray();
        Assert.Equal(2, calls.Length);
        Assert.All(calls, call => Assert.Equal(calls[0].EventId, call.EventId));
    }

    [Fact]
    public async Task ReaderPublishesSnapshotWithOptionalClosure()
    {
        await using var database = await SqliteIncidentDatabase.CreateAsync();
        var coordinator = Guid.NewGuid();
        var harness = new IncidentHarness(database.NewContext, coordinator);
        var response = await harness.SubmitAsync(Guid.NewGuid(), ModuleA, FloorA0);

        await using var db = database.NewContext();
        var snapshot = await new EfIncidentReader(db).GetByIdAsync(response.IncidentId);

        Assert.NotNull(snapshot);
        Assert.Equal(ModuleA, snapshot.ModuleId);
        Assert.Equal(FloorA0, snapshot.FloorId);
        Assert.Equal(IncidentStatuses.Active, snapshot.Status);
        Assert.Equal(coordinator, snapshot.CurrentCoordinatorUserId);
        Assert.Equal(IncidentHarness.Start, snapshot.OpenedAtUtc);
        Assert.NotEqual(Guid.Empty, snapshot.InitialAlertEventId);
        Assert.Null(snapshot.ClosedAtUtc);
        Assert.Null(await new EfIncidentReader(db).GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task OnlyCurrentCoordinatorCanReadContributions()
    {
        await using var database = await SqliteIncidentDatabase.CreateAsync();
        var coordinator = Guid.NewGuid();
        var harness = new IncidentHarness(database.NewContext, coordinator);
        var response = await harness.SubmitAsync(Guid.NewGuid(), ModuleA, FloorA0);
        await harness.SubmitAsync(Guid.NewGuid(), ModuleA, FloorA0);

        await using var db = database.NewContext();
        var queries = new IncidentQueries(db, harness.Locations);

        var found = Assert.IsType<CoordinatorQueryResult.Found>(
            await queries.GetForCoordinatorAsync(response.IncidentId, coordinator, default));
        Assert.Equal(2, found.Incident.ReportCount);
        Assert.Equal("Módulo de prueba A", found.Incident.ModuleName);
        Assert.IsType<CoordinatorQueryResult.NotCoordinator>(
            await queries.GetForCoordinatorAsync(response.IncidentId, Guid.NewGuid(), default));
        Assert.Single(await queries.GetAssignedAsync(coordinator, default));
        Assert.Empty(await queries.GetAssignedAsync(Guid.NewGuid(), default));
    }
}
