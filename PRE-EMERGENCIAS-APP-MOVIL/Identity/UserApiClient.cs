using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PRE_EMERGENCIAS_APP_MOVIL.Services;
using Univalle.Emergencias.Contracts.Identity;

namespace PRE_EMERGENCIAS_APP_MOVIL.Identity;

public sealed class UserApiClient(
    HttpClient http, IApiEndpointSettings settings, MobileSession session)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<UserView>> ListAsync(bool includeInactive)
    {
        using var response = await SendAsync(
            HttpMethod.Get, UserRoutes.Base + "?includeInactive=" + includeInactive.ToString().ToLowerInvariant());
        return await response.Content.ReadFromJsonAsync<List<UserView>>(Json) ?? [];
    }

    public async Task<UserView> CreateAsync(CreateUserRequest input)
    {
        using var response = await SendAsync(HttpMethod.Post, UserRoutes.Base, input);
        return await response.Content.ReadFromJsonAsync<UserView>(Json)
            ?? throw new InvalidOperationException("La respuesta está vacía.");
    }

    public async Task<UserView> UpdateAsync(Guid id, UpdateUserRequest input)
    {
        using var response = await SendAsync(HttpMethod.Put, UserRoutes.Base + "/" + id, input);
        return await response.Content.ReadFromJsonAsync<UserView>(Json)
            ?? throw new InvalidOperationException("La respuesta está vacía.");
    }

    public async Task ReactivateAsync(Guid id)
    {
        using var response = await SendAsync(HttpMethod.Post, UserRoutes.Base + "/" + id + "/reactivate");
    }

    public async Task DeactivateAsync(Guid id)
    {
        using var response = await SendAsync(HttpMethod.Delete, UserRoutes.Base + "/" + id);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string route, object? data = null)
    {
        if (!ApiEndpointSettings.TryNormalize(settings.CurrentUrl, out var address, out var error))
            throw new InvalidOperationException(error);
        using var request = new HttpRequestMessage(method, new Uri(new Uri(address + "/"), route));
        if (data is not null) request.Content = JsonContent.Create(data, options: Json);
        await session.AuthorizeAsync(request);
        var response = await http.SendAsync(request);
        if (response.IsSuccessStatusCode) return response;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            session.ClearLocal();
            response.Dispose();
            throw new InvalidOperationException("La sesión venció o la cuenta fue desactivada.");
        }
        var message = "No se pudo completar la operación.";
        try
        {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (body.RootElement.TryGetProperty("title", out var title))
                message = title.GetString() ?? message;
        }
        catch (JsonException) { }
        response.Dispose();
        throw new InvalidOperationException(message);
    }
}
