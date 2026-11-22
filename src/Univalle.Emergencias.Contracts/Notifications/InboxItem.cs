namespace Univalle.Emergencias.Contracts.Notifications;

public sealed record InboxItem(Guid EventId, Guid IncidentId, string Type, DateTimeOffset RegisteredAtUtc,
    string DeliveryStatus, DateTimeOffset? DeliveredAtUtc, DateTimeOffset? ReadAtUtc);
