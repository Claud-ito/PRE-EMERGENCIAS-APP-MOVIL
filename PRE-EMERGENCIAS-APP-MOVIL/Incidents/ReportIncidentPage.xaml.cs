using PRE_EMERGENCIAS_APP_MOVIL.Services;
using Univalle.Emergencias.Contracts.Incidents;

namespace PRE_EMERGENCIAS_APP_MOVIL.Incidents;

/// <summary>
/// RF07 (AL-01): reporte en tres pasos (tipo, ubicación, gravedad) más el envío. RNF05.
/// El usuario y la hora no se piden: los fija el servidor con la sesión.
/// </summary>
public partial class ReportIncidentPage : ContentPage
{
    private readonly IncidentApiClient _api;
    private readonly DevSessionSettings _devSession;

    /// <summary>
    /// Identificador del envío. Se conserva si la red falla (reintentar no duplica el aporte)
    /// y se renueva cuando el servidor confirma o rechaza el reporte.
    /// </summary>
    private Guid _clientRequestId = Guid.NewGuid();

    private bool _optionsLoaded;
    private bool _busy;

    public ReportIncidentPage(IncidentApiClient api, DevSessionSettings devSession)
    {
        InitializeComponent();
        _api = api;
        _devSession = devSession;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
#if DEBUG
        DevSessionLabel.IsVisible = true;
        DevSessionLabel.Text = $"Sesión de ensayo: {_devSession.Role} · {_devSession.UserId.ToString()[..8]}";
#endif
        if (!_optionsLoaded)
        {
            await LoadOptionsAsync();
        }
    }

    private async void OnReloadOptionsClicked(object? sender, EventArgs e) => await LoadOptionsAsync();

