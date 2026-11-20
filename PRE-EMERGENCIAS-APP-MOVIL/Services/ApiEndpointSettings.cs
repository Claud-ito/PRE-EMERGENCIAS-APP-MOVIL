using Microsoft.Maui.Devices;
using Microsoft.Maui.Storage;

namespace PRE_EMERGENCIAS_APP_MOVIL.Services;

public interface IApiEndpointSettings
{
    string CurrentUrl { get; }

    bool TrySave(string? value, out string error);
}

public sealed class ApiEndpointSettings : IApiEndpointSettings
{
    private const string PreferenceKey = "api.base-url.v1";

    public string CurrentUrl
    {
        get
        {
            var saved = Preferences.Default.Get(PreferenceKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(saved))
            {
                return saved;
            }

            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                return DeviceInfo.DeviceType == DeviceType.Virtual
                    ? "http://10.0.2.2:5080"
                    : string.Empty;
            }

            if (DeviceInfo.Platform == DevicePlatform.iOS
                && DeviceInfo.DeviceType != DeviceType.Virtual)
            {
                return string.Empty;
            }

            return "http://localhost:5080";
        }
    }

    public bool TrySave(string? value, out string error)
    {
        if (!TryNormalize(value, out var normalized, out error))
        {
            return false;
        }

        Preferences.Default.Set(PreferenceKey, normalized);
        return true;
    }

    public static bool TryNormalize(string? value, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "Escribe una URL HTTP o HTTPS completa, sin ruta, usuario ni contraseña.";
            return false;
        }

        normalized = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return true;
    }
}
