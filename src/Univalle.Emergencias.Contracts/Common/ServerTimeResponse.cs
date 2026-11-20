namespace Univalle.Emergencias.Contracts.Common;

/// <summary>Hora UTC observada en la API.</summary>
public sealed record ServerTimeResponse(DateTimeOffset UtcNow);
