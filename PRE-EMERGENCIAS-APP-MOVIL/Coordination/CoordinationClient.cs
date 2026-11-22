using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PRE_EMERGENCIAS_APP_MOVIL.Identity;
using PRE_EMERGENCIAS_APP_MOVIL.Services;

namespace PRE_EMERGENCIAS_APP_MOVIL.Coordination;

public sealed class CoordinationClient(HttpClient http, IApiEndpointSettings settings, MobileSession session)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
        { Converters = { new JsonStringEnumConverter() } };

    public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new InvalidOperationException("Respuesta vacía.");
    }

    public async Task ChangeAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        using var response = await SendAsync(method, path, body, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!ApiEndpointSettings.TryNormalize(settings.CurrentUrl, out var url, out var error))
            throw new InvalidOperationException(error);
        using var request = new HttpRequestMessage(method, new Uri(new Uri(url + "/"), "api/v1/" + path));
        if (body != null) request.Content = JsonContent.Create(body, options: Json);
        await session.AuthorizeAsync(request);
        var response = await http.SendAsync(request, ct);
        if (response.IsSuccessStatusCode) return response;
        var status = response.StatusCode;
        response.Dispose();
        if (status == HttpStatusCode.Unauthorized) session.ClearLocal();
        throw new InvalidOperationException(status switch
        {
            HttpStatusCode.Unauthorized => "Inicia sesión de nuevo.",
            HttpStatusCode.Forbidden => "Tu cuenta no tiene permiso.",
            HttpStatusCode.Conflict => "Hay un registro previo, un solapamiento o el caso cambió.",
            HttpStatusCode.BadRequest => "Revisa el día, las horas y los datos elegidos.",
            _ => $"El servidor respondió con el código {(int)status}."
        });
    }
}
