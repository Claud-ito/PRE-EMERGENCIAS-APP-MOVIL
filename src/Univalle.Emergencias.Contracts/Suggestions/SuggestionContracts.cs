using Univalle.Emergencias.Contracts.Common;

namespace Univalle.Emergencias.Contracts.Suggestions;

public static class SuggestionRoutes
{
    public const string Base = ApiRoutes.ApiV1 + "/suggestions";
    public const string ClosedIncidents = Base + "/incidents";
    public static string Thread(Guid incidentId) => ClosedIncidents + "/" + incidentId;
}

public static class SuggestionErrorCodes
{
    public const string IncidentNotClosed = "incident_not_closed";
    public const string WindowClosed = "suggestion_window_closed";
}

public sealed record ClosedIncidentOption(
    Guid IncidentId,
    string ModuleName,
    string FloorName,
    DateTimeOffset ClosedAtUtc);

public sealed record SuggestionView(
    Guid SuggestionId,
    Guid IncidentId,
    Guid AuthorUserId,
    string AuthorName,
    string Text,
    DateTimeOffset CreatedAtUtc);

public sealed record SuggestionThreadView(
    Guid IncidentId,
    DateTimeOffset ClosedAtUtc,
    DateTimeOffset WriteUntilUtc,
    bool CanWrite,
    IReadOnlyList<SuggestionView> Suggestions);

public sealed record CreateSuggestionRequest(string? Text);
