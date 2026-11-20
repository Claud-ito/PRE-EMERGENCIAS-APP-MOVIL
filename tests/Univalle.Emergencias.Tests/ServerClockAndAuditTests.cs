using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Services;
using Univalle.Emergencias.Contracts.Audit;
using Xunit;

namespace Univalle.Emergencias.Tests;

public sealed class ServerClockAndAuditTests
{
    [Fact]
    public void ServerClockUsesReplaceableTimeProvider()
    {
        var start = new DateTimeOffset(2026, 11, 20, 4, 0, 0, TimeSpan.Zero);
        var provider = new ManualTimeProvider(start);
        var clock = new SystemServerClock(provider);

        Assert.Equal(start, clock.UtcNow);

        provider.Advance(TimeSpan.FromHours(6));

        Assert.Equal(start.AddHours(6), clock.UtcNow);
        Assert.Equal(TimeSpan.Zero, clock.UtcNow.Offset);
    }

    [Fact]
    public async Task AuditRecordPersistsServerTimeAndReferencesAcrossContexts()
    {
        await using var connection = await OpenInMemoryDatabaseAsync();
        var options = CreateOptions(connection);
        var instant = new DateTimeOffset(2026, 11, 20, 4, 10, 30, TimeSpan.Zero);
        var provider = new ManualTimeProvider(instant);
        var actorId = Guid.NewGuid();
        var entityId = Guid.NewGuid();
        var incidentId = Guid.NewGuid();
        Guid recordedId;

        await using (var writer = new AppDbContext(options))
        {
            var trail = new EfAuditTrail(writer, new SystemServerClock(provider));
            recordedId = await trail.RecordAsync(new AuditWriteRequest(
                actorId,
                AuditActorKind.User,
                "  incident.reported  ",
                "  Incident  ",
                entityId,
                incidentId,
                "{\"source\":\"test\"}"));
        }

        Assert.NotEqual(Guid.Empty, recordedId);

        await using var reader = new AppDbContext(options);
        var stored = await reader.AuditEntries.AsNoTracking().SingleAsync();
        Assert.Equal(recordedId, stored.AuditEntryId);
        Assert.Equal(actorId, stored.ActorUserId);
        Assert.Equal(AuditActorKind.User, stored.ActorKind);
        Assert.Equal("incident.reported", stored.Action);
        Assert.Equal("Incident", stored.EntityType);
        Assert.Equal(entityId, stored.EntityId);
        Assert.Equal(incidentId, stored.IncidentId);
        Assert.Equal(instant, stored.OccurredAtUtc);
        Assert.Equal("{\"source\":\"test\"}", stored.MetadataJson);
    }

    [Fact]
    public async Task SystemAuditPersistsWithoutUserId()
    {
        await using var connection = await OpenInMemoryDatabaseAsync();
        var options = CreateOptions(connection);
        var instant = new DateTimeOffset(2026, 11, 20, 6, 0, 0, TimeSpan.Zero);

        await using var db = new AppDbContext(options);
        var trail = new EfAuditTrail(db, new SystemServerClock(new ManualTimeProvider(instant)));
        await trail.RecordAsync(new AuditWriteRequest(
            null, AuditActorKind.System, "notification.retry", "Notification"));

        var stored = await db.AuditEntries.SingleAsync();
        Assert.Null(stored.ActorUserId);
        Assert.Equal(AuditActorKind.System, stored.ActorKind);
        Assert.Equal(instant, stored.OccurredAtUtc);
    }

    [Fact]
    public async Task InvalidAuditRequestsDoNotPersistRows()
    {
        await using var connection = await OpenInMemoryDatabaseAsync();
        var options = CreateOptions(connection);
        await using var db = new AppDbContext(options);
        var clock = new SystemServerClock(new ManualTimeProvider(DateTimeOffset.UtcNow));
        var trail = new EfAuditTrail(db, clock);

        var invalidRequests = new[]
        {
            new AuditWriteRequest(null, AuditActorKind.User, "incident.reported", "Incident"),
            new AuditWriteRequest(Guid.NewGuid(), AuditActorKind.System, "incident.reported", "Incident"),
            new AuditWriteRequest(null, AuditActorKind.System, "  ", "Incident"),
            new AuditWriteRequest(null, AuditActorKind.System, "incident.reported", "  "),
            new AuditWriteRequest(Guid.Empty, AuditActorKind.User, "incident.reported", "Incident"),
            new AuditWriteRequest(null, AuditActorKind.System, "incident.reported", "Incident",
                MetadataJson: "{invalid")
        };

        foreach (var request in invalidRequests)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => trail.RecordAsync(request));
        }

        Assert.Equal(0, await db.AuditEntries.CountAsync());
    }

    private static DbContextOptions<AppDbContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;

    private static async Task<SqliteConnection> OpenInMemoryDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(CreateOptions(connection));
        await db.Database.EnsureCreatedAsync();
        return connection;
    }

    private sealed class ManualTimeProvider(DateTimeOffset initialInstant) : TimeProvider
    {
        private DateTimeOffset _instant = initialInstant;

        public override DateTimeOffset GetUtcNow() => _instant;

        public void Advance(TimeSpan duration) => _instant += duration;
    }
}
