namespace Univalle.Emergencias.Contracts.Common;

/// <summary>Rutas compartidas; los módulos agregan sus segmentos bajo ApiV1.</summary>
public static class ApiRoutes
{
    public const string ApiV1 = "api/v1";
    public const string SystemTime = ApiV1 + "/system/time";
    public const string SystemHealth = ApiV1 + "/system/health";
}

/// <summary>Rutas Shell comunes, independientes de títulos visibles.</summary>
public static class MobileRoutes
{
    public const string Home = "home";
}

/// <summary>Códigos estables que acompañan las respuestas ProblemDetails.</summary>
public static class ApiErrorCodes
{
    public const string InvalidInput = "invalid_input";
    public const string NotFound = "not_found";
    public const string DatabaseUnavailable = "database_unavailable";
    public const string Unexpected = "unexpected_error";
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
}
