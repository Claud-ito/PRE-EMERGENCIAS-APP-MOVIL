namespace Univalle.Emergencias.Contracts.Notifications;

/// <summary>EventId es la clave de idempotencia; un reintento conserva el mismo valor.</summary>
public sealed record NotificationRequest(
    Guid EventId,
    Guid IncidentId,
    string Type,
    Guid RecipientUserId);

public sealed record NotificationResult(
    Guid EventId,
    Guid IncidentId,
    string Type,
    Guid RecipientUserId,
    DateTimeOffset RegisteredAtUtc,
    string DeliveryStatus);

/// <summary>Jorge registra y envía el aviso; un registro no implica entrega al dispositivo.</summary>
public interface INotificationSender
{
    Task<NotificationResult> RegisterAndSendAsync(
        NotificationRequest request,
        CancellationToken cancellationToken = default);
}
