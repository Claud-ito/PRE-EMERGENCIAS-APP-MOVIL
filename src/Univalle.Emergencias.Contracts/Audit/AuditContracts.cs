namespace Univalle.Emergencias.Contracts.Audit;

public enum AuditActorKind
{
    User,
    System
}

/// <summary>Datos de auditoría entregados por un servicio servidor, nunca por el cliente móvil.</summary>
public sealed record AuditWriteRequest(
    Guid? ActorUserId,
    AuditActorKind ActorKind,
    string Action,
    string EntityType,
    Guid? EntityId = null,
    Guid? IncidentId = null,
    string? MetadataJson = null);

/// <summary>Registra un evento con ID y hora asignados en el servidor.</summary>
public interface IAuditTrail
{
    Task<Guid> RecordAsync(
        AuditWriteRequest request,
        CancellationToken cancellationToken = default);
}
