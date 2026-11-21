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
    private readonly Tab _suggestionsTab;

    public AppShell(
        MobileSession session,
        AccountPage accountPage,
        MainPage mainPage,
        ReportIncidentPage reportIncidentPage,
        CoordinatedIncidentsPage coordinatedIncidentsPage,
        DianaMenuPage dianaMenuPage,
        UsersPage usersPage,
        SuggestionsPage suggestionsPage)
    {
        InitializeComponent();
        _session = session;

        var root = new FlyoutItem { Route = "root" };
        root.Items.Add(CreateTab("Cuenta", "account", accountPage));
        root.Items.Add(CreateTab("Inicio", MobileRoutes.Home, mainPage));
        _reportTab = CreateTab("Reportar", IncidentMobileRoutes.Report, reportIncidentPage);
        _coordinatedTab = CreateTab("Mis incidentes", IncidentMobileRoutes.Coordinated, coordinatedIncidentsPage);
        _dianaTab = CreateTab("Catálogos y atención", "diana-menu", dianaMenuPage);
        _usersTab = CreateTab("Usuarios", "users", usersPage);
        _suggestionsTab = CreateTab("Sugerencias", "suggestions", suggestionsPage);
        root.Items.Add(_reportTab);
        root.Items.Add(_coordinatedTab);
        root.Items.Add(_dianaTab);
        root.Items.Add(_usersTab);
        root.Items.Add(_suggestionsTab);
        Items.Add(root);
        UpdateTabs();
        _session.Changed += () => MainThread.BeginInvokeOnMainThread(UpdateTabs);
        Loaded += async (_, _) =>
        {
            await _session.RestoreAsync();
            UpdateTabs();
        };
    }

    private void UpdateTabs()
    {
        var user = _session.CurrentUser;
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
