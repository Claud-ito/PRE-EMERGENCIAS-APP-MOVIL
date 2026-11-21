using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PRE_EMERGENCIAS_APP_MOVIL.Identity;
using PRE_EMERGENCIAS_APP_MOVIL.Services;
using Univalle.Emergencias.Contracts.Suggestions;

namespace PRE_EMERGENCIAS_APP_MOVIL.Suggestions;

public sealed class SuggestionApiClient(
    HttpClient http, IApiEndpointSettings settings, MobileSession session)
{
    public async Task<IReadOnlyList<ClosedIncidentOption>> ListAsync()
    {
        using var response = await SendAsync(HttpMethod.Get, SuggestionRoutes.ClosedIncidents);
        return await response.Content.ReadFromJsonAsync<List<ClosedIncidentOption>>() ?? [];
    }

    public async Task<SuggestionThreadView> GetAsync(Guid incidentId)
    {
        using var response = await SendAsync(HttpMethod.Get, SuggestionRoutes.Thread(incidentId));
        return await response.Content.ReadFromJsonAsync<SuggestionThreadView>()
            ?? throw new InvalidOperationException("La respuesta está vacía.");
    }

    public async Task CreateAsync(Guid incidentId, string text)
    {
        using var response = await SendAsync(
            HttpMethod.Post, SuggestionRoutes.Thread(incidentId),
            new CreateSuggestionRequest(text));
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string route, object? body = null)
    {
        if (!ApiEndpointSettings.TryNormalize(settings.CurrentUrl, out var address, out var error))
            throw new InvalidOperationException(error);
        using var request = new HttpRequestMessage(method, new Uri(new Uri(address + "/"), route));
        if (body is not null) request.Content = JsonContent.Create(body);
        await session.AuthorizeAsync(request);
        var response = await http.SendAsync(request);
        if (response.IsSuccessStatusCode) return response;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            session.ClearLocal();
        var message = "No se pudo completar la operación.";
        try
        {
            using var bodyJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (bodyJson.RootElement.TryGetProperty("title", out var title))
                message = title.GetString() ?? message;
        }
        catch (JsonException) { }
        response.Dispose();
        throw new InvalidOperationException(message);
    }
}
