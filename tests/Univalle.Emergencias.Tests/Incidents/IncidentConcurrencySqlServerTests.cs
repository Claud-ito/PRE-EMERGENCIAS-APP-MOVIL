using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Incidents;
using Univalle.Emergencias.Api.Incidents.Development;
using Univalle.Emergencias.Contracts.Incidents;
using Xunit;
using Xunit.Abstractions;

namespace Univalle.Emergencias.Tests.Incidents;

/// <summary>
/// Entrega verificable de AL-02: reportes simultáneos de la misma ubicación contra SQL Server real.
/// La garantía la aporta el índice único filtrado de la migración, no un bloqueo en memoria.
/// </summary>
public sealed class IncidentConcurrencySqlServerTests(ITestOutputHelper output) : IAsyncLifetime
{
    private DbContextOptions<AppDbContext>? _options;

    public async Task InitializeAsync()
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(baseConnection))
        {
            return;
        }

        var builder = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"UnivalleIncidentsTest_{Guid.NewGuid():N}",
            MaxPoolSize = 200
        };
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var db = new AppDbContext(_options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_options is not null)
        {
            await using var db = new AppDbContext(_options);
            await db.Database.EnsureDeletedAsync();
        }
    }

    [SqlServerFact]
    public async Task TwoSimultaneousReportsProduceOneIncidentTwoContributionsAndOneAlert()
    {
        var harness = new IncidentHarness(() => new AppDbContext(_options!), Guid.NewGuid());

        var responses = await SubmitSimultaneouslyAsync(harness, 2);

        await AssertSingleIncidentAsync(harness, responses, expectedReports: 2);
    }

    [SqlServerFact]
    public async Task ManySimultaneousReportsProduceOneIncidentAndKeepEveryContribution()
    {
        var harness = new IncidentHarness(() => new AppDbContext(_options!), Guid.NewGuid());

        var responses = await SubmitSimultaneouslyAsync(harness, 25);

        await AssertSingleIncidentAsync(harness, responses, expectedReports: 25);
    }

    private static async Task<SubmitIncidentReportResponse[]> SubmitSimultaneouslyAsync(
        IncidentHarness harness,
        int count)
    {
        using var gate = new SemaphoreSlim(0, count);
        var tasks = Enumerable.Range(0, count).Select(_ => Task.Run(async () =>
        {
            var (service, db) = harness.NewService();
            await using (db)
            {
                var request = IncidentHarness.Request(
                    DevLocationCatalog.ModuleA,
                    DevLocationCatalog.ModuleAFloor0,
                    Guid.NewGuid());
                await gate.WaitAsync();
                var result = await service.SubmitAsync(Guid.NewGuid(), request);
                return Assert.IsType<SubmitReportResult.Accepted>(result).Response;
            }
        })).ToArray();

        gate.Release(count); // liberar todos a la vez
        return await Task.WhenAll(tasks);
    }

    private async Task AssertSingleIncidentAsync(
        IncidentHarness harness,
        SubmitIncidentReportResponse[] responses,
        int expectedReports)
    {
        output.WriteLine($"Reportes: {expectedReports}; carreras resueltas por el índice único: {harness.RaceLogger.Races}.");
        Assert.Single(responses.Select(response => response.IncidentId).Distinct());
        Assert.Single(responses, response => response.IncidentCreated);

        await using var db = new AppDbContext(_options!);
        var incident = await db.Incidents.Include(i => i.Reports).SingleAsync();
        Assert.Equal(expectedReports, incident.Reports.Count);
        Assert.Single(incident.Reports, report => report.IsInitial);
        Assert.Equal(expectedReports, incident.Reports.Select(report => report.ReporterUserId).Distinct().Count());

        var alert = Assert.Single(harness.Sender.Calls);
        Assert.Equal(incident.InitialAlertEventId, alert.EventId);
        Assert.Equal(InitialAlertStates.Dispatched, incident.InitialAlertState);

        var audit = await db.AuditEntries.Where(entry => entry.IncidentId == incident.IncidentId).ToListAsync();
        Assert.Single(audit, entry => entry.Action == "incident.opened");
        Assert.Equal(expectedReports, audit.Count(entry => entry.Action == "incident.report-added"));
    }
}
