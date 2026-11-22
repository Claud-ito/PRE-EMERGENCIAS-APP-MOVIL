using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Incidents;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Brigades;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Incidents;
using Univalle.Emergencias.Contracts.Notifications;

namespace Univalle.Emergencias.Api.Coordination;

public sealed class CalloutService(AppDbContext db, IBrigadeLeaderLookup leaders,
    INotificationSender notifications, IServerClock clock, IAuditTrail audit)
{
    public async Task<bool> RegisterAsync(Guid incidentId, Guid? manualActor, CancellationToken ct)
    {
        var incident = await db.Incidents.SingleOrDefaultAsync(x => x.IncidentId == incidentId, ct);
        if (incident is null || incident.Status != IncidentStatuses.Active) return false;
        if (manualActor.HasValue && incident.CurrentCoordinatorUserId != manualActor)
            throw new UnauthorizedAccessException();
        var now = clock.UtcNow.ToUniversalTime();
        if (manualActor is null && (incident.InitialAlertRecipientUserId is null ||
            incident.AutoDueAtUtc is null || incident.AutoDueAtUtc > now.UtcDateTime)) return false;
        if (await db.Callouts.AnyAsync(x => x.IncidentId == incidentId, ct)) return false;
        var leader = await leaders.GetCurrentLeaderAsync(incident.ModuleId, now, ct);
        if (leader is null) return false;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Reserve the incident before inserting the callout. A competing request uses the same stamp.
        incident.ConcurrencyStamp = Guid.NewGuid();
        incident.AutoDueAtUtc = null;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
        db.Callouts.Add(new Callout { IncidentId = incidentId, SenderUserId = manualActor,
            RecipientUserId = leader.UserId, Channel = manualActor.HasValue ? "Manual" : "AUTO",
            OccurredAtUtc = now });
        await db.SaveChangesAsync(ct);
        if (manualActor is null)
            await notifications.RegisterAndSendAsync(new NotificationRequest(
                Guid.NewGuid(), incidentId, "incident.auto", leader.UserId), ct);
        await audit.RecordAsync(new AuditWriteRequest(
            manualActor, manualActor.HasValue ? AuditActorKind.User : AuditActorKind.System,
            manualActor.HasValue ? "incident.callout.manual" : "incident.callout.auto",
            "Callout", incidentId, incidentId,
            System.Text.Json.JsonSerializer.Serialize(new { RecipientUserId = leader.UserId })), ct);
        await transaction.CommitAsync(ct);
        return true;
    }
}

public sealed class AutoWorker(IServiceScopeFactory scopes, ILogger<AutoWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var now = scope.ServiceProvider.GetRequiredService<IServerClock>().UtcNow.UtcDateTime;
                var ids = await db.Incidents.AsNoTracking().Where(x => x.AutoDueAtUtc <= now &&
                    x.Status == IncidentStatuses.Active && x.InitialAlertRecipientUserId != null)
                    .Select(x => x.IncidentId).ToListAsync(stoppingToken);
                foreach (var id in ids)
                {
                    try
                    {
                        using var item = scopes.CreateScope();
                        await item.ServiceProvider.GetRequiredService<CalloutService>()
                            .RegisterAsync(id, null, stoppingToken);
                    }
                    catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                    {
                        log.LogWarning(ex, "Error de AUTO para el incidente {IncidentId}", id);
                    }
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogWarning(ex, "Error al recuperar las convocatorias pendientes");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
