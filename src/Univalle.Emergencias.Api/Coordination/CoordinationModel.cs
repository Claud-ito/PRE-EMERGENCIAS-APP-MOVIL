using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Univalle.Emergencias.Api.Coordination;

public sealed class AutoPreference
{
    [Key] public Guid CoordinatorUserId { get; set; }
    public bool Enabled { get; set; }
}

public sealed class Callout
{
    [Key] public Guid IncidentId { get; set; }
    public Guid? SenderUserId { get; set; }
    public Guid RecipientUserId { get; set; }
    [MaxLength(16)] public string Channel { get; set; } = "";
    public DateTimeOffset OccurredAtUtc { get; set; }
}


public sealed class Handoff
{
    public Guid Id { get; set; }
    public Guid IncidentId { get; set; }
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    public DateTimeOffset ProposedAtUtc { get; set; }
    public DateTimeOffset? AcceptedAtUtc { get; set; }
}
