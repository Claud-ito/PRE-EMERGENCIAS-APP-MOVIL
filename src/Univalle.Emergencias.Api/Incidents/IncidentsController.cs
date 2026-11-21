using Microsoft.AspNetCore.Mvc;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Incidents;

namespace Univalle.Emergencias.Api.Incidents;

/// <summary>
/// Reporte (RF07), unión de aportes (RF08) y seguimiento mínimo del coordinador.
/// La autorización usa IIdentityContext de SA-01 y los permisos comunes en cada solicitud.
/// </summary>
[ApiController]
[Route(IncidentRoutes.Base)]
public sealed class IncidentsController(
    IIdentityContext identity,
    IncidentReportService reports,
    IncidentQueries queries) : ControllerBase
{
    [HttpGet("report-options")]
    [ProducesResponseType(typeof(ReportOptionsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReportOptions(CancellationToken cancellationToken)
    {
        var (_, denied) = await AuthorizeAsync(PermissionCodes.ReportIncident, cancellationToken);
        return denied ?? Ok(await queries.GetReportOptionsAsync(cancellationToken));
    }

    [HttpPost("reports")]
    [ProducesResponseType(typeof(SubmitIncidentReportResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(SubmitIncidentReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SubmitReport(
        [FromBody] SubmitIncidentReportRequest request,
        CancellationToken cancellationToken)
    {
        var (user, denied) = await AuthorizeAsync(PermissionCodes.ReportIncident, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var result = await reports.SubmitAsync(user!.UserId, request, cancellationToken);
        switch (result)
        {
            case SubmitReportResult.Accepted accepted:
                return StatusCode(StatusCodes.Status201Created, accepted.Response);

            case SubmitReportResult.Invalid invalid:
                var validation = new ValidationProblemDetails(invalid.Errors.ToDictionary())
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Faltan datos obligatorios del reporte."
                };
                validation.Extensions["code"] = ApiErrorCodes.InvalidInput;
                return BadRequest(validation);

            case SubmitReportResult.LocationUnavailable:
                return CodedProblem(
                    StatusCodes.Status422UnprocessableEntity,
                    "La ubicación no está disponible para reportes.",
                    "El módulo o el piso no existe, está inactivo o el piso no pertenece al módulo.",
                    IncidentErrorCodes.LocationUnavailable);

            default:
                throw new InvalidOperationException($"Resultado no contemplado: {result.GetType().Name}");
        }
    }

    [HttpGet("assigned")]
    [ProducesResponseType(typeof(IReadOnlyList<CoordinatedIncidentView>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAssigned(CancellationToken cancellationToken)
    {
        var (user, denied) = await AuthorizeAsync(PermissionCodes.CoordinateAssignedIncident, cancellationToken);
        return denied ?? Ok(await queries.GetAssignedAsync(user!.UserId, cancellationToken));
    }

    [HttpGet("{incidentId:guid}/reports")]
    [ProducesResponseType(typeof(CoordinatedIncidentView), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetIncidentReports(Guid incidentId, CancellationToken cancellationToken)
    {
        var (user, denied) = await AuthorizeAsync(PermissionCodes.CoordinateAssignedIncident, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        return await queries.GetForCoordinatorAsync(incidentId, user!.UserId, cancellationToken) switch
        {
            CoordinatorQueryResult.Found found => Ok(found.Incident),
            CoordinatorQueryResult.NotFound => CodedProblem(
                StatusCodes.Status404NotFound,
                "El incidente no existe.",
                null,
                ApiErrorCodes.NotFound),
            _ => CodedProblem(
                StatusCodes.Status403Forbidden,
                "Solo el coordinador vigente puede consultar los aportes.",
                null,
                IncidentErrorCodes.NotIncidentCoordinator)
        };
    }

    private async Task<(AuthenticatedUser? User, IActionResult? Denied)> AuthorizeAsync(
        string permission,
        CancellationToken cancellationToken)
    {
        var user = await identity.GetCurrentAsync(cancellationToken);
        if (user is null || !user.IsActive)
        {
            return (null, CodedProblem(
                StatusCodes.Status401Unauthorized,
                "Se requiere una sesión activa.",
                null,
                ApiErrorCodes.Unauthenticated));
        }

        if (!user.Permissions.Contains(permission))
        {
            return (null, CodedProblem(
                StatusCodes.Status403Forbidden,
                "La cuenta no tiene permiso para esta operación.",
                null,
                ApiErrorCodes.Forbidden));
        }

        return (user, null);
    }

    private ObjectResult CodedProblem(int status, string title, string? detail, string code)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail };
        problem.Extensions["code"] = code;
        return StatusCode(status, problem);
    }
}
