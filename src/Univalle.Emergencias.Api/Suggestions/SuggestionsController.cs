using Microsoft.AspNetCore.Mvc;
using Univalle.Emergencias.Api.Identity;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Incidents;
using Univalle.Emergencias.Contracts.Suggestions;

namespace Univalle.Emergencias.Api.Suggestions;

[ApiController]
[Route(SuggestionRoutes.Base)]
public sealed class SuggestionsController(
    IIncidentReader incidents,
    SuggestionService suggestions) : ControllerBase
{
    [HttpGet("incidents")]
    [RequirePermission(PermissionCodes.ReadSuggestions)]
    public async Task<IActionResult> ClosedIncidents(CancellationToken cancellationToken) =>
        Ok(await suggestions.ListClosedAsync(cancellationToken));

    [HttpGet("incidents/{incidentId:guid}")]
    [RequirePermission(PermissionCodes.ReadSuggestions)]
    public async Task<IActionResult> Thread(Guid incidentId, CancellationToken cancellationToken)
    {
        var incident = await incidents.GetByIdAsync(incidentId, cancellationToken);
        var error = ValidateClosed(incident);
        return error ?? Ok(await suggestions.ReadAsync(incident!, cancellationToken));
    }

    [HttpPost("incidents/{incidentId:guid}")]
    [RequirePermission(PermissionCodes.WriteSuggestions)]
    public async Task<IActionResult> Create(
        Guid incidentId, CreateSuggestionRequest request, CancellationToken cancellationToken)
    {
        var text = request.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1000)
            return Error(400, ApiErrorCodes.InvalidInput,
                "La sugerencia debe tener entre 1 y 1000 caracteres.");
        var incident = await incidents.GetByIdAsync(incidentId, cancellationToken);
        var error = ValidateClosed(incident);
        if (error is not null) return error;
        var actor = (AuthenticatedUser)HttpContext.Items[RequirePermissionAttribute.ActorKey]!;
        var result = await suggestions.WriteAsync(
            incident!, actor.UserId, text, cancellationToken);
        return result.ErrorCode switch
        {
            null => Created(SuggestionRoutes.Thread(incidentId), result.Value),
            ApiErrorCodes.Unauthenticated => Error(401, ApiErrorCodes.Unauthenticated,
                "La cuenta dejó de estar activa."),
            _ => Error(409, SuggestionErrorCodes.WindowClosed,
                "El plazo de seis horas para comentar terminó.")
        };
    }

    private ObjectResult? ValidateClosed(IncidentSnapshot? incident)
    {
        if (incident is null)
            return Error(404, ApiErrorCodes.NotFound, "El incidente no existe.");
        if (incident.Status != IncidentStatuses.Closed || incident.ClosedAtUtc is null)
            return Error(409, SuggestionErrorCodes.IncidentNotClosed,
                "El buzón se abre después del cierre formal del incidente.");
        return null;
    }

    private ObjectResult Error(int status, string code, string title)
    {
        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;
        return StatusCode(status, problem);
    }
}
