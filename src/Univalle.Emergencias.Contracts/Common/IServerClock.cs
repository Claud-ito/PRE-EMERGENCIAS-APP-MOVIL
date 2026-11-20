namespace Univalle.Emergencias.Contracts.Common;

/// <summary>Tiempo UTC del servidor; se sustituye por un reloj controlado en pruebas.</summary>
public interface IServerClock
{
    DateTimeOffset UtcNow { get; }
}
