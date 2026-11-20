namespace Univalle.Emergencias.Contracts.Locations;

public sealed record ModuleLocation(
    Guid ModuleId,
    string Code,
    string Name,
    bool IsActive);

public sealed record FloorLocation(
    Guid FloorId,
    Guid ModuleId,
    string Name,
    bool IsActive);

/// <summary>Diana implementa este catálogo; las consultas por ID también permiten leer historial inactivo.</summary>
public interface ILocationCatalog
{
    Task<IReadOnlyList<ModuleLocation>> GetActiveModulesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FloorLocation>> GetActiveFloorsAsync(
        Guid moduleId,
        CancellationToken cancellationToken = default);

    Task<ModuleLocation?> GetModuleAsync(
        Guid moduleId,
        CancellationToken cancellationToken = default);

    Task<FloorLocation?> GetFloorAsync(
        Guid floorId,
        CancellationToken cancellationToken = default);

    Task<bool> IsActiveFloorInModuleAsync(
        Guid moduleId,
        Guid floorId,
        CancellationToken cancellationToken = default);
}
