using Univalle.Emergencias.Contracts.Incidents;

namespace Univalle.Emergencias.Api.Incidents;

/// <summary>Estado técnico del envío de la alerta inicial; el registro de entrega pertenece a JO-02.</summary>
public static class InitialAlertStates
{
    /// <summary>Hay destinatario y falta enviar (o reintentar).</summary>
    public const string Pending = "Pending";

    /// <summary>INotificationSender aceptó el evento.</summary>
    public const string Dispatched = "Dispatched";

    /// <summary>El último intento falló; se reintenta con el mismo EventId.</summary>
    public const string Failed = "Failed";

    /// <summary>No había encargado de turno; RF09 no añade otro escalamiento automático.</summary>
    public const string NoCoverage = "NoCoverage";
}

public sealed class Incident
{
    public Guid IncidentId { get; set; }
    public Guid ModuleId { get; set; }
    public Guid FloorId { get; set; }
    public IncidentType Type { get; set; }
    public string Status { get; set; } = IncidentStatuses.Active;
    public DateTimeOffset OpenedAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }

    /// <summary>Coordinador inicial por turno (JO-01). El relevo aceptado (JO-04) lo actualiza.</summary>
    public Guid? CurrentCoordinatorUserId { get; set; }

    /// <summary>Identidad persistente de la alerta inicial; los reintentos reutilizan este valor.</summary>
    public Guid InitialAlertEventId { get; set; }
    public Guid? InitialAlertRecipientUserId { get; set; }
    public string InitialAlertState { get; set; } = InitialAlertStates.Pending;
    public string? InitialAlertDeliveryStatus { get; set; }
    public int InitialAlertAttempts { get; set; }
    public DateTimeOffset? InitialAlertLastAttemptAtUtc { get; set; }

    /// <summary>Token de concurrencia optimista; se renueva en cada actualización del incidente.</summary>
    public Guid ConcurrencyStamp { get; set; }

    public List<IncidentReport> Reports { get; set; } = [];
}

/// <summary>Aporte de un testigo. Conserva sus propios datos, autor y momento (RF07).</summary>
public sealed class IncidentReport
{
    public Guid ReportId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid ReporterUserId { get; set; }
    public Guid ClientRequestId { get; set; }
    public IncidentType Type { get; set; }
    public Guid ModuleId { get; set; }
    public Guid FloorId { get; set; }
    public PreliminarySeverity PreliminarySeverity { get; set; }
    public string? Details { get; set; }
    public DateTimeOffset ReportedAtUtc { get; set; }

    /// <summary>true para el reporte que abrió el incidente.</summary>
    public bool IsInitial { get; set; }

    public Incident? Incident { get; set; }
}
