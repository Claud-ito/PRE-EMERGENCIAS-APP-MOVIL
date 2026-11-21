using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using PRE_EMERGENCIAS_APP_MOVIL.Identity;
using Univalle.Emergencias.Contracts.Incidents;

namespace PRE_EMERGENCIAS_APP_MOVIL.Services;

public enum ApiFailureKind
{
    Network,
    Unauthenticated,
    Forbidden,
    Validation,
    LocationUnavailable,
    NotFound,
    Unexpected
}

/// <summary>Resultado de una llamada: valor o fallo con mensaje para el usuario y errores por campo.</summary>
public sealed record ApiResult<T>(
    T? Value,
    ApiFailureKind? Failure,
    string Message,
    IReadOnlyDictionary<string, string[]> FieldErrors)
{
    public bool Succeeded => Failure is null;

    public static ApiResult<T> Ok(T value) => new(value, null, string.Empty, Empty);

    public static ApiResult<T> Fail(ApiFailureKind kind, string message, IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(default, kind, message, errors ?? Empty);

    private static readonly IReadOnlyDictionary<string, string[]> Empty = new Dictionary<string, string[]>();
}

/// <summary>Cliente HTTP del módulo de incidentes (AL-01, AL-02).</summary>
public sealed class IncidentApiClient(
    HttpClient httpClient,
    IApiEndpointSettings settings,
    MobileSession session)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public Task<ApiResult<ReportOptionsResponse>> GetReportOptionsAsync(CancellationToken cancellationToken) =>
        SendAsync<ReportOptionsResponse>(HttpMethod.Get, IncidentRoutes.ReportOptions, null, cancellationToken);

    public Task<ApiResult<SubmitIncidentReportResponse>> SubmitReportAsync(
        SubmitIncidentReportRequest request,
        CancellationToken cancellationToken) =>
        SendAsync<SubmitIncidentReportResponse>(HttpMethod.Post, IncidentRoutes.Reports, request, cancellationToken);

    public Task<ApiResult<List<CoordinatedIncidentView>>> GetAssignedAsync(CancellationToken cancellationToken) =>
        SendAsync<List<CoordinatedIncidentView>>(HttpMethod.Get, IncidentRoutes.Assigned, null, cancellationToken);

    private async Task<ApiResult<T>> SendAsync<T>(
        HttpMethod method,
        string route,
        object? body,
        CancellationToken cancellationToken)
    {
        if (!ApiEndpointSettings.TryNormalize(settings.CurrentUrl, out var baseUrl, out var error))
        {
            return ApiResult<T>.Fail(ApiFailureKind.Network, error);
        }

        using var request = new HttpRequestMessage(method, new Uri(new Uri(baseUrl + "/"), route));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        try
        {
            await session.AuthorizeAsync(request);
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
                return value is null
                    ? ApiResult<T>.Fail(ApiFailureKind.Unexpected, "La API devolvió una respuesta vacía.")
                    : ApiResult<T>.Ok(value);
            }

            var problem = await ReadProblemAsync(response, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                session.ClearLocal();
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => ApiResult<T>.Fail(
                    ApiFailureKind.Unauthenticated, "La sesión no es válida. Vuelve a ingresar."),
                HttpStatusCode.Forbidden => ApiResult<T>.Fail(
                    ApiFailureKind.Forbidden, problem?.Title ?? "Tu cuenta no tiene permiso para esta operación."),
                HttpStatusCode.BadRequest => ApiResult<T>.Fail(
                    ApiFailureKind.Validation, problem?.Title ?? "Revisa los datos.", problem?.Errors),
                HttpStatusCode.UnprocessableEntity when problem?.Code == IncidentErrorCodes.LocationUnavailable =>
                    ApiResult<T>.Fail(
                        ApiFailureKind.LocationUnavailable,
                        "La ubicación ya no está disponible. Actualiza la lista y elige otra."),
                HttpStatusCode.NotFound => ApiResult<T>.Fail(ApiFailureKind.NotFound, "No se encontró el recurso."),
                _ => ApiResult<T>.Fail(ApiFailureKind.Unexpected, problem?.Title ?? "La API devolvió un error.")
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ApiResult<T>.Fail(ApiFailureKind.Network, "La API no respondió a tiempo.");
        }
        catch (HttpRequestException)
        {
            return ApiResult<T>.Fail(ApiFailureKind.Network, "No se pudo conectar con la API. Revisa la dirección en Inicio.");
        }
        catch (JsonException)
        {
            return ApiResult<T>.Fail(ApiFailureKind.Unexpected, "La respuesta de la API no pudo procesarse.");
        }
        catch (InvalidOperationException)
        {
            return ApiResult<T>.Fail(ApiFailureKind.Unauthenticated, "Inicia sesión para continuar.");
        }
    }

    private static async Task<ProblemPayload?> ReadProblemAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ProblemPayload>(Json, cancellationToken);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record ProblemPayload(
        string? Title,
        string? Code,
        Dictionary<string, string[]>? Errors);
}
