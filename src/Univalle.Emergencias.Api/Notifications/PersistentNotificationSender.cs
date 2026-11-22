
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Notifications;

namespace Univalle.Emergencias.Api.Notifications;

public sealed class PersistentNotificationSender(
    AppDbContext db,
    IServerClock clock) : INotificationSender
{
    public async Task<NotificationResult> RegisterAndSendAsync(
        NotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.EventId == Guid.Empty ||
            request.IncidentId == Guid.Empty ||
            request.RecipientUserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Type))
        {
            throw new ArgumentException(
                "La notificacion contiene datos invalidos.");
        }

        var existing = await db.Set<NotificationEntry>()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.EventId == request.EventId,
                cancellationToken);

        if (existing is not null)
        {
            return ToResult(ValidateExisting(existing, request));
        }

        var entry = new NotificationEntry
        {
            EventId = request.EventId,
            IncidentId = request.IncidentId,
            Type = request.Type,
            RecipientUserId = request.RecipientUserId,
            RegisteredAtUtc = clock.UtcNow.ToUniversalTime(),
            DeliveryStatus = "Pending"
        };

        db.Set<NotificationEntry>().Add(entry);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Otro proceso pudo registrar el mismo EventId.
            db.Entry(entry).State = EntityState.Detached;

            var saved = await db.Set<NotificationEntry>()
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.EventId == request.EventId,
                    cancellationToken);

            if (saved is null)
                throw;

            return ToResult(ValidateExisting(saved, request));
        }

        return ToResult(entry);
    }

    private static NotificationEntry ValidateExisting(
        NotificationEntry entry,
        NotificationRequest request)
    {
        if (entry.IncidentId != request.IncidentId ||
            entry.RecipientUserId != request.RecipientUserId ||
            entry.Type != request.Type)
        {
            throw new InvalidOperationException(
                "El EventId ya pertenece a otra notificacion.");
        }

        return entry;
    }

    private static NotificationResult ToResult(
        NotificationEntry entry)
    {
        return new NotificationResult(
            entry.EventId,
            entry.IncidentId,
            entry.Type,
            entry.RecipientUserId,
            entry.RegisteredAtUtc,
            entry.DeliveryStatus);
    }
}
