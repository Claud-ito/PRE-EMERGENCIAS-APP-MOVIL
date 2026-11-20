using Microsoft.Extensions.DependencyInjection;

namespace PRE_EMERGENCIAS_APP_MOVIL;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        // Los recursos (Styles.xaml) deben cargarse antes de construir AppShell y sus páginas:
        // si AppShell se inyecta en el constructor, MainPage busca StaticResource con el diccionario vacío.
        InitializeComponent();
        _services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(_services.GetRequiredService<AppShell>());
}
