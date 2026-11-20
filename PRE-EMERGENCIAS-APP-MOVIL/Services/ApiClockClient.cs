using System.Net.Http.Json;
using Univalle.Emergencias.Contracts.Common;

namespace PRE_EMERGENCIAS_APP_MOVIL.Services;

public sealed class ApiClockClient(HttpClient httpClient, IApiEndpointSettings settings)
{
    public async Task<ServerTimeResponse> GetServerTimeAsync(
        CancellationToken cancellationToken = default)
    {
        if (!ApiEndpointSettings.TryNormalize(settings.CurrentUrl, out var baseUrl, out var error))
        {
            throw new InvalidOperationException(error);
        }

        var endpoint = new Uri(new Uri(baseUrl + "/"), ApiRoutes.SystemTime);
        using var response = await httpClient.GetAsync(
            endpoint,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ServerTimeResponse>(
            cancellationToken) ?? throw new InvalidDataException("La API no devolvió la hora del servidor.");
    }
}
