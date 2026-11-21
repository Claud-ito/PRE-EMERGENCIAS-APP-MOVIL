
namespace Univalle.Emergencias.Contracts.Turns;

public sealed record WorkScheduleDto(
    Guid WorkScheduleId,
    Guid CoordinatorUserId,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool IsActive);

public sealed record SaveWorkScheduleRequest(
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime);

public sealed record ScheduleExceptionDto(
    Guid ScheduleExceptionId,
    Guid CoordinatorUserId,
    DateOnly Date,
    bool IsAvailable,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    string? Reason);

public sealed record SaveScheduleExceptionRequest(
    DateOnly Date,
    bool IsAvailable,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    string? Reason);
