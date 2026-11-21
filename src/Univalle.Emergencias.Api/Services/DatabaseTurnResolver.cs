
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Turns;

namespace Univalle.Emergencias.Api.Services;

public class DatabaseTurnResolver : ITurnResolver
{
    private readonly AppDbContext _context;

    public DatabaseTurnResolver(AppDbContext context)
    {
        _context = context;
    }

    public async Task<TurnCoverage?> ResolveAsync(
        DateTimeOffset instantUtc,
        CancellationToken cancellationToken = default)
    {
        var schedules = await _context.WorkSchedules
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(cancellationToken);

        var exceptions = await _context.ScheduleExceptions
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(cancellationToken);

        // Reutilizamos la logica que ya probamos
        var resolver = new TurnResolver(
            schedules,
            exceptions);

        return await resolver.ResolveAsync(
            instantUtc,
            cancellationToken);
    }
}
