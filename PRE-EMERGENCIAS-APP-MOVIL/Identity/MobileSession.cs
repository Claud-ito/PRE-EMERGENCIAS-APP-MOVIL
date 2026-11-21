using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Maui.Storage;
using PRE_EMERGENCIAS_APP_MOVIL.Services;
using Univalle.Emergencias.Contracts.Identity;

namespace PRE_EMERGENCIAS_APP_MOVIL.Identity;

public sealed record MobileLoginResult(bool Succeeded, string Message);

/// <summary>Conserva solo el token en almacenamiento protegido; la API valida cada operación.</summary>
public sealed class MobileSession(HttpClient http, IApiEndpointSettings settings)
{
    private const string TokenKey = "auth.access-token.v1";
    private const string EndpointKey = "auth.endpoint.v1";
    private const string InitializedKey = "auth.storage-initialized.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public AuthenticatedUser? CurrentUser { get; private set; }
    public string? DisplayName { get; private set; }
    public event Action? Changed;

    public async Task<MobileLoginResult> LoginAsync(string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return new(false, "Escribe usuario y contraseña.");
        if (!TryBaseUrl(out var baseUrl, out var error))
            return new(false, error);
        try
        {
            using var response = await http.PostAsJsonAsync(
                new Uri(new Uri(baseUrl + "/"), AuthRoutes.Login),
                new LoginRequest(username.Trim(), password), Json);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return new(false, "Usuario, contraseña o cuenta no válidos.");
            if (!response.IsSuccessStatusCode)
                return new(false, "No se pudo iniciar sesión. Intenta de nuevo.");
            var login = await response.Content.ReadFromJsonAsync<LoginResponse>(Json);
            if (login is null || string.IsNullOrWhiteSpace(login.AccessToken))
                return new(false, "La respuesta de acceso está incompleta.");
            Preferences.Default.Set(InitializedKey, true);
            await SecureStorage.Default.SetAsync(TokenKey, login.AccessToken);
            await SecureStorage.Default.SetAsync(EndpointKey, baseUrl);
            SetUser(login.User);
            return new(true, "Sesión iniciada.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new(false, "No se pudo conectar con el servidor.");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return new(false, "La respuesta de acceso no pudo procesarse.");
        }
        catch (Exception)
        {
            return new(false, "No se pudo guardar la sesión en este dispositivo.");
        }
    }

    public async Task<bool> RestoreAsync()
    {
        string? token;
        try
        {
            // En iOS Keychain puede sobrevivir a una reinstalación; se descarta ese token viejo.
            if (!Preferences.Default.ContainsKey(InitializedKey))
            {
                SecureStorage.Default.Remove(TokenKey);
                SecureStorage.Default.Remove(EndpointKey);
                Preferences.Default.Set(InitializedKey, true);
            }
            token = await SecureStorage.Default.GetAsync(TokenKey);
            var savedEndpoint = await SecureStorage.Default.GetAsync(EndpointKey);
            if (!ApiEndpointSettings.TryNormalize(settings.CurrentUrl, out var currentEndpoint, out _)
                || !string.Equals(savedEndpoint, currentEndpoint, StringComparison.OrdinalIgnoreCase))
            {
                ClearLocal();
                return false;
            }
        }
        catch (Exception)
        {
            ClearLocal();
            return false;
        }
        if (string.IsNullOrEmpty(token) || !TryBaseUrl(out var baseUrl, out _))
            return false;
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, new Uri(new Uri(baseUrl + "/"), AuthRoutes.Me));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                ClearLocal();
                return false;
            }
            if (!response.IsSuccessStatusCode) return false;
            var current = await response.Content.ReadFromJsonAsync<CurrentSessionResponse>(Json);
            if (current is null) return false;
            SetUser(current.User);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or JsonException or NotSupportedException)
        {
            return false;
        }
    }

    public async Task AuthorizeAsync(HttpRequestMessage request)
    {
        var token = await SecureStorage.Default.GetAsync(TokenKey);
        var savedEndpoint = await SecureStorage.Default.GetAsync(EndpointKey);
        if (!TryBaseUrl(out var currentEndpoint, out _)
            || !string.Equals(savedEndpoint, currentEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            ClearLocal();
            throw new InvalidOperationException("La dirección del servidor cambió. Ingresa de nuevo.");
        }
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Inicia sesión para continuar.");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task SignOutAsync()
    {
        try
        {
            if (TryBaseUrl(out var baseUrl, out _))
            {
                using var request = new HttpRequestMessage(
                    HttpMethod.Post, new Uri(new Uri(baseUrl + "/"), AuthRoutes.Logout));
                await AuthorizeAsync(request);
                using var response = await http.SendAsync(request);
            }
        }
        catch (Exception)
        {
            // La sesión local se borra aunque el servidor no esté disponible.
        }
        ClearLocal();
    }

    public void ClearLocal()
    {
        try
        {
            SecureStorage.Default.Remove(TokenKey);
            SecureStorage.Default.Remove(EndpointKey);
        }
        catch (Exception) { }
        CurrentUser = null;
        DisplayName = null;
        Changed?.Invoke();
    }

    private void SetUser(SessionUser user)
    {
        CurrentUser = new AuthenticatedUser(
            user.UserId, true, user.Role, user.Permissions.ToHashSet(StringComparer.Ordinal));
        DisplayName = user.FullName;
        Changed?.Invoke();
    }

    private bool TryBaseUrl(out string baseUrl, out string error) =>
        ApiEndpointSettings.TryNormalize(settings.CurrentUrl, out baseUrl, out error);
}
