using System.Text.Json;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Common;

namespace Univalle.Emergencias.Api.Services;

public sealed class EfAuditTrail(AppDbContext db, IServerClock clock) : IAuditTrail
{
    public async Task<Guid> RecordAsync(
        AuditWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var action = request.Action?.Trim();
        var entityType = request.EntityType?.Trim();
        if (string.IsNullOrWhiteSpace(action) || action.Length > 120)
        {
            throw new ArgumentException("Action debe tener entre 1 y 120 caracteres.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(entityType) || entityType.Length > 80)
        {
            throw new ArgumentException("EntityType debe tener entre 1 y 80 caracteres.", nameof(request));
        }

        if (!Enum.IsDefined(request.ActorKind)
            || (request.ActorKind == AuditActorKind.User && request.ActorUserId is null)
            || (request.ActorKind == AuditActorKind.System && request.ActorUserId is not null))
        {
            throw new ArgumentException("ActorKind y ActorUserId no son coherentes.", nameof(request));
        }

        if (request.ActorUserId == Guid.Empty
            || request.EntityId == Guid.Empty
            || request.IncidentId == Guid.Empty)
        {
            throw new ArgumentException("Los identificadores no pueden ser Guid.Empty.", nameof(request));
        }

        if (request.MetadataJson is { Length: > 4000 })
        {
            throw new ArgumentException("MetadataJson supera 4000 caracteres.", nameof(request));
        }

        if (request.MetadataJson is not null)
        {
            try
            {
                using var document = JsonDocument.Parse(request.MetadataJson);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("MetadataJson debe contener JSON válido.", nameof(request), ex);
            }
        }

        var now = clock.UtcNow.ToUniversalTime();
        var entry = new AuditEntry
        {
            AuditEntryId = Guid.CreateVersion7(now),
            ActorUserId = request.ActorUserId,
            ActorKind = request.ActorKind,
            Action = action,
            EntityType = entityType,
            EntityId = request.EntityId,
            IncidentId = request.IncidentId,
            OccurredAtUtc = now,
            MetadataJson = request.MetadataJson
        };

        db.AuditEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        return entry.AuditEntryId;
    }
}
