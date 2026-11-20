using System.Net.Http;
using PRE_EMERGENCIAS_APP_MOVIL.Services;

namespace PRE_EMERGENCIAS_APP_MOVIL;

public partial class MainPage : ContentPage
{
    private readonly IApiEndpointSettings _settings;
    private readonly ApiClockClient _clockClient;

    public MainPage(IApiEndpointSettings settings, ApiClockClient clockClient)
    {
        InitializeComponent();
        _settings = settings;
        _clockClient = clockClient;
        ApiUrlEntry.Text = _settings.CurrentUrl;
        if (string.IsNullOrWhiteSpace(ApiUrlEntry.Text))
        {
            StatusLabel.Text = "En un dispositivo físico, introduce la dirección alcanzable de la API.";
        }
    }

    private void OnSaveClicked(object? sender, EventArgs e)
    {
        try
        {
            StatusLabel.Text = _settings.TrySave(ApiUrlEntry.Text, out var error)
                ? "Dirección guardada en este dispositivo."
                : error;
        }
        catch (Exception)
        {
            StatusLabel.Text = "No se pudo guardar la dirección en este dispositivo.";
        }
    }

    private async void OnCheckClicked(object? sender, EventArgs e)
    {
        try
        {
            if (!_settings.TrySave(ApiUrlEntry.Text, out var error))
            {
                StatusLabel.Text = error;
                return;
            }

            CheckButton.IsEnabled = false;
            SaveButton.IsEnabled = false;
            BusyIndicator.IsVisible = true;
            BusyIndicator.IsRunning = true;
            StatusLabel.Text = "Consultando la API...";
            TimeLabel.Text = string.Empty;

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var time = await _clockClient.GetServerTimeAsync(timeout.Token);
            StatusLabel.Text = "API disponible.";
            TimeLabel.Text = $"Hora del servidor (UTC): {time.UtcNow:yyyy-MM-dd HH:mm:ss zzz}";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "La API no respondió dentro del tiempo de espera.";
        }
        catch (HttpRequestException)
        {
            StatusLabel.Text = "No se pudo conectar. Revisa la dirección y que la API esté en ejecución.";
        }
        catch (Exception)
        {
            StatusLabel.Text = "La respuesta de la API no pudo procesarse.";
        }
        finally
        {
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
            CheckButton.IsEnabled = true;
            SaveButton.IsEnabled = true;
        }
    }
}
