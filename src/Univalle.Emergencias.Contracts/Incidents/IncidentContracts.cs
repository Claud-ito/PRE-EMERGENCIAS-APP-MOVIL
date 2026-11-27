namespace Univalle.Emergencias.Contracts.Incidents;

/// <summary>Estados del incidente. RF19 (Sprint 3) produce el cierre real; en Sprint 2 se usan cierres de ensayo.</summary>
public static class IncidentStatuses
{
    public const string Active = "Active";
    public const string Closed = "Closed";
}

/// <summary>
/// Lectura mínima que publica Alejandro (AL-01).
/// OpenedAtUtc es el instante de la alerta inicial (inicio del plazo AUTO de RF09) e InitialAlertEventId su identidad persistente.
/// ClosedAtUtc es null mientras no haya cierre.
/// </summary>
public sealed record IncidentSnapshot(
    Guid IncidentId,
    Guid ModuleId,
    Guid FloorId,
    string Status,
    Guid? CurrentCoordinatorUserId,
    DateTimeOffset OpenedAtUtc,
    Guid InitialAlertEventId,
    DateTimeOffset? ClosedAtUtc);

public interface IIncidentReader
{
    Task<IncidentSnapshot?> GetByIdAsync(
        Guid incidentId,
        CancellationToken cancellationToken = default);
}

/// <summary>Alcance del sistema: incendio y primeros auxilios.</summary>
public enum IncidentType
{
    Incendio,
    PrimerosAuxilios
}

/// <summary>Gravedad que estima el testigo. No sustituye la evaluación operativa de RF12.</summary>
public enum PreliminarySeverity
{
    Leve,
    Grave,
    MuyGrave
}

/// <summary>Tipo de aviso que el módulo de incidentes entrega a INotificationSender.</summary>
public static class IncidentNotificationTypes
{
    public const string InitialAlert = "incident.initial-alert";
}

public static class IncidentRoutes
{
    public const string Base = Common.ApiRoutes.ApiV1 + "/incidents";
    public const string Reports = Base + "/reports";
    public const string ReportOptions = Base + "/report-options";
    public const string Assigned = Base + "/assigned";

    public static string IncidentReports(Guid incidentId) => $"{Base}/{incidentId}/reports";
}

/// <summary>Rutas Shell del módulo de incidentes (app móvil).</summary>
public static class IncidentMobileRoutes
{
    public const string Report = "report-incident";
    public const string Coordinated = "coordinated-incidents";
}

public static class IncidentErrorCodes
{
    /// <summary>El módulo o piso no existe, está inactivo o el piso no pertenece al módulo.</summary>
    public const string LocationUnavailable = "location_unavailable";

    /// <summary>La operación exige ser el coordinador vigente del incidente.</summary>
    public const string NotIncidentCoordinator = "not_incident_coordinator";
}

/// <summary>
/// Reporte enviado por el testigo. Los campos son anulables para señalar cuáles faltan.
/// ClientRequestId lo genera la app una vez por formulario: un reenvío repetido no crea otro aporte.
/// </summary>
public sealed record SubmitIncidentReportRequest(
    Guid? ClientRequestId,
    IncidentType? Type,
    Guid? ModuleId,
    Guid? FloorId,
    PreliminarySeverity? PreliminarySeverity,
    string? Details);

/// <summary>IncidentCreated indica si el reporte abrió el caso o se unió como aporte a uno activo.</summary>
public sealed record SubmitIncidentReportResponse(
    Guid IncidentId,
    Guid ReportId,
    bool IncidentCreated,
    DateTimeOffset ReportedAtUtc);

public sealed record ReportFloorOption(Guid FloorId, string Name);

public sealed record ReportModuleOption(
    Guid ModuleId,
    string Code,
    string Name,
    IReadOnlyList<ReportFloorOption> Floors);

public sealed record ReportOptionsResponse(IReadOnlyList<ReportModuleOption> Modules);

public sealed record IncidentReportView(
    Guid ReportId,
    Guid ReporterUserId,
    IncidentType Type,
    PreliminarySeverity PreliminarySeverity,
    string? Details,
    DateTimeOffset ReportedAtUtc,
    bool IsInitial,
    string? ReporterName = null,
    string? ReporterUsername = null);

/// <summary>
/// Seguimiento mínimo del coordinador (AL-02). La reevaluación y órdenes completas son RF12, Sprint 3.
/// AutoDueAtUtc es el plazo AUTO pendiente (null si no corre). CalloutAtUtc y CalloutChannel describen la
/// convocatoria al líder ya registrada (Manual o AUTO), si existe.
/// </summary>
public sealed record CoordinatedIncidentView(
    Guid IncidentId,
    Guid ModuleId,
    string ModuleName,
    Guid FloorId,
    string FloorName,
    IncidentType Type,
    string Status,
    DateTimeOffset OpenedAtUtc,
    int ReportCount,
    IReadOnlyList<IncidentReportView> Reports,
    DateTimeOffset? AutoDueAtUtc = null,
    DateTimeOffset? CalloutAtUtc = null,
    string? CalloutChannel = null);
