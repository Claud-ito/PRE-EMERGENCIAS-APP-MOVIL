using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Incidents;
using Univalle.Emergencias.Api.Incidents.Development;
using Univalle.Emergencias.Api.Services;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Incidents;
using Univalle.Emergencias.Contracts.Locations;
using Univalle.Emergencias.Contracts.Notifications;
using Univalle.Emergencias.Contracts.Turns;
using Xunit;

namespace Univalle.Emergencias.Tests.Incidents;

/// <summary>Reloj controlado: permite fijar instantes sin esperar tiempo real.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan duration) => _now += duration;
}

/// <summary>Turno de prueba: un coordinador fijo o ausencia de cobertura.</summary>
internal sealed class FixedTurnResolver(Guid? coordinatorUserId) : ITurnResolver
{
    public Task<TurnCoverage?> ResolveAsync(DateTimeOffset instantUtc, CancellationToken cancellationToken = default) =>
        Task.FromResult(coordinatorUserId is { } id
            ? new TurnCoverage(id, instantUtc.AddHours(-1), instantUtc.AddHours(1))
            : null);
}

/// <summary>Emisor de prueba seguro para hilos; puede fallar las primeras N llamadas.</summary>
internal sealed class RecordingNotificationSender(int failuresBeforeSuccess = 0) : INotificationSender
{
    private int _remainingFailures = failuresBeforeSuccess;

    public ConcurrentQueue<NotificationRequest> Calls { get; } = new();

    public Task<NotificationResult> RegisterAndSendAsync(
        NotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        Calls.Enqueue(request);
        if (Interlocked.Decrement(ref _remainingFailures) >= 0)
        {
            throw new HttpRequestException("Canal de avisos no disponible (prueba).");
        }

        return Task.FromResult(new NotificationResult(
            request.EventId,
            request.IncidentId,
            request.Type,
            request.RecipientUserId,
            DateTimeOffset.UtcNow,
            "Sent"));
    }
}

/// <summary>Cuenta las carreras resueltas por reintento (mensaje informativo del servicio).</summary>
internal sealed class RaceCountingLogger : Microsoft.Extensions.Logging.ILogger<IncidentReportService>
{
    private int _races;

    public int Races => Volatile.Read(ref _races);

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

    public void Log<TState>(
        Microsoft.Extensions.Logging.LogLevel logLevel,
        Microsoft.Extensions.Logging.EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (formatter(state, exception).StartsWith("Carrera", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _races);
        }
    }
}

internal sealed class IncidentHarness
{
    public static readonly DateTimeOffset Start = new(2026, 11, 20, 14, 0, 0, TimeSpan.Zero);

    private readonly Func<AppDbContext> _contextFactory;

    public IncidentHarness(Func<AppDbContext> contextFactory, Guid? coordinatorUserId, int senderFailures = 0)
    {
        _contextFactory = contextFactory;
        Turns = new FixedTurnResolver(coordinatorUserId);
        Sender = new RecordingNotificationSender(senderFailures);
    }

    public ManualClock Clock { get; } = new(Start);
    public ILocationCatalog Locations { get; } = new DevLocationCatalog();
    public ITurnResolver Turns { get; }
    public RecordingNotificationSender Sender { get; }
    public RaceCountingLogger RaceLogger { get; } = new();

    public AppDbContext NewContext() => _contextFactory();

    /// <summary>Crea un servicio con su propio contexto, como lo haría cada solicitud HTTP.</summary>
    public (IncidentReportService Service, AppDbContext Db) NewService()
    {
        var db = _contextFactory();
        var clock = new SystemServerClock(Clock);
        var audit = new EfAuditTrail(db, clock);
        var dispatcher = new InitialAlertDispatcher(
            db, Sender, clock, audit, NullLogger<InitialAlertDispatcher>.Instance);
        var service = new IncidentReportService(
            db, clock, Locations, Turns, audit, dispatcher, RaceLogger);
        return (service, db);
    }

    public InitialAlertDispatcher NewDispatcher(AppDbContext db)
    {
        var clock = new SystemServerClock(Clock);
        return new InitialAlertDispatcher(
            db, Sender, clock, new EfAuditTrail(db, clock), NullLogger<InitialAlertDispatcher>.Instance);
    }

    public async Task<SubmitIncidentReportResponse> SubmitAsync(
        Guid reporterUserId,
        Guid moduleId,
        Guid floorId,
        Guid? clientRequestId = null,
        PreliminarySeverity severity = PreliminarySeverity.Leve)
    {
        var (service, db) = NewService();
        await using (db)
        {
            var result = await service.SubmitAsync(
                reporterUserId,
                Request(moduleId, floorId, clientRequestId ?? Guid.NewGuid(), severity));
            return Assert.IsType<SubmitReportResult.Accepted>(result).Response;
        }
    }

    public static SubmitIncidentReportRequest Request(
        Guid moduleId,
        Guid floorId,
        Guid clientRequestId,
        PreliminarySeverity severity = PreliminarySeverity.Leve) =>
        new(clientRequestId, IncidentType.Incendio, moduleId, floorId, severity, null);
}

/// <summary>Base SQLite en memoria compartida por los contextos de una prueba.</summary>
internal sealed class SqliteIncidentDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    private SqliteIncidentDatabase(SqliteConnection connection)
    {
        _connection = connection;
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
    }

    public static async Task<SqliteIncidentDatabase> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var database = new SqliteIncidentDatabase(connection);
        await using var db = database.NewContext();
        await db.Database.EnsureCreatedAsync();
        return database;
    }

    public AppDbContext NewContext() => new(_options);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}

/// <summary>
/// Prueba que requiere SQL Server real. Define UNIVALLE_TEST_SQLSERVER con una cadena de conexión
/// (sin base de datos o con cualquiera: la prueba crea y elimina una base temporal).
/// </summary>
internal sealed class SqlServerFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "UNIVALLE_TEST_SQLSERVER";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable)))
        {
            Skip = $"Define {EnvironmentVariable} para ejecutar las pruebas de concurrencia con SQL Server.";
        }
    }
}
