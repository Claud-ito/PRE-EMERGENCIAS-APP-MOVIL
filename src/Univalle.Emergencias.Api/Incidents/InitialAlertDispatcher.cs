using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Incidents;
using Univalle.Emergencias.Contracts.Notifications;

namespace Univalle.Emergencias.Api.Incidents;

public interface IInitialAlertDispatcher
{
    /// <summary>Envía (o reintenta) la alerta inicial del incidente. Nunca lanza por fallos del canal.</summary>
    Task<bool> TryDispatchAsync(Guid incidentId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Entrega la alerta inicial mediante INotificationSender (JO-02). No es otro emisor:
/// solo conserva el estado técnico y reutiliza el mismo EventId en cada reintento.
/// </summary>
public sealed class InitialAlertDispatcher(
    AppDbContext db,
    INotificationSender sender,
    IServerClock clock,
    IAuditTrail audit,
    ILogger<InitialAlertDispatcher> logger) : IInitialAlertDispatcher
{
    public const int MaxAttempts = 5;

    public async Task<bool> TryDispatchAsync(Guid incidentId, CancellationToken cancellationToken = default)
    {
        var incident = await db.Incidents.SingleOrDefaultAsync(
            candidate => candidate.IncidentId == incidentId,
            cancellationToken);
        if (incident?.InitialAlertRecipientUserId is not { } recipientUserId
            || incident.InitialAlertState is not (InitialAlertStates.Pending or InitialAlertStates.Failed)
            || incident.InitialAlertAttempts >= MaxAttempts)
        {
            return false;
        }

        // Reservar el intento: si otro proceso lo reservó antes, el token de concurrencia lo detecta.
        incident.InitialAlertAttempts++;
        incident.InitialAlertLastAttemptAtUtc = clock.UtcNow.ToUniversalTime();
        incident.ConcurrencyStamp = Guid.NewGuid();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }

        string state;
        string? deliveryStatus = null;
        try
        {
            var result = await sender.RegisterAndSendAsync(
                new NotificationRequest(
                    incident.InitialAlertEventId,
                    incident.IncidentId,
                    IncidentNotificationTypes.InitialAlert,
                    recipientUserId),
                cancellationToken);
            state = InitialAlertStates.Dispatched;
            deliveryStatus = result.DeliveryStatus;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "No se pudo enviar la alerta inicial {EventId} del incidente {IncidentId} (intento {Attempt}).",
                incident.InitialAlertEventId,
                incident.IncidentId,
                incident.InitialAlertAttempts);
            state = InitialAlertStates.Failed;
        }

        await SaveOutcomeAsync(incident, state, deliveryStatus, cancellationToken);

        await audit.RecordAsync(
            new AuditWriteRequest(
                null,
                AuditActorKind.System,
                state == InitialAlertStates.Dispatched
                    ? "incident.initial-alert-dispatched"
                    : "incident.initial-alert-failed",
                "Incident",
                incident.IncidentId,
                incident.IncidentId,
                JsonSerializer.Serialize(new
                {
                    incident.InitialAlertEventId,
                    Attempt = incident.InitialAlertAttempts,
                    DeliveryStatus = deliveryStatus
                })),
            cancellationToken);

        return state == InitialAlertStates.Dispatched;
    }

    /// <summary>Guarda el resultado aunque otra operación (p. ej. un relevo) haya modificado el incidente.</summary>
    private async Task SaveOutcomeAsync(
        Incident incident,
        string state,
        string? deliveryStatus,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            incident.InitialAlertState = state;
            incident.InitialAlertDeliveryStatus = deliveryStatus;
            incident.ConcurrencyStamp = Guid.NewGuid();
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                foreach (var entry in ex.Entries)
                {
                    await entry.ReloadAsync(cancellationToken);
                }
            }
        }

        throw new InvalidOperationException(
            $"No se pudo guardar el estado de la alerta inicial del incidente {incident.IncidentId}.");
    }
}

/// <summary>Reintenta alertas iniciales pendientes o fallidas, p. ej. tras reiniciar la API.</summary>
public sealed class InitialAlertRetryWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<InitialAlertRetryWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    /// <summary>Margen para no competir con el envío inmediato que hace la propia solicitud.</summary>
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await RetryDueAlertsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Fallo al revisar alertas iniciales pendientes.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RetryDueAlertsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IServerClock>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IInitialAlertDispatcher>();

        var candidates = await db.Incidents.AsNoTracking()
            .Where(incident => incident.InitialAlertRecipientUserId != null
                && (incident.InitialAlertState == InitialAlertStates.Pending
                    || incident.InitialAlertState == InitialAlertStates.Failed)
                && incident.InitialAlertAttempts < InitialAlertDispatcher.MaxAttempts)
            .OrderBy(incident => incident.InitialAlertAttempts)
            .ThenBy(incident => incident.IncidentId)
            .Select(incident => new
            {
                incident.IncidentId,
                incident.OpenedAtUtc,
                incident.InitialAlertLastAttemptAtUtc,
                incident.InitialAlertAttempts
            })
            .Take(50)
            .ToListAsync(cancellationToken);

        var now = clock.UtcNow;
        foreach (var candidate in candidates)
        {
            var reference = candidate.InitialAlertLastAttemptAtUtc ?? candidate.OpenedAtUtc;
            var backoff = BaseBackoff * Math.Max(1, candidate.InitialAlertAttempts);
            if (now - reference >= backoff)
            {
                await dispatcher.TryDispatchAsync(candidate.IncidentId, cancellationToken);
            }
        }
    }
}
