
using Microsoft.AspNetCore.Mvc;
using Univalle.Emergencias.Contracts.Turns;

namespace Univalle.Emergencias.Api.Controllers;

[ApiController]
[Route("api/v1/turns")]
public sealed class TurnsController : ControllerBase
{
    private readonly ITurnResolver _turnResolver;

    public TurnsController(ITurnResolver turnResolver)
    {
        _turnResolver = turnResolver;
    }

    [HttpGet("coverage")]
    public async Task<IActionResult> GetCoverage(
        [FromQuery] DateTimeOffset? atUtc,
        CancellationToken cancellationToken)
    {
        if (atUtc is null)
        {
            return BadRequest(new
            {
                code = "MISSING_DATE",
                message = "Debe proporcionar la fecha UTC."
            });
        }

        if (atUtc.Value.Offset != TimeSpan.Zero)
        {
            return BadRequest(new
            {
                code = "INVALID_TIMEZONE",
                message = "La fecha debe estar expresada en UTC."
            });
        }

        var coverage = await _turnResolver.ResolveAsync(
            atUtc.Value,
            cancellationToken);

        if (coverage is null)
        {
            return Ok(new
            {
                hasCoverage = false,
                coordinatorUserId = (Guid?)null
            });
        }

        return Ok(new
        {
            hasCoverage = true,
            coordinatorUserId = coverage.CoordinatorUserId,
            startsAtUtc = coverage.StartsAtUtc,
            endsAtUtc = coverage.EndsAtUtc
        });
    }
}
