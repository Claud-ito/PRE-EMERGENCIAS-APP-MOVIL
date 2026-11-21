using Univalle.Emergencias.Contracts.Identity;

namespace PRE_EMERGENCIAS_APP_MOVIL.Diana;

public abstract class DianaPage : ContentPage
{
    protected readonly DianaApiClient Api;
    protected readonly IDianaMobileSession Session;
    protected readonly VerticalStackLayout Form = new() { Spacing = 14, IsVisible = false };
    protected readonly Label Status = new() { TextColor = Colors.DarkRed };
    private readonly string permission;
    private readonly bool physician;
    private bool running;
    protected DianaPage(DianaApiClient api, IDianaMobileSession session, string title, string permission, bool physician = false) {
        Api = api; Session = session; Title = title; this.permission = permission; this.physician = physician;
        var content = new VerticalStackLayout { Padding = 20, Spacing = 16 };
        content.Add(new Label { Text = title, FontSize = 24, FontAttributes = FontAttributes.Bold });
        content.Add(Status); content.Add(Form); Content = new ScrollView { Content = content };
    }
    protected override async void OnAppearing() {
        base.OnAppearing();
        Form.IsVisible = false;
        await Run(async () => {
            var user = await Session.GetCurrentAsync();
            if (user is null) { Status.Text = "Inicia sesión para continuar."; return; }
            if (!user.IsActive || !user.Permissions.Contains(permission) ||
                (physician ? user.Role != UserRole.Medico : user.Role is not (UserRole.SySO or UserRole.Mantenimiento))) {
                Status.Text = "Tu cuenta no tiene acceso a esta pantalla."; return;
            }
            Form.IsVisible = true; await LoadAsync();
        });
    }
    protected abstract Task LoadAsync();
    protected async Task Run(Func<Task> work) {
        if (running) return;
        running = true; Form.IsEnabled = false; IsBusy = true; Status.Text = "";
        try { await work(); }
        catch (HttpRequestException) { Status.Text = "No se pudo conectar con la API. Revisa la dirección y tu conexión."; }
        catch (TaskCanceledException) { Status.Text = "La solicitud tardó demasiado. Actualiza la lista antes de volver a guardar."; }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException) { Status.Text = ex.Message; }
        finally { IsBusy = false; Form.IsEnabled = true; running = false; }
    }
    protected Button Action(string text, Func<Task> action) {
        var button = new Button { Text = text };
        button.Clicked += async (_, _) => await Run(action); return button;
    }
    protected static string Required(Entry entry, string field, int max) {
        var value = entry.Text?.Trim() ?? "";
        if (value.Length == 0 || value.Length > max) throw new InvalidOperationException($"Revisa {field} (máximo {max} caracteres).");
        return value;
    }
    protected static Label Heading(string text) => new() { Text = text, FontSize = 19, FontAttributes = FontAttributes.Bold };
    protected async Task<bool> ConfirmDeactivate() => await DisplayAlertAsync("Desactivar", "Se conservarán las referencias anteriores. ¿Desactivar este registro?", "Desactivar", "Cancelar");
}
