using Univalle.Emergencias.Contracts.Common;

namespace PRE_EMERGENCIAS_APP_MOVIL;

public partial class AppShell : Shell
{
    public AppShell(MainPage mainPage)
    {
        InitializeComponent();

        var home = new ShellContent
        {
            Title = "Inicio",
            Route = MobileRoutes.Home,
            Content = mainPage
        };
        var tab = new Tab();
        tab.Items.Add(home);
        var root = new FlyoutItem { Route = "root" };
        root.Items.Add(tab);
        Items.Add(root);
    }
}
