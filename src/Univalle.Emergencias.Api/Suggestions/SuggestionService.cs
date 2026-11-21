using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Incidents;
using Univalle.Emergencias.Contracts.Locations;
using Univalle.Emergencias.Contracts.Suggestions;

namespace Univalle.Emergencias.Api.Suggestions;

public static class SuggestionWindow
{
    public static DateTimeOffset WriteUntilUtc(DateTimeOffset closedAtUtc) =>
        closedAtUtc.ToUniversalTime().AddHours(6);

    public static bool CanWrite(DateTimeOffset closedAtUtc, DateTimeOffset nowUtc)
    {
        var closed = closedAtUtc.ToUniversalTime();
        var now = nowUtc.ToUniversalTime();
        return now >= closed && now < WriteUntilUtc(closed);
    }
}

public sealed record SuggestionWriteResult(SuggestionView? Value, string? ErrorCode);

public sealed class SuggestionService(
    AppDbContext db,
    ILocationCatalog locations,
    IServerClock clock,
    IAuditTrail audit)
{
    public async Task<IReadOnlyList<ClosedIncidentOption>> ListClosedAsync(
        CancellationToken cancellationToken)
    {
        var incidents = await db.Incidents.AsNoTracking()
            .Where(x => x.Status == IncidentStatuses.Closed && x.ClosedAtUtc != null)
            .Select(x => new { x.IncidentId, x.ModuleId, x.FloorId, x.ClosedAtUtc })
            .ToListAsync(cancellationToken);
        var orderedIncidents = incidents.OrderByDescending(x => x.ClosedAtUtc);
        var options = new List<ClosedIncidentOption>(incidents.Count);
        foreach (var incident in orderedIncidents)
        {
            var module = await locations.GetModuleAsync(incident.ModuleId, cancellationToken);
            var floor = await locations.GetFloorAsync(incident.FloorId, cancellationToken);
            options.Add(new ClosedIncidentOption(
                incident.IncidentId,
                module?.Name ?? "Módulo no disponible",
                floor?.Name ?? "Piso no disponible",
                incident.ClosedAtUtc!.Value));
        }
        return options;
    }

    public async Task<SuggestionThreadView> ReadAsync(
        IncidentSnapshot incident, CancellationToken cancellationToken)
    {
        var closedAt = incident.ClosedAtUtc!.Value.ToUniversalTime();
        var suggestions = await db.IncidentSuggestions.AsNoTracking()
            .Where(x => x.IncidentId == incident.IncidentId)
            .Select(x => new SuggestionView(
                x.SuggestionId, x.IncidentId, x.AuthorUserId,
                x.Author.FullName, x.Text, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        suggestions = suggestions.OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.SuggestionId).ToList();
        return new SuggestionThreadView(
            incident.IncidentId,
            closedAt,
            SuggestionWindow.WriteUntilUtc(closedAt),
            SuggestionWindow.CanWrite(closedAt, clock.UtcNow),
            suggestions);
    }

    public async Task<SuggestionWriteResult> WriteAsync(
        IncidentSnapshot incident, Guid authorUserId, string text,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.ToUniversalTime();
        if (!SuggestionWindow.CanWrite(incident.ClosedAtUtc!.Value, now))
            return new(null, SuggestionErrorCodes.WindowClosed);
        var author = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            x => x.UserId == authorUserId && x.IsActive, cancellationToken);
        if (author is null) return new(null, ApiErrorCodes.Unauthenticated);

        // La hora se vuelve a leer justo antes de persistir para respetar el límite exacto.
        now = clock.UtcNow.ToUniversalTime();
        if (!SuggestionWindow.CanWrite(incident.ClosedAtUtc.Value, now))
            return new(null, SuggestionErrorCodes.WindowClosed);
        var suggestion = new IncidentSuggestion
        {
            SuggestionId = Guid.CreateVersion7(now),
            IncidentId = incident.IncidentId,
            AuthorUserId = authorUserId,
            Text = text,
            CreatedAtUtc = now
        };
        db.IncidentSuggestions.Add(suggestion);
        await audit.RecordAsync(new AuditWriteRequest(
            authorUserId, AuditActorKind.User, "suggestion.create",
            "IncidentSuggestion", suggestion.SuggestionId, incident.IncidentId),
            cancellationToken);
        return new(new SuggestionView(
            suggestion.SuggestionId, incident.IncidentId, authorUserId,
            author.FullName, text, now), null);
    }
}
