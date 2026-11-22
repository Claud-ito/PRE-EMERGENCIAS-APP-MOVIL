
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

        var localTime = TimeZoneInfo.ConvertTime(instantUtc, BoliviaTimeZone);
        var date = DateOnly.FromDateTime(localTime.DateTime);
        var candidates = TurnCalendar.Coverage(date, _schedules, _exceptions)
            .Where(x => x.StartsAtUtc <= instantUtc && x.EndsAtUtc > instantUtc).ToList();

        // Si no hay cobertura, o existe una configuracion
        // ambigua, no asignamos un coordinador arbitrario.
        TurnCoverage? result = candidates.Count == 1
            ? candidates[0]
            : null;

        return Task.FromResult(result);
    }
}
