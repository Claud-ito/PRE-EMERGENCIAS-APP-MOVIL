
using Microsoft.AspNetCore.Mvc;
using Univalle.Emergencias.Api.Services;
using Univalle.Emergencias.Contracts.Turns;

namespace Univalle.Emergencias.Api.Controllers;

[Univalle.Emergencias.Api.Identity.RequirePermission(Univalle.Emergencias.Contracts.Identity.PermissionCodes.ConfigureOwnAvailability)]
[ApiController]
[Route("api/v1/turns")]
public sealed class TurnsController : ControllerBase
{
    private readonly ITurnResolver _turnResolver;
    private readonly ScheduleManagementService _management;

    public TurnsController(
        ITurnResolver turnResolver,
        ScheduleManagementService management)
    {
        _turnResolver = turnResolver;
        _management = management;
    }

    [HttpGet("calendar")]
    public async Task<IActionResult> Calendar(DateOnly date, CancellationToken ct)
    {
        if (date == default || date == DateOnly.MaxValue) return BadRequest();
        return Ok(await _management.CalendarAsync(date, ct));
    }

    // Consultar cobertura para un instante UTC.
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

    // Consultar los horarios del usuario autenticado.
    [HttpGet("mine")]
    public async Task<IActionResult> GetMySchedules(
        CancellationToken cancellationToken)
    {
        try
        {
            var schedules = await _management
                .GetMySchedulesAsync(cancellationToken);

            return Ok(schedules);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    code = "AVAILABILITY_ACCESS_DENIED",
                    message = ex.Message
                });
        }
    }

    // Registrar un horario del usuario autenticado.
    [HttpPost]
    public async Task<IActionResult> CreateSchedule(
        [FromBody] SaveWorkScheduleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var schedule = await _management
                .CreateScheduleAsync(
                    request,
                    cancellationToken);

            return StatusCode(
                StatusCodes.Status201Created,
                schedule);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new
                {
                    code = "AVAILABILITY_ACCESS_DENIED",
                    message = ex.Message
                });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                code = "INVALID_SCHEDULE",
                message = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                code = "SCHEDULE_OVERLAP",
                message = ex.Message
            });
        }
    }

    // EDITAR HORARIO
    [HttpPut("{scheduleId:guid}")]
    public async Task<IActionResult> UpdateSchedule(
        Guid scheduleId,
        [FromBody] SaveWorkScheduleRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _management.UpdateScheduleAsync(
                scheduleId, request, cancellationToken);

            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, new
            {
                code = "AVAILABILITY_ACCESS_DENIED"
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { code = "SCHEDULE_NOT_FOUND" });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                code = "INVALID_SCHEDULE",
                message = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                code = "SCHEDULE_OVERLAP",
                message = ex.Message
            });
        }
    }

    // DESACTIVAR HORARIO
    [HttpDelete("{scheduleId:guid}")]
    public async Task<IActionResult> DeactivateSchedule(
        Guid scheduleId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _management.DeactivateScheduleAsync(
                scheduleId, cancellationToken);

            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, new
            {
                code = "AVAILABILITY_ACCESS_DENIED"
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { code = "SCHEDULE_NOT_FOUND" });
        }
    }

    // CONSULTAR EXCEPCIONES
    [HttpGet("exceptions/mine")]
    public async Task<IActionResult> GetMyExceptions(
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _management
                .GetMyExceptionsAsync(cancellationToken);

            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, new
            {
                code = "AVAILABILITY_ACCESS_DENIED"
            });
        }
    }

    [HttpPut("exceptions/{id:guid}")]
    public async Task<IActionResult> EditException(Guid id, CreateScheduleExceptionRequest request, CancellationToken ct)
    {
        try
        {
            return Ok(await _management.CreateExceptionAsync(request.Date, request.IsAvailable,
                request.StartTime, request.EndTime, request.Reason, ct, id));
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (ArgumentException ex) { return BadRequest(ex.Message); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    [HttpDelete("exceptions/{id:guid}")]
    public async Task<IActionResult> RemoveException(Guid id, CancellationToken ct)
    {
        try { await _management.RemoveExceptionAsync(id, ct); return NoContent(); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
    }

    // REGISTRAR EXCEPCION
    [HttpPost("exceptions")]
    public async Task<IActionResult> CreateException(
        [FromBody] CreateScheduleExceptionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _management.CreateExceptionAsync(
                request.Date,
                request.IsAvailable,
                request.StartTime,
                request.EndTime,
                request.Reason,
                cancellationToken);

            return StatusCode(201, result);
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(403, new
            {
                code = "AVAILABILITY_ACCESS_DENIED"
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                code = "INVALID_EXCEPTION",
                message = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new
            {
                code = "EXCEPTION_ALREADY_EXISTS",
                message = ex.Message
            });
        }
    }

}
