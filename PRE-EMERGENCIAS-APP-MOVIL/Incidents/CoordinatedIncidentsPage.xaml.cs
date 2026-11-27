using PRE_EMERGENCIAS_APP_MOVIL.Coordination;
using PRE_EMERGENCIAS_APP_MOVIL.Identity;
using PRE_EMERGENCIAS_APP_MOVIL.Services;
using Univalle.Emergencias.Contracts.Incidents;
using Univalle.Emergencias.Contracts.Notifications;
using Univalle.Emergencias.Contracts.Turns;

namespace PRE_EMERGENCIAS_APP_MOVIL.Incidents;

/// <summary>
/// Pestaña Incidentes del encargado (RF08 + RF09). Reúne lo que antes estaba en Avisos, Mis incidentes y Turnos:
/// una tarjeta por incidente con la alerta inicial en la cabecera, un contador de aportes nuevos,
/// el historial de aportes al tocarla, la convocatoria al líder y el relevo.
/// La reevaluación y las órdenes completas pertenecen a RF12 (Sprint 3).
/// </summary>
public partial class CoordinatedIncidentsPage : ContentPage
{
    private static readonly TimeSpan Bolivia = TimeSpan.FromHours(-4);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly IncidentApiClient _api;
    private readonly CoordinationClient _coordination;
    private readonly MobileSession _session;
    private readonly HashSet<Guid> _expanded = [];
    private readonly Dictionary<Guid, CoordinatorOption> _selectedRecipient = [];

    private List<CoordinatedIncidentView> _incidents = [];
    private List<InboxItem> _alerts = [];
    private List<HandoffView> _handoffs = [];
    private List<CoordinatorOption> _coordinators = [];
    private Guid? _coordinatorsFor;
    private string? _renderedSignature;
    private CancellationTokenSource? _polling;
    private bool _visible;
    private bool _busy;

    public CoordinatedIncidentsPage(IncidentApiClient api, CoordinationClient coordination, MobileSession session)
    {
        InitializeComponent();
        _api = api;
        _coordination = coordination;
        _session = session;
        _session.Changed += () => Dispatcher.Dispatch(() =>
        {
            _expanded.Clear();
            _selectedRecipient.Clear();
            _incidents = [];
            _alerts = [];
            _handoffs = [];
            _renderedSignature = null;
            HandoffsLayout.Children.Clear();
            IncidentsLayout.Children.Clear();
            StatusLabel.Text = MessageLabel.Text = string.Empty;
            Restart();
        });
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _visible = true;
        Restart();
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        Restart();
        base.OnDisappearing();
    }

    private async void OnRefreshing(object? sender, EventArgs e)
    {
        await LoadAsync(force: true);
        Refresher.IsRefreshing = false;
    }

    private void Restart()
    {
        _polling?.Cancel();
        if (_visible && _session.CurrentUser is not null)
        {
            _polling = new CancellationTokenSource();
            _ = PollAsync(_polling);
        }
    }

    /// <summary>Consulta periódica mientras la pestaña está visible: un aporte o caso nuevo aparece sin tocar nada.</summary>
    private async Task PollAsync(CancellationTokenSource source)
    {
        var ct = source.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await LoadAsync(force: false, ct);
                await Task.Delay(PollInterval, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            if (_polling == source) _polling = null;
            source.Dispose();
        }
    }

