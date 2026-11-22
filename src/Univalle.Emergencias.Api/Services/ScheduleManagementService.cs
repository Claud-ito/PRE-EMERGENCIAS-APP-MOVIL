
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Models;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Turns;

namespace Univalle.Emergencias.Api.Services;

public sealed class ScheduleManagementService
{
    private readonly AppDbContext _context;
    private readonly IIdentityContext _identity;
    private readonly IAuditTrail _audit;

    public ScheduleManagementService(
        AppDbContext context,
        IIdentityContext identity,
        IAuditTrail audit)
    {
        _context = context;
        _identity = identity;
        _audit = audit;
    }

    // Verificar que el usuario pueda administrar
    // su propia disponibilidad.
    private async Task<AuthenticatedUser> GetAuthorizedUserAsync(
        CancellationToken cancellationToken)
    {
        var user = await _identity.GetCurrentAsync(
            cancellationToken);

        if (user is null || !user.IsActive)
        {
            throw new UnauthorizedAccessException(
                "Debe iniciar sesion con una cuenta activa.");
        }

        if (user.Role != UserRole.SySO &&
            user.Role != UserRole.Mantenimiento)
        {
            throw new UnauthorizedAccessException(
                "Su rol no puede configurar turnos.");
        }

        if (!user.Permissions.Contains(
            PermissionCodes.ConfigureOwnAvailability))
        {
            throw new UnauthorizedAccessException(
                "No tiene permiso para configurar horarios.");
        }

        return user;
    }

