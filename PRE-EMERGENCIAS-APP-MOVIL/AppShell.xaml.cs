using Microsoft.Maui.ApplicationModel;
using PRE_EMERGENCIAS_APP_MOVIL.Diana;
using PRE_EMERGENCIAS_APP_MOVIL.Identity;
using PRE_EMERGENCIAS_APP_MOVIL.Incidents;
using PRE_EMERGENCIAS_APP_MOVIL.Suggestions;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Incidents;

namespace PRE_EMERGENCIAS_APP_MOVIL;

public partial class AppShell : Shell
{
    private readonly MobileSession _session;
    private readonly Tab _reportTab;
    private readonly Tab _coordinatedTab;
    private readonly Tab _dianaTab;
    private readonly Tab _usersTab;
    private readonly Tab _inboxTab;
    private readonly Coordination.CoordinationClient _coordination;
    private CancellationTokenSource? _foregroundAlerts;
    private bool _active;
    private readonly Tab _turnTab;
    private readonly Tab _suggestionsTab;

    public AppShell(
        MobileSession session,
        AccountPage accountPage,
        MainPage mainPage,
        ReportIncidentPage reportIncidentPage,
        CoordinatedIncidentsPage coordinatedIncidentsPage,
        DianaMenuPage dianaMenuPage,
        UsersPage usersPage,
        Coordination.CoordinationClient coordination,
        Coordination.InboxPage inbox,
        Coordination.TurnPage turns,
        SuggestionsPage suggestionsPage)
    {
        InitializeComponent();
        _session = session;
        _coordination = coordination;

        var root = new FlyoutItem { Route = "root" };
        root.Items.Add(CreateTab("Cuenta", "account", accountPage));
        root.Items.Add(CreateTab("Inicio", MobileRoutes.Home, mainPage));
        _reportTab = CreateTab("Reportar", IncidentMobileRoutes.Report, reportIncidentPage);
        _coordinatedTab = CreateTab("Incidentes", IncidentMobileRoutes.Coordinated, coordinatedIncidentsPage);
        _dianaTab = CreateTab("Catálogos y atención", "diana-menu", dianaMenuPage);
        _usersTab = CreateTab("Usuarios", "users", usersPage);
        _suggestionsTab = CreateTab("Sugerencias", "suggestions", suggestionsPage);
        _inboxTab = CreateTab("Avisos", "notifications", inbox);
        _turnTab = CreateTab("Turnos", "turns", turns);
        // Incidentes ocupa el lugar de Avisos para el encargado: así queda en la barra visible y no en «More».
        root.Items.Add(_inboxTab);
        root.Items.Add(_coordinatedTab);
        root.Items.Add(_turnTab);
        root.Items.Add(_reportTab);
        root.Items.Add(_dianaTab);
        root.Items.Add(_usersTab);
        root.Items.Add(_suggestionsTab);
        Items.Add(root);
        UpdateTabs();
        _session.Changed += () => MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateTabs();
            MonitorAlerts();
        });
        Loaded += async (_, _) =>
        {
            await _session.RestoreAsync();
            UpdateTabs();
        };
    }

    public void SetWindowActive(bool active)
    {
        _active = active;
        MonitorAlerts();
    }

    /// <summary>
    /// El encargado (SySO/Mantenimiento) recibe la alerta inicial en la pestaña Incidentes, no en Avisos.
    /// Líder, brigadista y médico conservan Avisos para sus convocatorias.
    /// </summary>
    private bool UsesIncidentsTab =>
        _session.CurrentUser?.Permissions.Contains(PermissionCodes.CoordinateAssignedIncident) == true;

    private void MonitorAlerts()
    {
        _foregroundAlerts?.Cancel();
        _inboxTab.Title = "Avisos";
        _coordinatedTab.Title = "Incidentes";
        if (!_active || _session.CurrentUser?.Permissions.Contains(PermissionCodes.ReceiveNotifications) != true) return;
        _foregroundAlerts = new();
        _ = WatchInboxAsync(_foregroundAlerts);
    }

    private async Task WatchInboxAsync(CancellationTokenSource source)
    {
        var ct = source.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Cada pestaña de avisos marca sus propias entregas mientras está abierta.
                if (CurrentPage is not (Coordination.InboxPage or CoordinatedIncidentsPage))
                {
                    try
                    {
                        var items = await _coordination.GetAsync<List<Univalle.Emergencias.Contracts.Notifications.InboxItem>>(
                            "notifications/mine", ct);
                        ct.ThrowIfCancellationRequested();
                        var pending = items.FirstOrDefault(x => x.DeliveryStatus == "Pending");
                        var unread = items.Count(x => x.ReadAtUtc == null);
                        var incidents = UsesIncidentsTab;
                        var tab = incidents ? _coordinatedTab : _inboxTab;
                        var name = incidents ? "Incidentes" : "Avisos";
                        tab.Title = unread == 0 ? name : $"{name} ({unread})";
                        if (pending is not null && CurrentPage is { } page
                            && page is not (Coordination.InboxPage or CoordinatedIncidentsPage))
                        {
                            var title = pending.Type == "incident.auto" ? "Convocatoria AUTO" : "Alerta inicial";
                            var open = await page.DisplayAlertAsync(title, "Hay un incidente nuevo que requiere tu atención.",
                                incidents ? "Ver incidente" : "Ver avisos", "Cerrar");
                            ct.ThrowIfCancellationRequested();
                            await _coordination.ChangeAsync(HttpMethod.Patch, $"notifications/{pending.EventId}/delivered", ct: ct);
                            if (open) CurrentItem.CurrentItem = tab;
                        }
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested)
                    {
                        System.Diagnostics.Debug.WriteLine($"Consulta de avisos: {ex.GetType().Name}");
                    }
                }
                else
                {
                    (UsesIncidentsTab ? _coordinatedTab : _inboxTab).Title = UsesIncidentsTab ? "Incidentes" : "Avisos";
                }
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { if (_foregroundAlerts == source) _foregroundAlerts = null; source.Dispose(); }
    }

    private void UpdateTabs()
    {
        var user = _session.CurrentUser;
        _inboxTab.IsVisible = user?.Permissions.Contains(PermissionCodes.ReceiveNotifications) == true
            && user.Permissions.Contains(PermissionCodes.CoordinateAssignedIncident) != true;
        _turnTab.IsVisible = user?.Permissions.Contains(PermissionCodes.ConfigureOwnAvailability) == true;
        _reportTab.IsVisible = user?.Permissions.Contains(PermissionCodes.ReportIncident) == true;
        _coordinatedTab.IsVisible =
            user?.Permissions.Contains(PermissionCodes.CoordinateAssignedIncident) == true;
        _dianaTab.IsVisible = user?.Permissions.Any(permission =>
            permission is PermissionCodes.ManageLocations
                or PermissionCodes.ManageBrigades
                or PermissionCodes.ManageSupportContacts
                or PermissionCodes.RecordInjury) == true;
        _usersTab.IsVisible = user?.Permissions.Contains(PermissionCodes.ManageUsers) == true;
        _suggestionsTab.IsVisible =
            user?.Permissions.Contains(PermissionCodes.ReadSuggestions) == true;
    }

    private static Tab CreateTab(string title, string route, Page page)
    {
        var tab = new Tab { Title = title };
        tab.Items.Add(new ShellContent { Title = title, Route = route, Content = page });
        return tab;
    }
}
