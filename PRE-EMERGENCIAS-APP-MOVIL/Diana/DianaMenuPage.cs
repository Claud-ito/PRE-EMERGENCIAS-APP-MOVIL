using Univalle.Emergencias.Contracts.Identity;

namespace PRE_EMERGENCIAS_APP_MOVIL.Diana;

public sealed class DianaMenuPage : ContentPage
{
    private readonly IDianaMobileSession session;
    private readonly VerticalStackLayout menu = new() { Padding = 24, Spacing = 18 };
    public DianaMenuPage(IDianaMobileSession session) { this.session = session; Title = "Catálogos y atención"; Content = menu; }
    protected override async void OnAppearing() {
        base.OnAppearing(); menu.Clear();
        menu.Add(new Label { Text = "Catálogos y atención", FontSize = 24, FontAttributes = FontAttributes.Bold });
        var user = await session.GetCurrentAsync();
        if (user is null || !user.IsActive) { menu.Add(new Label { Text = "Inicia sesión con una cuenta activa para continuar." }); return; }
        if (user.Role is UserRole.SySO or UserRole.Mantenimiento) {
            Add(user, PermissionCodes.ManageLocations, "Módulos y pisos", "diana-locations");
            Add(user, PermissionCodes.ManageBrigades, "Brigadas y líderes", "diana-brigades");
            Add(user, PermissionCodes.ManageSupportContacts, "Contactos de apoyo", "diana-contacts");
        }
        if (user.Role == UserRole.Medico) Add(user, PermissionCodes.RecordInjury, "Registro de accidentados", "diana-injuries");
        if (menu.Children.Count == 1) menu.Add(new Label { Text = "Tu cuenta no tiene operaciones disponibles en este módulo." });
    }
    private void Add(AuthenticatedUser user, string permission, string title, string route) {
        if (!user.Permissions.Contains(permission)) return;
        var button = new Button { Text = title }; button.Clicked += async (_, _) => await Shell.Current.GoToAsync(route); menu.Add(button);
    }
}
