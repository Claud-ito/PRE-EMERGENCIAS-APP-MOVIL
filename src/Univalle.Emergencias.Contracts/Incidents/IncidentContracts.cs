namespace Univalle.Emergencias.Contracts.Incidents;

/// <summary>Lectura mínima que publica Alejandro; ClosedAtUtc es null mientras no haya cierre.</summary>
public sealed record IncidentSnapshot(
    Guid IncidentId,
    Guid ModuleId,
    Guid FloorId,
    string Status,
    Guid? CurrentCoordinatorUserId,
    DateTimeOffset? ClosedAtUtc);

public interface IIncidentReader
{
    Task<IncidentSnapshot?> GetByIdAsync(
        Guid incidentId,
        CancellationToken cancellationToken = default);
}