    private async Task LoadOptionsAsync()
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true, "Cargando ubicaciones...");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var result = await _api.GetReportOptionsAsync(timeout.Token);
            if (!result.Succeeded)
            {
                StatusLabel.Text = result.Message;
                return;
            }

            var previousModule = (ModulePicker.SelectedItem as ReportModuleOption)?.ModuleId;
            var previousFloor = (FloorPicker.SelectedItem as ReportFloorOption)?.FloorId;
            var modules = result.Value!.Modules.ToList();
            ModulePicker.ItemsSource = modules;
            ModulePicker.SelectedIndex = modules.FindIndex(module => module.ModuleId == previousModule);
            if (FloorPicker.ItemsSource is List<ReportFloorOption> floors)
            {
                FloorPicker.SelectedIndex = floors.FindIndex(floor => floor.FloorId == previousFloor);
            }

            _optionsLoaded = true;
            StatusLabel.Text = modules.Count == 0 ? "No hay ubicaciones disponibles para reportar." : string.Empty;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnModuleChanged(object? sender, EventArgs e)
    {
        var floors = (ModulePicker.SelectedItem as ReportModuleOption)?.Floors.ToList() ?? [];
        FloorPicker.ItemsSource = floors;
        FloorPicker.SelectedIndex = floors.Count == 1 ? 0 : -1;
        FloorPicker.IsEnabled = floors.Count > 0;
        ClearError(ModuleError);
    }

    private void OnFormChanged(object? sender, EventArgs e)
    {
        if (sender == FireRadio || sender == FirstAidRadio)
        {
            ClearError(TypeError);
        }
        else if (sender == FloorPicker)
        {
            ClearError(FloorError);
        }
        else if (sender == DetailsEditor)
        {
            ClearError(DetailsError);
        }
        else
        {
            ClearError(SeverityError);
        }
    }

    private async void OnSubmitClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var request = new SubmitIncidentReportRequest(
            _clientRequestId,
            FireRadio.IsChecked ? IncidentType.Incendio
                : FirstAidRadio.IsChecked ? IncidentType.PrimerosAuxilios
                : null,
            (ModulePicker.SelectedItem as ReportModuleOption)?.ModuleId,
            (FloorPicker.SelectedItem as ReportFloorOption)?.FloorId,
            MildRadio.IsChecked ? PreliminarySeverity.Leve
                : SevereRadio.IsChecked ? PreliminarySeverity.Grave
                : VerySevereRadio.IsChecked ? PreliminarySeverity.MuyGrave
                : null,
            string.IsNullOrWhiteSpace(DetailsEditor.Text) ? null : DetailsEditor.Text.Trim());

        if (ShowLocalErrors(request))
        {
            StatusLabel.Text = "Completa los datos marcados.";
            return;
        }

        SetBusy(true, "Enviando reporte...");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await _api.SubmitReportAsync(request, timeout.Token);
            if (result.Succeeded)
            {
                var response = result.Value!;
                ResetForm();
                StatusLabel.Text = response.IncidentCreated
                    ? $"Reporte enviado. Se abrió un incidente nuevo en ese lugar.\nHora del servidor: {response.ReportedAtUtc.ToLocalTime():HH:mm:ss}"
                    : $"Reporte enviado. Ya había un incidente activo en ese lugar: tu reporte se agregó como aporte.\nHora del servidor: {response.ReportedAtUtc.ToLocalTime():HH:mm:ss}";
                return;
            }

            switch (result.Failure)
            {
                case ApiFailureKind.Validation:
                    _clientRequestId = Guid.NewGuid();
                    ShowServerErrors(result.FieldErrors);
                    StatusLabel.Text = result.Message;
                    break;
                case ApiFailureKind.LocationUnavailable:
                    _clientRequestId = Guid.NewGuid();
                    StatusLabel.Text = result.Message;
                    _optionsLoaded = false;
                    break;
                case ApiFailureKind.Network:
                    // Se conserva _clientRequestId: si el reporte sí llegó, reenviarlo no crea otro aporte.
                    StatusLabel.Text = result.Message + " Tus datos se conservan; vuelve a enviar.";
                    break;
                default:
                    StatusLabel.Text = result.Message;
                    break;
            }
        }
        finally
        {
            SetBusy(false);
        }

        if (!_optionsLoaded)
        {
            await LoadOptionsAsync();
        }
    }

    private bool ShowLocalErrors(SubmitIncidentReportRequest request)
    {
        var hasErrors = false;
        hasErrors |= SetError(TypeError, request.Type is null, "Selecciona el tipo de incidente.");
        hasErrors |= SetError(ModuleError, request.ModuleId is null, "Selecciona el módulo.");
        hasErrors |= SetError(FloorError, request.FloorId is null, "Selecciona el piso.");
        hasErrors |= SetError(SeverityError, request.PreliminarySeverity is null, "Selecciona la gravedad preliminar.");
        return hasErrors;
    }

    private void ShowServerErrors(IReadOnlyDictionary<string, string[]> errors)
    {
        foreach (var (field, messages) in errors)
        {
            var label = field.ToLowerInvariant() switch
            {
                "type" => TypeError,
                "moduleid" => ModuleError,
                "floorid" => FloorError,
                "preliminaryseverity" => SeverityError,
                "details" => DetailsError,
                _ => null
            };
            if (label is not null)
            {
                SetError(label, true, string.Join(" ", messages));
            }
        }
    }

    private void ResetForm()
    {
        _clientRequestId = Guid.NewGuid();
        FireRadio.IsChecked = FirstAidRadio.IsChecked = false;
        MildRadio.IsChecked = SevereRadio.IsChecked = VerySevereRadio.IsChecked = false;
        ModulePicker.SelectedIndex = -1;
        DetailsEditor.Text = string.Empty;
        foreach (var label in new[] { TypeError, ModuleError, FloorError, SeverityError, DetailsError })
        {
            ClearError(label);
        }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        _busy = busy;
        BusyIndicator.IsVisible = BusyIndicator.IsRunning = busy;
        SubmitButton.IsEnabled = ReloadOptionsButton.IsEnabled = !busy;
        if (status is not null)
        {
            StatusLabel.Text = status;
        }
    }

    private static bool SetError(Label label, bool condition, string message)
    {
        label.Text = condition ? message : string.Empty;
        label.IsVisible = condition;
        return condition;
    }

    private static void ClearError(Label label) => SetError(label, false, string.Empty);
}
