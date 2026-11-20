using Univalle.Emergencias.Contracts.Audit;

namespace Univalle.Emergencias.Api.Data;

public sealed class AuditEntry
{
    public Guid AuditEntryId { get; set; }
    public Guid? ActorUserId { get; set; }
    public AuditActorKind ActorKind { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public Guid? IncidentId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string? MetadataJson { get; set; }
}