    private async Task LoadAsync(bool force, CancellationToken ct = default)
    {
        var user = _session.CurrentUser?.UserId;
        if (user is null) return;
        if (_busy)
        {
            if (!force) return;
            // Una acción del usuario espera a que termine la consulta periódica en curso.
            while (_busy) await Task.Delay(100, ct);
        }
        _busy = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var result = await _api.GetAssignedAsync(timeout.Token);
            if (!result.Succeeded)
            {
                StatusLabel.Text = result.Failure == ApiFailureKind.Forbidden
                    ? "Tu rol no coordina incidentes."
                    : result.Message;
                return;
            }

            var alerts = await _coordination.GetAsync<List<InboxItem>>("notifications/mine", timeout.Token);
            var handoffs = await _coordination.GetAsync<List<HandoffView>>("coordination/handoffs", timeout.Token);
            if (_coordinatorsFor != user)
            {
                _coordinators = await _coordination.GetAsync<List<CoordinatorOption>>("coordination/coordinators", timeout.Token);
                _coordinatorsFor = user;
            }

            if (user != _session.CurrentUser?.UserId) return;
            _incidents = result.Value!;
            _alerts = alerts.Where(x => x.Type == IncidentNotificationTypes.InitialAlert).ToList();
            _handoffs = handoffs;

            // La alerta inicial ya está a la vista en esta pestaña: se registra como entregada.
            foreach (var pending in _alerts.Where(x => x.DeliveryStatus == "Pending"))
                await _coordination.ChangeAsync(HttpMethod.Patch, $"notifications/{pending.EventId}/delivered", ct: timeout.Token);

            Render(force);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or OperationCanceledException)
        {
            StatusLabel.Text = ex is InvalidOperationException ? ex.Message : "No se pudo conectar con la API.";
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Redibuja solo si cambiaron los datos, para no perder lo que el usuario está eligiendo.</summary>
    private void Render(bool force)
    {
        var signature = string.Join("|", _incidents.Select(x =>
                $"{x.IncidentId}:{x.ReportCount}:{x.CalloutAtUtc}:{x.AutoDueAtUtc}:{Unread(x) is not null}:{NewReports(x)}:{_expanded.Contains(x.IncidentId)}"))
            + "#" + string.Join("|", _handoffs.Select(x => $"{x.Id}:{x.AcceptedAtUtc}"));
        StatusLabel.Text = _incidents.Count == 0
            ? "No tienes incidentes activos asignados."
            : $"{_incidents.Count} incidente(s) activo(s). Actualizado {DateTime.Now:HH:mm:ss}.";
        if (!force && signature == _renderedSignature) return;
        _renderedSignature = signature;

        HandoffsLayout.Children.Clear();
        var me = _session.CurrentUser?.UserId;
        var incoming = _handoffs.Where(x => x.ToUserId == me && x.AcceptedAtUtc is null).ToList();
        if (incoming.Count > 0)
        {
            HandoffsLayout.Children.Add(new Label { Text = "Relevos por aceptar", FontAttributes = FontAttributes.Bold, FontSize = 17 });
            foreach (var handoff in incoming) HandoffsLayout.Children.Add(BuildIncomingHandoff(handoff));
        }

        IncidentsLayout.Children.Clear();
        foreach (var incident in _incidents) IncidentsLayout.Children.Add(BuildCard(incident));
    }

    private Border BuildIncomingHandoff(HandoffView handoff)
    {
        var accept = new Button { Text = "Aceptar relevo" };
        accept.Clicked += async (_, _) => await ActAsync(accept,
            () => _coordination.ChangeAsync(HttpMethod.Post, $"coordination/handoffs/{handoff.Id}/accept"),
            "Relevo aceptado: el incidente ya está a tu cargo.",
            "No se pudo aceptar: el relevo ya no está vigente o el caso cambió.");
        return new Border
        {
            Padding = 12,
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label { Text = $"{CoordinatorName(handoff.FromUserId)} te propone un incidente · {handoff.ProposedAtUtc.ToOffset(Bolivia):dd/MM HH:mm}" },
                    accept
                }
            }
        };
    }

    private Border BuildCard(CoordinatedIncidentView incident)
    {
        var expanded = _expanded.Contains(incident.IncidentId);
        var unread = Unread(incident);
        var newReports = NewReports(incident);

        // Cabecera: datos de la alerta inicial y contador de aportes nuevos (como una notificación).
        var header = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
        var initial = incident.Reports.FirstOrDefault(x => x.IsInitial) ?? incident.Reports.FirstOrDefault();
        var headerText = new VerticalStackLayout { Spacing = 2 };
        headerText.Children.Add(new Label
        {
            Text = $"{(expanded ? "▾" : "▸")} {TypeName(incident.Type)} · {incident.ModuleName}, {incident.FloorName}",
            FontAttributes = FontAttributes.Bold,
            FontSize = 17
        });
        headerText.Children.Add(new Label
        {
            Text = $"Alerta inicial {incident.OpenedAtUtc.ToOffset(Bolivia):dd/MM HH:mm:ss}"
                + (initial is null ? string.Empty : $" · {SeverityName(initial.PreliminarySeverity)}")
                + $" · {incident.ReportCount} aporte(s)",
            FontSize = 13
        });
        if (unread is not null)
        {
            headerText.Children.Add(new Label { Text = "Alerta nueva", TextColor = Color.FromArgb("#C62828"), FontAttributes = FontAttributes.Bold, FontSize = 13 });
        }
        header.Add(headerText, 0, 0);
        if (newReports > 0 && !expanded)
        {
            header.Add(new Border
            {
                BackgroundColor = Color.FromArgb("#25D366"),
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(8, 2),
                VerticalOptions = LayoutOptions.Start,
                Content = new Label { Text = newReports > 99 ? "99+" : newReports.ToString(), TextColor = Colors.White, FontAttributes = FontAttributes.Bold }
            }.WithDescription($"{newReports} aportes nuevos"), 1, 0);
        }
        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await ToggleAsync(incident);
        header.GestureRecognizers.Add(tap);

        var content = new VerticalStackLayout { Spacing = 8 };
        content.Children.Add(header);
        content.Children.Add(new Label { Text = CalloutStatus(incident), FontSize = 13 });

        // RF09: convocatoria asociada a este caso; queda deshabilitada una vez registrada.
        var callout = new Button
        {
            Text = incident.CalloutAtUtc is null ? "Registrar convocatoria ya realizada al líder" : "Convocatoria registrada ✓",
            IsEnabled = incident.CalloutAtUtc is null
        };
        callout.Clicked += async (_, _) => await ActAsync(callout,
            () => _coordination.ChangeAsync(HttpMethod.Post, $"coordination/{incident.IncidentId}/manual"),
            "Convocatoria registrada.",
            "No se pudo registrar: ya existe una convocatoria o el módulo no tiene un líder de brigada asignado.");
        content.Children.Add(callout);

        if (expanded)
        {
            content.Children.Add(new Label { Text = "Aportes", FontAttributes = FontAttributes.Bold, Margin = new Thickness(0, 6, 0, 0) });
            foreach (var report in incident.Reports.OrderBy(x => x.ReportedAtUtc))
                content.Children.Add(BuildReport(report));
            content.Children.Add(BuildHandoffSection(incident));
        }

        return new Border { Padding = 14, Content = content };
    }

    private static View BuildReport(IncidentReportView report)
    {
        var who = report.ReporterName is null
            ? $"informante {report.ReporterUserId.ToString()[..8]}"
            : $"{report.ReporterName} ({report.ReporterUsername})";
        var line = $"{report.ReportedAtUtc.ToOffset(Bolivia):HH:mm:ss} · {SeverityName(report.PreliminarySeverity)}"
            + (report.IsInitial ? " · reporte inicial" : string.Empty)
            + $"\n{who}";
        if (!string.IsNullOrWhiteSpace(report.Details)) line += $"\n“{report.Details}”";
        return new Label { Text = line, FontSize = 13, Margin = new Thickness(8, 0, 0, 0) };
    }

    private View BuildHandoffSection(CoordinatedIncidentView incident)
    {
        var section = new VerticalStackLayout { Spacing = 6, Margin = new Thickness(0, 8, 0, 0) };
        section.Children.Add(new Label { Text = "Relevo", FontAttributes = FontAttributes.Bold });
        var pending = _handoffs.FirstOrDefault(x => x.IncidentId == incident.IncidentId && x.AcceptedAtUtc is null);
        if (pending is not null)
        {
            section.Children.Add(new Label { Text = $"Relevo pendiente: esperando que {CoordinatorName(pending.ToUserId)} lo acepte." });
            return section;
        }

        var picker = new Picker
        {
            Title = "Encargado que recibirá el relevo",
            ItemsSource = _coordinators,
            ItemDisplayBinding = new Binding(nameof(CoordinatorOption.FullName))
        };
        if (_selectedRecipient.TryGetValue(incident.IncidentId, out var chosen))
            picker.SelectedIndex = _coordinators.FindIndex(x => x.UserId == chosen.UserId);
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (picker.SelectedItem is CoordinatorOption option) _selectedRecipient[incident.IncidentId] = option;
        };
        var propose = new Button { Text = "Proponer relevo" };
        propose.Clicked += async (_, _) =>
        {
            if (picker.SelectedItem is not CoordinatorOption target)
            {
                MessageLabel.Text = "Elige al encargado que recibirá el relevo.";
                return;
            }
            await ActAsync(propose,
                () => _coordination.ChangeAsync(HttpMethod.Post, $"coordination/{incident.IncidentId}/handoffs", new CoordinationRecipient(target.UserId)),
                $"Relevo propuesto a {target.FullName}.",
                "No se pudo proponer: ya hay un relevo pendiente o el caso cambió.");
        };
        section.Children.Add(picker);
        section.Children.Add(propose);
        return section;
    }

    private async Task ToggleAsync(CoordinatedIncidentView incident)
    {
        if (!_expanded.Remove(incident.IncidentId))
        {
            _expanded.Add(incident.IncidentId);
            // Abrir la tarjeta es ver los aportes: el contador desaparece y la alerta inicial queda leída.
            Preferences.Default.Set(SeenKey(incident.IncidentId), incident.ReportCount);
            if (Unread(incident) is { } alert)
            {
                // La tarjeta se abre al instante; la lectura se registra en la API después.
                _alerts = _alerts.Select(x => x.EventId == alert.EventId ? x with { ReadAtUtc = DateTimeOffset.UtcNow } : x).ToList();
                Render(force: true);
                try { await _coordination.ChangeAsync(HttpMethod.Patch, $"notifications/{alert.EventId}/read"); }
                catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException) { MessageLabel.Text = ex.Message; }
                return;
            }
        }
        Render(force: true);
    }

    private async Task ActAsync(Button button, Func<Task> action, string success, string conflict)
    {
        button.IsEnabled = false;
        MessageLabel.Text = string.Empty;
        try
        {
            await action();
            MessageLabel.Text = success;
            await LoadAsync(force: true);
        }
        catch (InvalidOperationException ex)
        {
            MessageLabel.Text = ex.Message.StartsWith("Hay un registro previo", StringComparison.Ordinal) ? conflict : ex.Message;
            button.IsEnabled = true;
        }
        catch (HttpRequestException)
        {
            MessageLabel.Text = "No se pudo conectar con la API.";
            button.IsEnabled = true;
        }
    }

    private InboxItem? Unread(CoordinatedIncidentView incident) =>
        _alerts.FirstOrDefault(x => x.IncidentId == incident.IncidentId && x.ReadAtUtc is null);

    private int NewReports(CoordinatedIncidentView incident) =>
        Math.Max(0, incident.ReportCount - Preferences.Default.Get(SeenKey(incident.IncidentId), 0));

    private string SeenKey(Guid incidentId) => $"incident-seen:{_session.CurrentUser?.UserId}:{incidentId}";

    private string CoordinatorName(Guid userId) =>
        _coordinators.FirstOrDefault(x => x.UserId == userId)?.FullName ?? "Otro encargado";

    private static string CalloutStatus(CoordinatedIncidentView incident)
    {
        if (incident.CalloutAtUtc is { } at)
            return $"Convocatoria {(incident.CalloutChannel == "AUTO" ? "AUTO" : "manual")} al líder registrada a las {at.ToOffset(Bolivia):HH:mm:ss}.";
        if (incident.AutoDueAtUtc is { } due)
            return $"AUTO activo: si no registras la convocatoria, se avisará al líder a las {due.ToOffset(Bolivia):HH:mm:ss}.";
        return "Sin convocatoria registrada.";
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

internal static class SemanticExtensions
{
    public static T WithDescription<T>(this T view, string description) where T : VisualElement
    {
        SemanticProperties.SetDescription(view, description);
        return view;
    }
}
