using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Locations;
using Univalle.Emergencias.Contracts.Notifications;
using Univalle.Emergencias.Contracts.Turns;

namespace Univalle.Emergencias.Api.Incidents.Development;

// SUSTITUCIONES SOLO PARA DESARROLLO (reparto, sección 2): permiten probar RF07/RF08 antes de integrar
// SA-01 (identidad), DI-01 (ubicaciones), JO-01 (turno) y JO-02 (avisos). Se registran con TryAdd y solo
// en el entorno Development; la entrega integrada usa las implementaciones reales de cada dueño.

/// <summary>
/// Identidad de ensayo leída de las cabeceras X-Dev-User-Id y X-Dev-Role. Sustituye a SA-01 mientras no exista inicio de sesión.
/// </summary>
public sealed class DevHeaderIdentityContext(IHttpContextAccessor accessor) : IIdentityContext
{
    public const string UserIdHeader = "X-Dev-User-Id";
    public const string RoleHeader = "X-Dev-Role";

    public Task<AuthenticatedUser?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var headers = accessor.HttpContext?.Request.Headers;
        if (headers is null
            || !Guid.TryParse(headers[UserIdHeader].ToString(), out var userId)
            || userId == Guid.Empty)
        {
            return Task.FromResult<AuthenticatedUser?>(null);
        }

        var role = Enum.TryParse<UserRole>(headers[RoleHeader].ToString(), ignoreCase: true, out var parsed)
            ? parsed
            : UserRole.ComunidadTestigo;

        return Task.FromResult<AuthenticatedUser?>(new AuthenticatedUser(userId, true, role, PermissionsFor(role)));
    }

    /// <summary>Solo los permisos que usa el módulo de incidentes. La matriz real la define SA-01.</summary>
    private static IReadOnlySet<string> PermissionsFor(UserRole role)
    {
        var permissions = new HashSet<string>
        {
            PermissionCodes.ReportIncident,
            PermissionCodes.ReceiveNotifications
        };
        if (role is UserRole.SySO or UserRole.Mantenimiento)
        {
            permissions.Add(PermissionCodes.CoordinateAssignedIncident);
        }

        return permissions;
    }
}

/// <summary>Catálogo de ensayo con datos de prueba fijos. Sustituye a DI-01. Los nombres no son ubicaciones reales.</summary>
public sealed class DevLocationCatalog : ILocationCatalog
{
    public static readonly Guid ModuleA = Guid.Parse("0b5c2a10-0000-4000-8000-0000000000a1");
    public static readonly Guid ModuleB = Guid.Parse("0b5c2a10-0000-4000-8000-0000000000b1");
    public static readonly Guid ModuleAFloor0 = Guid.Parse("0b5c2a10-0000-4000-8000-0000000000a2");
    public static readonly Guid ModuleAFloor1 = Guid.Parse("0b5c2a10-0000-4000-8000-0000000000a3");
    public static readonly Guid ModuleAFloor2Inactive = Guid.Parse("0b5c2a10-0000-4000-8000-0000000000a4");
    public static readonly Guid ModuleBFloor0 = Guid.Parse("0b5c2a10-0000-4000-8000-0000000000b2");

    private static readonly ModuleLocation[] Modules =
    [
        new(ModuleA, "PRUEBA-A", "Módulo de prueba A", true),
        new(ModuleB, "PRUEBA-B", "Módulo de prueba B", true)
    ];

    private static readonly FloorLocation[] Floors =
    [
        new(ModuleAFloor0, ModuleA, "Planta baja (prueba)", true),
        new(ModuleAFloor1, ModuleA, "Piso 1 (prueba)", true),
        new(ModuleAFloor2Inactive, ModuleA, "Piso 2 (prueba, inactivo)", false),
        new(ModuleBFloor0, ModuleB, "Planta baja (prueba)", true)
    ];

    public Task<IReadOnlyList<ModuleLocation>> GetActiveModulesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ModuleLocation>>(Modules.Where(module => module.IsActive).ToList());

    public Task<IReadOnlyList<FloorLocation>> GetActiveFloorsAsync(
        Guid moduleId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FloorLocation>>(
            Floors.Where(floor => floor.ModuleId == moduleId && floor.IsActive).ToList());

    public Task<ModuleLocation?> GetModuleAsync(Guid moduleId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Modules.SingleOrDefault(module => module.ModuleId == moduleId));

    public Task<FloorLocation?> GetFloorAsync(Guid floorId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Floors.SingleOrDefault(floor => floor.FloorId == floorId));

    public Task<bool> IsActiveFloorInModuleAsync(
        Guid moduleId,
        Guid floorId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            Modules.Any(module => module.ModuleId == moduleId && module.IsActive)
            && Floors.Any(floor => floor.FloorId == floorId && floor.ModuleId == moduleId && floor.IsActive));
}

/// <summary>
/// Turno de ensayo: devuelve el encargado configurado en IncidentDevSubstitutes:CoordinatorUserId,
/// o ausencia de cobertura si no se configura. Sustituye a JO-01.
/// </summary>
public sealed class DevTurnResolver(IConfiguration configuration) : ITurnResolver
{
    public Task<TurnCoverage?> ResolveAsync(DateTimeOffset instantUtc, CancellationToken cancellationToken = default)
    {
        var configured = configuration["IncidentDevSubstitutes:CoordinatorUserId"];
        return Task.FromResult(
            Guid.TryParse(configured, out var coordinatorId) && coordinatorId != Guid.Empty
                ? new TurnCoverage(coordinatorId, instantUtc.AddHours(-1), instantUtc.AddHours(1))
                : null);
    }
}

/// <summary>Emisor de ensayo que solo registra en el log. Sustituye a JO-02; no entrega nada al dispositivo.</summary>
public sealed class LoggingNotificationSender(
    IServerClock clock,
    ILogger<LoggingNotificationSender> logger) : INotificationSender
{
    public const string SimulatedStatus = "SimulatedNotDelivered";

    public Task<NotificationResult> RegisterAndSendAsync(
        NotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "[DEV] Aviso {Type} {EventId} del incidente {IncidentId} para {RecipientUserId}.",
            request.Type,
            request.EventId,
            request.IncidentId,
            request.RecipientUserId);

        return Task.FromResult(new NotificationResult(
            request.EventId,
            request.IncidentId,
            request.Type,
            request.RecipientUserId,
            clock.UtcNow,
            SimulatedStatus));
    }
}
