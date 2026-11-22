
using Univalle.Emergencias.Api.Models;

namespace Univalle.Emergencias.Api.Services;

public static class ScheduleValidator
{
    public static string? ValidateHours(
        TimeOnly start,
        TimeOnly end)
    {
        if (start >= end)
        {
            return "La hora de inicio debe ser menor " +
                   "que la hora de finalizacion.";
        }

        return null;
    }

    public static bool HasOverlap(
        IEnumerable<WorkSchedule> schedules,
        DayOfWeek day,
        TimeOnly start,
        TimeOnly end,
        Guid? excludedScheduleId = null)
    {
        return schedules.Any(schedule =>
            schedule.IsActive &&
            schedule.DayOfWeek == day &&
            schedule.WorkScheduleId != excludedScheduleId &&
            start < schedule.EndTime &&
            end > schedule.StartTime);
    }

    public static string? ValidateException(
        ScheduleException exception)
    {
        if (!exception.IsAvailable)
        {
            if (exception.StartTime.HasValue ||
                exception.EndTime.HasValue)
            {
                return "Una ausencia no debe tener horas.";
            }

            return null;
        }

        if (exception.StartTime.HasValue !=
            exception.EndTime.HasValue)
        {
            return "Debe proporcionar ambas horas " +
                   "o ninguna.";
        }

        if (exception.StartTime.HasValue &&
            exception.EndTime.HasValue)
        {
            return ValidateHours(
                exception.StartTime.Value,
                exception.EndTime.Value);
        }

        return null;
    }
}
