namespace Univalle.Emergencias.Contracts.Turns;

public sealed record TurnCoverage(
    Guid CoordinatorUserId,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtc);

/// <summary>Jorge resuelve horarios y excepciones; null representa ausencia de cobertura.</summary>
public interface ITurnResolver
{
    Task<TurnCoverage?> ResolveAsync(
        DateTimeOffset instantUtc,
        CancellationToken cancellationToken = default);
}
