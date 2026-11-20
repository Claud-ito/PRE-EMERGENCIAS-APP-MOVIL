using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Common;

namespace Univalle.Emergencias.Api.Controllers;

[ApiController]
[Route(ApiRoutes.ApiV1 + "/system")]
public sealed class SystemController(
    IServerClock clock,
    AppDbContext db,
    ILogger<SystemController> logger) : ControllerBase
{
    [HttpGet("time")]
    [ProducesResponseType(typeof(ServerTimeResponse), StatusCodes.Status200OK)]
    public ActionResult<ServerTimeResponse> GetTime() =>
        Ok(new ServerTimeResponse(clock.UtcNow));

    [HttpGet("health")]
    [ProducesResponseType(typeof(SystemHealthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetHealth(CancellationToken cancellationToken)
    {
        try
        {
            if (await db.Database.CanConnectAsync(cancellationToken))
            {
                await db.AuditEntries.AsNoTracking().AnyAsync(cancellationToken);
                return Ok(new SystemHealthResponse("ready", clock.UtcNow));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo consultar la base de datos de desarrollo.");
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "La base de datos no está disponible.",
            Detail = "Revise la conexión SQL Server y aplique las migraciones."
        };
        problem.Extensions["code"] = ApiErrorCodes.DatabaseUnavailable;
        return StatusCode(StatusCodes.Status503ServiceUnavailable, problem);
    }
}

public sealed record SystemHealthResponse(string Status, DateTimeOffset UtcNow);
