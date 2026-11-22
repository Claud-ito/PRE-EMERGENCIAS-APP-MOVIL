
namespace Univalle.Emergencias.Contracts.Turns;

public sealed record CreateScheduleExceptionRequest(
    DateOnly Date,
    bool IsAvailable,
    TimeOnly? StartTime,
    TimeOnly? EndTime,
    string? Reason);
