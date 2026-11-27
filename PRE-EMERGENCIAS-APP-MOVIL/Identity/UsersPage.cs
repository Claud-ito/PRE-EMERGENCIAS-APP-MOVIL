using System.Net.Http;
using Univalle.Emergencias.Contracts.Identity;

namespace PRE_EMERGENCIAS_APP_MOVIL.Identity;

public sealed class UsersPage : ContentPage
{
    private readonly UserApiClient _api;
    private readonly Entry _username = new() { Placeholder = "Usuario único" };
    private readonly Entry _email = new() { Placeholder = "Correo", Keyboard = Keyboard.Email };
    private readonly Entry _fullName = new() { Placeholder = "Nombre completo" };
    private readonly Entry _password = new() { Placeholder = "Contraseña nueva (mínimo 12)", IsPassword = true };
    private readonly Picker _role = new() { Title = "Rol" };
    private readonly Switch _includeInactive = new();
    private readonly Button _save = new() { Text = "Crear cuenta" };
    private readonly Button _deactivate = new() { Text = "Desactivar cuenta", IsVisible = false };
    private readonly Button _reactivate = new() { Text = "Reactivar cuenta", IsVisible = false };
    private readonly Label _status = new();
    private readonly VerticalStackLayout _list = new() { Spacing = 8 };
    private Guid? _selectedId;

    public UsersPage(UserApiClient api)
    {
        _api = api;
        Title = "Usuarios";
        foreach (var name in new[]
        {
            "Comunidad/Testigo", "SySO", "Mantenimiento",
            "Líder de brigada", "Brigadista", "Médico"
        }) _role.Items.Add(name);
        _role.SelectedIndex = 0;
        _save.Clicked += OnSaveClicked;
        _deactivate.Clicked += OnDeactivateClicked;
        _reactivate.Clicked += OnReactivateClicked;
        _includeInactive.Toggled += async (_, _) => await RefreshAsync();
        var newButton = new Button { Text = "Nueva cuenta" };
        newButton.Clicked += (_, _) => ClearForm();
        var refreshButton = new Button { Text = "Actualizar lista" };
        refreshButton.Clicked += async (_, _) => await RefreshAsync();
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24, 28),
                Spacing = 12,
                Children =
                {
                    new Label { Text = "Gestión de cuentas", FontSize = 24,
                        FontAttributes = FontAttributes.Bold },
                    _username, _email, _fullName, _role, _password,
                    newButton, _save, _deactivate, _reactivate, _status,
                    new HorizontalStackLayout
                    {
                        Spacing = 10,
                        Children = { new Label { Text = "Mostrar inactivas" }, _includeInactive }
                    },
                    refreshButton, _list
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var users = await _api.ListAsync(_includeInactive.IsToggled);
            _list.Clear();
            foreach (var user in users)
            {
                var button = new Button
                {
                    Text = $"{user.FullName} · {user.Role}" + (user.IsActive ? "" : " (inactiva)")
                };
                button.Clicked += (_, _) => Select(user);
                _list.Add(button);
            }
            if (users.Count == 0) _list.Add(new Label { Text = "No hay cuentas para mostrar." });
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            _status.Text = ex is HttpRequestException
                ? "No se pudo conectar con la API."
                : ex.Message;
        }
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        _save.IsEnabled = false;
        try
        {
            var role = (UserRole)_role.SelectedIndex;
            if (_selectedId is Guid id)
            {
                await _api.UpdateAsync(id, new UpdateUserRequest(
                    _username.Text, _email.Text, _fullName.Text, role,
                    string.IsNullOrEmpty(_password.Text) ? null : _password.Text));
                _status.Text = "Cuenta actualizada.";
            }
            else
            {
                await _api.CreateAsync(new CreateUserRequest(
                    _username.Text, _email.Text, _fullName.Text, role, _password.Text));
                _status.Text = "Cuenta creada.";
            }
            _password.Text = string.Empty;
            await RefreshAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            _status.Text = ex is HttpRequestException ? "No se pudo conectar con la API." : ex.Message;
        }
        finally { _save.IsEnabled = true; }
    }

    private async void OnDeactivateClicked(object? sender, EventArgs e)
    {
        if (_selectedId is not Guid id) return;
        var confirmed = await DisplayAlertAsync(
            "Desactivar cuenta", "La cuenta perderá el acceso de inmediato. ¿Continuar?", "Sí", "Cancelar");
        if (!confirmed) return;
        _deactivate.IsEnabled = false;
        try
        {
            await _api.DeactivateAsync(id);
            ClearForm();
            _status.Text = "Cuenta desactivada.";
            await RefreshAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            _status.Text = ex is HttpRequestException ? "No se pudo conectar con la API." : ex.Message;
        }
        finally { _deactivate.IsEnabled = true; }
    }

    private async void OnReactivateClicked(object? sender, EventArgs e)
    {
        if (_selectedId is not Guid id) return;
        _reactivate.IsEnabled = false;
        try
        {
            await _api.ReactivateAsync(id);
            ClearForm();
            _status.Text = "Cuenta reactivada. Ya puede iniciar sesión.";
            await RefreshAsync();
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            _status.Text = ex is HttpRequestException ? "No se pudo conectar con la API." : ex.Message;
        }
        finally { _reactivate.IsEnabled = true; }
    }

    private void Select(UserView user)
    {
        _selectedId = user.UserId;
        _username.Text = user.Username;
        _email.Text = user.Email;
        _fullName.Text = user.FullName;
        _role.SelectedIndex = (int)user.Role;
        _password.Text = string.Empty;
        _save.Text = "Guardar cambios";
        _deactivate.IsVisible = user.IsActive;
        _reactivate.IsVisible = !user.IsActive;
        _status.Text = user.IsActive
            ? "Deja la contraseña vacía para conservarla."
            : "Cuenta inactiva: no puede iniciar sesión. Puedes reactivarla.";
        _save.IsVisible = user.IsActive;
    }

    private void ClearForm()
    {
        _selectedId = null;
        _username.Text = string.Empty;
        _email.Text = string.Empty;
        _fullName.Text = string.Empty;
        _password.Text = string.Empty;
        _role.SelectedIndex = 0;
        _save.Text = "Crear cuenta";
        _save.IsVisible = true;
        _deactivate.IsVisible = false;
        _reactivate.IsVisible = false;
        _status.Text = string.Empty;
    }
}