    // Consultar los horarios del usuario autenticado.
    public async Task<List<WorkScheduleDto>> GetMySchedulesAsync(
        CancellationToken cancellationToken = default)
    {
        var user = await GetAuthorizedUserAsync(
            cancellationToken);

        return await _context.WorkSchedules
            .AsNoTracking()
            .Where(x =>
                x.CoordinatorUserId == user.UserId &&
                x.IsActive)
            .OrderBy(x => x.DayOfWeek)
            .ThenBy(x => x.StartTime)
            .Select(x => new WorkScheduleDto(
                x.WorkScheduleId,
                x.CoordinatorUserId,
                x.DayOfWeek,
                x.StartTime,
                x.EndTime,
                x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<CalendarInterval>> CalendarAsync(DateOnly date, CancellationToken ct)
    {
        var schedules = await _context.WorkSchedules.AsNoTracking()
            .Where(x => x.IsActive && x.DayOfWeek == date.DayOfWeek).ToListAsync(ct);
        var exceptions = await _context.ScheduleExceptions.AsNoTracking()
            .Where(x => x.IsActive && x.Date == date).ToListAsync(ct);
        return TurnCalendar.Intervals(date, TurnCalendar.Coverage(date, schedules, exceptions));
    }

    private async Task CheckDatedOverlapsAsync(WorkSchedule changed, CancellationToken ct)
    {
        var exceptions = await _context.ScheduleExceptions.AsNoTracking()
            .Where(x => x.IsActive).ToListAsync(ct);
        var schedules = await _context.WorkSchedules.AsNoTracking()
            .Where(x => x.IsActive && x.DayOfWeek == changed.DayOfWeek &&
                x.WorkScheduleId != changed.WorkScheduleId).ToListAsync(ct);
        schedules.Add(changed);
        foreach (var date in exceptions.Select(x => x.Date).Distinct().Where(x => x.DayOfWeek == changed.DayOfWeek))
            CheckCoverage(date, schedules, exceptions);
    }

    private static void CheckCoverage(DateOnly date, List<WorkSchedule> schedules, List<ScheduleException> exceptions)
    {
        var windows = TurnCalendar.Coverage(date, schedules, exceptions);
        if (windows.Zip(windows.Skip(1)).Any(pair => pair.First.EndsAtUtc > pair.Second.StartsAtUtc))
            throw new InvalidOperationException("La excepción se superpone con otro turno.");
    }

    public async Task RemoveExceptionAsync(Guid id, CancellationToken ct)
    {
        var user = await GetAuthorizedUserAsync(ct);
        await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var previous = await _context.ScheduleExceptions.SingleOrDefaultAsync(x =>
            x.ScheduleExceptionId == id && x.IsActive && x.CoordinatorUserId == user.UserId, ct)
            ?? throw new KeyNotFoundException();
        var exceptions = await _context.ScheduleExceptions.AsNoTracking().Where(x =>
            x.Date == previous.Date && x.IsActive && x.ScheduleExceptionId != id).ToListAsync(ct);
        var schedules = await _context.WorkSchedules.AsNoTracking().Where(x =>
            x.IsActive && x.DayOfWeek == previous.Date.DayOfWeek).ToListAsync(ct);
        CheckCoverage(previous.Date, schedules, exceptions);
        previous.IsActive = false;
        await RecordAsync(user.UserId, "schedule.exception.deactivate",
            "ScheduleException", previous.ScheduleExceptionId, ct);
        await tx.CommitAsync(ct);
    }

    // Registrar un horario nuevo.
    public async Task<WorkScheduleDto> CreateScheduleAsync(
        SaveWorkScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await GetAuthorizedUserAsync(
            cancellationToken);

        // Validar que el dia sea correcto.
        if (!Enum.IsDefined(request.DayOfWeek))
        {
            throw new ArgumentException(
                "El dia de la semana no es valido.");
        }

        // Validar las horas.
        var error = ScheduleValidator.ValidateHours(
            request.StartTime,
            request.EndTime);

        if (error is not null)
        {
            throw new ArgumentException(error);
        }

        // Dias correspondientes a cada rol.
        bool validDay = user.Role switch
        {
            UserRole.SySO =>
                request.DayOfWeek is
                    DayOfWeek.Monday or
                    DayOfWeek.Wednesday or
                    DayOfWeek.Friday,

            UserRole.Mantenimiento =>
                request.DayOfWeek is
                    DayOfWeek.Tuesday or
                    DayOfWeek.Thursday or
                    DayOfWeek.Saturday or
                    DayOfWeek.Sunday,

            _ => false
        };

        if (!validDay)
        {
            throw new ArgumentException(
                "El dia no corresponde al rol del encargado.");
        }

        await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        // Consultar horarios existentes del mismo dia.
        var existingSchedules = await _context.WorkSchedules
            .AsNoTracking()
            .Where(x =>
                x.IsActive &&
                x.DayOfWeek == request.DayOfWeek)
            .ToListAsync(cancellationToken);

        // Evitar solapamientos.
        if (ScheduleValidator.HasOverlap(
            existingSchedules,
            request.DayOfWeek,
            request.StartTime,
            request.EndTime))
        {
            throw new InvalidOperationException(
                "El horario se superpone con otro existente.");
        }

        var schedule = new WorkSchedule
        {
            WorkScheduleId = Guid.NewGuid(),
            CoordinatorUserId = user.UserId,
            DayOfWeek = request.DayOfWeek,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            IsActive = true
        };

        await CheckDatedOverlapsAsync(schedule, cancellationToken);
        _context.WorkSchedules.Add(schedule);

        await RecordAsync(user.UserId, "schedule.create", "WorkSchedule",
            schedule.WorkScheduleId, cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return new WorkScheduleDto(
            schedule.WorkScheduleId,
            schedule.CoordinatorUserId,
            schedule.DayOfWeek,
            schedule.StartTime,
            schedule.EndTime,
            schedule.IsActive);
    }

    // ==========================================
    // EDITAR UN HORARIO
    // ==========================================

    public async Task<WorkScheduleDto> UpdateScheduleAsync(
        Guid scheduleId,
        SaveWorkScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await GetAuthorizedUserAsync(cancellationToken);

        await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var schedule = await _context.WorkSchedules
            .FirstOrDefaultAsync(x =>
                x.WorkScheduleId == scheduleId &&
                x.CoordinatorUserId == user.UserId &&
                x.IsActive,
                cancellationToken);

        if (schedule is null) throw new KeyNotFoundException("No se encontro el horario.");

        if (!Enum.IsDefined(request.DayOfWeek))
            throw new ArgumentException("Dia invalido.");

        var validDay = user.Role switch
        {
            UserRole.SySO => request.DayOfWeek is
                DayOfWeek.Monday or
                DayOfWeek.Wednesday or
                DayOfWeek.Friday,

            UserRole.Mantenimiento => request.DayOfWeek is
                DayOfWeek.Tuesday or
                DayOfWeek.Thursday or
                DayOfWeek.Saturday or
                DayOfWeek.Sunday,

            _ => false
        };

        if (!validDay)
            throw new ArgumentException(
                "El dia no corresponde al rol.");

        var error = ScheduleValidator.ValidateHours(
            request.StartTime,
            request.EndTime);

        if (error is not null)
            throw new ArgumentException(error);

        var existing = await _context.WorkSchedules
            .AsNoTracking()
            .Where(x =>
                x.IsActive &&
                x.DayOfWeek == request.DayOfWeek)
            .ToListAsync(cancellationToken);

        if (ScheduleValidator.HasOverlap(
            existing,
            request.DayOfWeek,
            request.StartTime,
            request.EndTime,
            scheduleId))
        {
            throw new InvalidOperationException(
                "El horario se superpone con otro existente.");
        }

        schedule.DayOfWeek = request.DayOfWeek;
        schedule.StartTime = request.StartTime;
        schedule.EndTime = request.EndTime;
        await CheckDatedOverlapsAsync(schedule, cancellationToken);
        await RecordAsync(user.UserId, "schedule.update", "WorkSchedule",
            schedule.WorkScheduleId, cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return new WorkScheduleDto(
            schedule.WorkScheduleId,
            schedule.CoordinatorUserId,
            schedule.DayOfWeek,
            schedule.StartTime,
            schedule.EndTime,
            schedule.IsActive);
    }

    // ==========================================
    // DESACTIVAR UN HORARIO
    // ==========================================

    public async Task DeactivateScheduleAsync(
        Guid scheduleId,
        CancellationToken cancellationToken = default)
    {
        var user = await GetAuthorizedUserAsync(cancellationToken);

        var schedule = await _context.WorkSchedules
            .FirstOrDefaultAsync(x =>
                x.WorkScheduleId == scheduleId &&
                x.CoordinatorUserId == user.UserId &&
                x.IsActive,
                cancellationToken);

        if (schedule is null)
            throw new KeyNotFoundException(
                "No se encontro el horario.");

        // Desactivacion logica: no eliminamos el registro.
        schedule.IsActive = false;

        await RecordAsync(user.UserId, "schedule.deactivate", "WorkSchedule",
            schedule.WorkScheduleId, cancellationToken);
    }

    // ==========================================
    // CONSULTAR MIS EXCEPCIONES
    // ==========================================

    public async Task<List<ScheduleException>>
        GetMyExceptionsAsync(
            CancellationToken cancellationToken = default)
    {
        var user = await GetAuthorizedUserAsync(cancellationToken);

        return await _context.ScheduleExceptions
            .AsNoTracking()
            .Where(x =>
                x.CoordinatorUserId == user.UserId &&
                x.IsActive)
            .OrderBy(x => x.Date)
            .ToListAsync(cancellationToken);
    }

    // ==========================================
    // REGISTRAR AUSENCIA U HORARIO ESPECIAL
    // ==========================================

    public async Task<ScheduleException> CreateExceptionAsync(
        DateOnly date,
        bool isAvailable,
        TimeOnly? startTime,
        TimeOnly? endTime,
        string? reason,
        CancellationToken cancellationToken = default,
        Guid? replaceId = null)
    {
        var user = await GetAuthorizedUserAsync(cancellationToken);

        var exception = new ScheduleException
        {
            ScheduleExceptionId = Guid.NewGuid(),
            CoordinatorUserId = user.UserId,
            Date = date,
            IsAvailable = isAvailable,
            StartTime = startTime,
            EndTime = endTime,
            Reason = reason,
            IsActive = true
        };

        var error = ScheduleValidator.ValidateException(exception);

        if (error is not null)
            throw new ArgumentException(error);

        if (reason?.Length > 250)
            throw new ArgumentException(
                "El motivo no puede superar 250 caracteres.");

        await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        ScheduleException? previous = null;
        if (replaceId.HasValue)
        {
            previous = await _context.ScheduleExceptions.SingleOrDefaultAsync(x =>
                x.ScheduleExceptionId == replaceId && x.CoordinatorUserId == user.UserId && x.IsActive,
                cancellationToken) ?? throw new KeyNotFoundException("Excepción no encontrada.");
            if (previous.Date != date) throw new ArgumentException("Edita la excepción en su fecha original.");
        }
        var alreadyExists = await _context.ScheduleExceptions
            .AnyAsync(x =>
                x.CoordinatorUserId == user.UserId &&
                x.Date == date && x.ScheduleExceptionId != replaceId &&
                x.IsActive,
                cancellationToken);

        if (alreadyExists)
            throw new InvalidOperationException(
                "Ya existe una excepcion activa para esa fecha.");

        var schedules = await _context.WorkSchedules.AsNoTracking()
            .Where(x => x.IsActive && x.DayOfWeek == date.DayOfWeek).ToListAsync(cancellationToken);
        var exceptions = await _context.ScheduleExceptions.AsNoTracking()
            .Where(x => x.IsActive && x.Date == date).ToListAsync(cancellationToken);
        exceptions.RemoveAll(x => x.ScheduleExceptionId == replaceId);
        exceptions.Add(exception);
        CheckCoverage(date, schedules, exceptions);
        if (previous is null) _context.ScheduleExceptions.Add(exception);
        else
        {
            previous.StartTime = exception.StartTime;
            previous.EndTime = exception.EndTime;
            previous.IsAvailable = exception.IsAvailable;
            previous.Reason = exception.Reason;
        }

        await RecordAsync(user.UserId,
            previous is null ? "schedule.exception.create" : "schedule.exception.update",
            "ScheduleException", (previous ?? exception).ScheduleExceptionId,
            cancellationToken);

        await tx.CommitAsync(cancellationToken);
        return previous ?? exception;
    }

    private Task<Guid> RecordAsync(Guid actor, string action, string entity,
        Guid entityId, CancellationToken ct) =>
        _audit.RecordAsync(new AuditWriteRequest(actor, AuditActorKind.User,
            action, entity, entityId), ct);

}
