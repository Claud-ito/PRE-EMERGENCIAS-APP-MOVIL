
namespace Univalle.Emergencias.Api.Notifications;

public sealed class NotificationEntry
{
    public Guid EventId { get; set; }
    public Guid IncidentId { get; set; }
    public string Type { get; set; } = string.Empty;
    public Guid RecipientUserId { get; set; }
    public DateTimeOffset RegisteredAtUtc { get; set; }
    public string DeliveryStatus { get; set; } = "Pending";
    public DateTimeOffset? DeliveredAtUtc { get; set; }
    public DateTimeOffset? ReadAtUtc { get; set; }
}
