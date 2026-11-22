namespace Univalle.Emergencias.Contracts.Turns;

public sealed record AutoSetting(bool Enabled);
public sealed record CoordinationRecipient(Guid UserId);
public sealed record CoordinatorOption(Guid UserId, string FullName);
public sealed record HandoffView(Guid Id, Guid IncidentId, Guid FromUserId, Guid ToUserId,
    DateTimeOffset ProposedAtUtc, DateTimeOffset? AcceptedAtUtc);
public sealed record CalloutView(Guid IncidentId, Guid? SenderUserId, Guid RecipientUserId,
    string Channel, DateTimeOffset OccurredAtUtc);
public sealed record CalendarInterval(Guid? CoordinatorUserId, DateTimeOffset StartsAtUtc, DateTimeOffset EndsAtUtc);
