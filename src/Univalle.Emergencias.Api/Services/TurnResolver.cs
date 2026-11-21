
using Univalle.Emergencias.Api.Models;
using Univalle.Emergencias.Contracts.Turns;

namespace Univalle.Emergencias.Api.Services;

public class TurnResolver : ITurnResolver
{
    private readonly List<WorkSchedule> _schedules;
    private readonly List<ScheduleException> _exceptions;

    private static readonly TimeZoneInfo BoliviaTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById(
            "America/La_Paz");

    public TurnResolver(
        List<WorkSchedule> schedules,
        List<ScheduleException> exceptions)
    {
        _schedules = schedules;
        _exceptions = exceptions;
    }

    public Task<TurnCoverage?> ResolveAsync(
        DateTimeOffset instantUtc,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Convertir el instante UTC a hora de Bolivia
        var localTime = TimeZoneInfo.ConvertTime(
            instantUtc,
            BoliviaTimeZone);

        var date = DateOnly.FromDateTime(localTime.DateTime);
        var time = TimeOnly.FromDateTime(localTime.DateTime);

        var candidates = new List<TurnCoverage>();

        foreach (var schedule in _schedules.Where(s => s.IsActive))
        {
            // Primera version: turnos dentro del mismo dia
            if (schedule.StartTime >= schedule.EndTime)
                continue;

            var exception = _exceptions
                .Where(e => e.IsActive
                    && e.CoordinatorUserId == schedule.CoordinatorUserId
                    && e.Date == date)
                .SingleOrDefault();

            if (exception is { IsAvailable: false })
                continue;

            var hasSpecialHours = exception is
            {
                IsAvailable: true,
                StartTime: not null,
                EndTime: not null
            };

            var start = hasSpecialHours
                ? exception!.StartTime!.Value
                : schedule.StartTime;

            var end = hasSpecialHours
                ? exception!.EndTime!.Value
                : schedule.EndTime;

            var matchesDay = schedule.DayOfWeek
                == localTime.DayOfWeek;

            if (!matchesDay && !hasSpecialHours)
                continue;

            if (start >= end || time < start || time >= end)
                continue;

            var startLocal = date.ToDateTime(start);
            var endLocal = date.ToDateTime(end);

            var startUtc = TimeZoneInfo.ConvertTimeToUtc(
                startLocal, BoliviaTimeZone);

            var endUtc = TimeZoneInfo.ConvertTimeToUtc(
                endLocal, BoliviaTimeZone);

            candidates.Add(new TurnCoverage(
                schedule.CoordinatorUserId,
                new DateTimeOffset(startUtc, TimeSpan.Zero),
                new DateTimeOffset(endUtc, TimeSpan.Zero)));
        }

        // Si no hay cobertura, o existe una configuracion
        // ambigua, no asignamos un coordinador arbitrario.
        TurnCoverage? result = candidates.Count == 1
            ? candidates[0]
            : null;

        return Task.FromResult(result);
    }
}
