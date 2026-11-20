using PRE_EMERGENCIAS_APP_MOVIL.Incidents;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Incidents;

namespace PRE_EMERGENCIAS_APP_MOVIL;

public partial class AppShell : Shell
{
    public AppShell(
        MainPage mainPage,
        ReportIncidentPage reportIncidentPage,
        CoordinatedIncidentsPage coordinatedIncidentsPage)
    {
        InitializeComponent();

        var root = new FlyoutItem { Route = "root" };
        root.Items.Add(CreateTab("Inicio", MobileRoutes.Home, mainPage));
        // AL-01/AL-02: pestañas del módulo de incidentes (coordinar con SA para la navegación final por rol).
        root.Items.Add(CreateTab("Reportar", IncidentMobileRoutes.Report, reportIncidentPage));
        root.Items.Add(CreateTab("Mis incidentes", IncidentMobileRoutes.Coordinated, coordinatedIncidentsPage));
        Items.Add(root);
    }

    private static Tab CreateTab(string title, string route, Page page)
    {
        var tab = new Tab { Title = title };
        tab.Items.Add(new ShellContent { Title = title, Route = route, Content = page });
        return tab;
    }
}
