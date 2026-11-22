using Univalle.Emergencias.Api.Models;
using Univalle.Emergencias.Contracts.Turns;

namespace Univalle.Emergencias.Api.Services;

public static class TurnCalendar
{
    public static readonly TimeZoneInfo Bolivia = TimeZoneInfo.FindSystemTimeZoneById("America/La_Paz");

    public static List<TurnCoverage> Coverage(DateOnly date, IEnumerable<WorkSchedule> schedules,
        IEnumerable<ScheduleException> exceptions)
    {
        var overrides = exceptions.Where(x => x.IsActive && x.Date == date &&
            (!x.IsAvailable || x.StartTime.HasValue)).ToList();
        var replaced = overrides.Select(x => x.CoordinatorUserId).ToHashSet();
        var windows = schedules.Where(x => x.IsActive && x.DayOfWeek == date.DayOfWeek &&
                !replaced.Contains(x.CoordinatorUserId))
            .Select(x => (x.CoordinatorUserId, x.StartTime, x.EndTime))
            .Concat(overrides.Where(x => x.IsAvailable && x.StartTime.HasValue && x.EndTime.HasValue)
                .Select(x => (x.CoordinatorUserId, x.StartTime!.Value, x.EndTime!.Value)));
        return windows.Where(x => x.Item2 < x.Item3).Select(x => new TurnCoverage(x.Item1,
            Utc(date, x.Item2), Utc(date, x.Item3))).OrderBy(x => x.StartsAtUtc).ToList();
    }

    public static DateTimeOffset Utc(DateOnly date, TimeOnly time) =>
        TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(time), Bolivia);

    public static List<CalendarInterval> Intervals(DateOnly date, List<TurnCoverage> windows)
    {
        var bounds = windows.SelectMany(x => new[] { x.StartsAtUtc, x.EndsAtUtc })
            .Append(Utc(date, TimeOnly.MinValue)).Append(Utc(date.AddDays(1), TimeOnly.MinValue))
            .Distinct().Order().ToArray();
        var result = new List<CalendarInterval>();
        for (var i = 1; i < bounds.Length; i++)
        {
            var active = windows.Where(x => x.StartsAtUtc <= bounds[i-1] && x.EndsAtUtc >= bounds[i]).ToList();
            result.Add(new(active.Count == 1 ? active[0].CoordinatorUserId : null, bounds[i-1], bounds[i]));
        }
        return result;
    }
}
