using Microsoft.Maui.ApplicationModel;

namespace PRE_EMERGENCIAS_APP_MOVIL.Identity;

public sealed class AccountPage : ContentPage
{
    private readonly MobileSession _session;
    private readonly Entry _username = new() { Placeholder = "Usuario" };
    private readonly Entry _password = new() { Placeholder = "Contraseña", IsPassword = true };
    private readonly Label _profile = new() { FontSize = 18 };
    private readonly Label _status = new() { HorizontalTextAlignment = TextAlignment.Center };
    private readonly Button _login = new() { Text = "Ingresar" };
    private readonly Button _logout = new() { Text = "Cerrar sesión" };
    private readonly Button _restore = new() { Text = "Comprobar sesión guardada" };

    public AccountPage(MobileSession session)
    {
        _session = session;
        Title = "Cuenta";
        _login.Clicked += OnLoginClicked;
        _logout.Clicked += OnLogoutClicked;
        _restore.Clicked += OnRestoreClicked;
        _session.Changed += () => MainThread.BeginInvokeOnMainThread(Update);
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24, 36),
                Spacing = 16,
                Children =
                {
                    new Label { Text = "Acceso", FontSize = 26, FontAttributes = FontAttributes.Bold },
                    _profile, _username, _password, _login, _logout, _restore, _status
                }
            }
        };
        Update();
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        _login.IsEnabled = false;
        _status.Text = "Comprobando cuenta...";
        try
        {
            var result = await _session.LoginAsync(_username.Text, _password.Text);
            _status.Text = result.Message;
            if (result.Succeeded) _password.Text = string.Empty;
        }
        finally
        {
            _login.IsEnabled = true;
        }
    }

    private async void OnLogoutClicked(object? sender, EventArgs e)
    {
        _logout.IsEnabled = false;
        await _session.SignOutAsync();
        _status.Text = "Sesión cerrada.";
        _logout.IsEnabled = true;
    }

    private async void OnRestoreClicked(object? sender, EventArgs e)
    {
        _restore.IsEnabled = false;
        var restored = await _session.RestoreAsync();
        _status.Text = restored ? "Sesión activa." : "No se pudo recuperar una sesión activa.";
        _restore.IsEnabled = true;
    }

    private void Update()
    {
        var user = _session.CurrentUser;
        var signedIn = user is not null;
        _profile.Text = signedIn
            ? $"{_session.DisplayName} · {user!.Role}"
            : "Ingresa con tu cuenta para usar el sistema.";
        _username.IsVisible = !signedIn;
        _password.IsVisible = !signedIn;
        _login.IsVisible = !signedIn;
        _logout.IsVisible = signedIn;
        _restore.IsVisible = !signedIn;
    }
}
