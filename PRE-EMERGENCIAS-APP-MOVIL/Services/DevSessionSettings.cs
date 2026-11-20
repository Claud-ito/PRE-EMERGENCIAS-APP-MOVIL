using Microsoft.Maui.Storage;
using Univalle.Emergencias.Contracts.Identity;

namespace PRE_EMERGENCIAS_APP_MOVIL.Services;

/// <summary>
/// SOLO DESARROLLO: identidad de ensayo enviada en cabeceras X-Dev-* mientras no exista el inicio de sesión de SA-01.
/// La API solo la acepta en el entorno Development. Se elimina al integrar la sesión real.
/// </summary>
public sealed class DevSessionSettings
{
    /// <summary>Coincide con IncidentDevSubstitutes__CoordinatorUserId de .env.mac.local (encargado de ensayo).</summary>
    public const string DefaultCoordinatorUserId = "5e550000-0000-4000-8000-00000000c001";

    private const string UserIdKey = "dev.session.user-id.v1";
    private const string RoleKey = "dev.session.role.v1";

    public Guid UserId
    {
        get
        {
            if (Guid.TryParse(Preferences.Default.Get(UserIdKey, string.Empty), out var userId) && userId != Guid.Empty)
            {
                return userId;
            }

            userId = Guid.NewGuid();
            Preferences.Default.Set(UserIdKey, userId.ToString());
            return userId;
        }
    }

    public UserRole Role =>
        Enum.TryParse<UserRole>(Preferences.Default.Get(RoleKey, string.Empty), out var role)
            ? role
            : UserRole.ComunidadTestigo;

    public void Set(Guid userId, UserRole role)
    {
        Preferences.Default.Set(UserIdKey, userId.ToString());
        Preferences.Default.Set(RoleKey, role.ToString());
    }

    public void UseNewWitness() => Set(Guid.NewGuid(), UserRole.ComunidadTestigo);

    public void UseTestCoordinator() => Set(Guid.Parse(DefaultCoordinatorUserId), UserRole.SySO);
}
