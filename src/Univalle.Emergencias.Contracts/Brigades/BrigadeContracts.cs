namespace Univalle.Emergencias.Contracts.Brigades;

/// <summary>Especialidad se mantiene textual hasta que Diana fije el catálogo de brigadas.</summary>
public sealed record BrigadeMember(Guid UserId, string Specialty);

public sealed record ModuleLeader(
    Guid UserId,
    Guid BrigadeId,
    Guid ModuleId,
    IReadOnlyList<BrigadeMember> Members);

/// <summary>Devuelve el líder vigente del módulo o null cuando no hay asignación.</summary>
public interface IBrigadeLeaderLookup
{
    Task<ModuleLeader?> GetCurrentLeaderAsync(
        Guid moduleId,
        DateTimeOffset instantUtc,
        CancellationToken cancellationToken = default);
}
