using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PRE_EMERGENCIAS_APP_MOVIL.Services;
using Univalle.Emergencias.Contracts.Identity;

namespace PRE_EMERGENCIAS_APP_MOVIL.Diana;

/// <summary>SA-01 conecta su sesión real y agrega su credencial al mensaje, sin roles enviados por el formulario.</summary>
public interface IDianaMobileSession
{
    Task<AuthenticatedUser?> GetCurrentAsync();
    Task AuthorizeAsync(HttpRequestMessage request);
}
public sealed class PendingDianaMobileSession : IDianaMobileSession
{
    public Task<AuthenticatedUser?> GetCurrentAsync() => Task.FromResult<AuthenticatedUser?>(null);
    public Task AuthorizeAsync(HttpRequestMessage request) => throw new InvalidOperationException("Inicia sesión para continuar.");
}
public sealed class DianaApiClient(HttpClient http, IApiEndpointSettings settings, IDianaMobileSession session)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) {
        Converters = { new JsonStringEnumConverter() }
    };
    public async Task<T> GetAsync<T>(string route) {
        using var response = await SendCoreAsync(HttpMethod.Get, route, null);
        return await response.Content.ReadFromJsonAsync<T>(Json) ?? throw new InvalidOperationException("La respuesta está vacía.");
    }
    public async Task SendAsync(HttpMethod method, string route, object? data = null) {
        using var response = await SendCoreAsync(method, route, data);
    }
    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string route, object? data) {
        var user = await session.GetCurrentAsync();
        if (user is null || !user.IsActive) throw new InvalidOperationException("Inicia sesión con una cuenta activa.");
        if (!ApiEndpointSettings.TryNormalize(settings.CurrentUrl, out var address, out var error)) throw new InvalidOperationException(error);
        using var request = new HttpRequestMessage(method, address + "/api/v1/" + route);
        if (data is not null) request.Content = JsonContent.Create(data, options: Json);
        await session.AuthorizeAsync(request);
        var response = await http.SendAsync(request);
        if (response.IsSuccessStatusCode) return response;
        string message = "No se pudo completar la operación.";
        try {
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (body.RootElement.TryGetProperty("title", out var title)) message = title.GetString() ?? message;
        } catch (JsonException) { }
        response.Dispose();
        throw new InvalidOperationException(message);
    }
}
