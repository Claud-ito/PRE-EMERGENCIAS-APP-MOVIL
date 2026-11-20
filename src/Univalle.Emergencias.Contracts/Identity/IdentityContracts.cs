namespace Univalle.Emergencias.Contracts.Identity;

/// <summary>Roles operativos; Encargado es un actor abstracto y no una cuenta adicional.</summary>
public enum UserRole
{
    ComunidadTestigo,
    SySO,
    Mantenimiento,
    LiderBrigada,
    Brigadista,
    Medico
}

/// <summary>Permisos base. SA-01 define qué permisos se conceden y valida cada operación.</summary>
public static class PermissionCodes
{
    public const string ReportIncident = "incident.report";
    public const string CoordinateAssignedIncident = "incident.coordinate-assigned";
    public const string ManageUsers = "users.manage";
    public const string ManageLocations = "locations.manage";
    public const string ManageBrigades = "brigades.manage";
    public const string ManageSupportContacts = "support-contacts.manage";
    public const string ConfigureOwnAvailability = "availability.configure-own";
    public const string ReceiveNotifications = "notifications.receive";
    public const string RecordInjury = "injuries.record";
    public const string ReadSuggestions = "suggestions.read";
    public const string WriteSuggestions = "suggestions.write";
}

/// <summary>Identidad resuelta por el servidor. IsActive debe consultarse para cada solicitud protegida.</summary>
public sealed record AuthenticatedUser(
    Guid UserId,
    bool IsActive,
    UserRole Role,
    IReadOnlySet<string> Permissions);

/// <summary>Lee la identidad de la solicitud; null indica ausencia de autenticación.</summary>
public interface IIdentityContext
{
    Task<AuthenticatedUser?> GetCurrentAsync(CancellationToken cancellationToken = default);
}
