using PRE_EMERGENCIAS_APP_MOVIL.Services;
using Univalle.Emergencias.Contracts.Incidents;

namespace PRE_EMERGENCIAS_APP_MOVIL.Incidents;

/// <summary>
/// AL-02: seguimiento mínimo del coordinador; muestra cada incidente activo y sus aportes.
/// La reevaluación y las órdenes completas pertenecen a RF12 (Sprint 3).
/// </summary>
public partial class CoordinatedIncidentsPage : ContentPage
{
    private readonly IncidentApiClient _api;
    private bool _loading;

    public CoordinatedIncidentsPage(IncidentApiClient api)
    {
        InitializeComponent();
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
    }

    private async void OnRefreshClicked(object? sender, EventArgs e) => await LoadAsync();

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadAsync();
        Refresher.IsRefreshing = false;
    }

    private async Task LoadAsync()
    {
        if (_loading)
        {
            return;
        }

        _loading = true;
        RefreshButton.IsEnabled = false;
        StatusLabel.Text = "Consultando...";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            var result = await _api.GetAssignedAsync(timeout.Token);
            IncidentsLayout.Children.Clear();
            if (!result.Succeeded)
            {
                StatusLabel.Text = result.Failure == ApiFailureKind.Forbidden
                    ? "Tu rol no coordina incidentes."
                    : result.Message;
                return;
            }

            var incidents = result.Value!;
            StatusLabel.Text = incidents.Count == 0
                ? "No tienes incidentes activos asignados."
                : $"{incidents.Count} incidente(s) activo(s). Actualizado {DateTime.Now:HH:mm:ss}.";
            foreach (var incident in incidents)
            {
                IncidentsLayout.Children.Add(BuildIncidentCard(incident));
            }
        }
        finally
        {
            RefreshButton.IsEnabled = true;
            _loading = false;
        }
    }

    private static Border BuildIncidentCard(CoordinatedIncidentView incident)
    {
        var content = new VerticalStackLayout { Spacing = 6 };
        content.Children.Add(new Label
        {
            Text = $"{TypeName(incident.Type)} · {incident.ModuleName}, {incident.FloorName}",
            FontAttributes = FontAttributes.Bold,
            FontSize = 17
        });
        content.Children.Add(new Label
        {
            Text = $"Abierto {incident.OpenedAtUtc.ToLocalTime():dd/MM HH:mm:ss} · {incident.ReportCount} aporte(s)"
        });

        foreach (var report in incident.Reports.OrderBy(report => report.ReportedAtUtc))
        {
            var line = $"{report.ReportedAtUtc.ToLocalTime():HH:mm:ss} · {SeverityName(report.PreliminarySeverity)}"
                + (report.IsInitial ? " · reporte inicial" : string.Empty)
                + $" · informante {report.ReporterUserId.ToString()[..8]}";
            if (!string.IsNullOrWhiteSpace(report.Details))
            {
                line += $"\n“{report.Details}”";
            }

            content.Children.Add(new Label { Text = line, FontSize = 13, Margin = new Thickness(8, 0, 0, 0) });
        }

        return new Border { Padding = 14, Content = content };
    }

    private static string TypeName(IncidentType type) => type switch
    {
        IncidentType.Incendio => "Incendio",
        IncidentType.PrimerosAuxilios => "Primeros auxilios",
        _ => type.ToString()
    };

    private static string SeverityName(PreliminarySeverity severity) => severity switch
    {
        PreliminarySeverity.Leve => "Leve",
        PreliminarySeverity.Grave => "Grave",
        PreliminarySeverity.MuyGrave => "Muy grave",
        _ => severity.ToString()
    };
}
