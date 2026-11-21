using System.Net.Http;
using Univalle.Emergencias.Contracts.Suggestions;

namespace PRE_EMERGENCIAS_APP_MOVIL.Suggestions;

public sealed class SuggestionsPage : ContentPage
{
    private readonly SuggestionApiClient _api;
    private readonly Picker _incidentPicker = new() { Title = "Incidente cerrado" };
    private readonly Label _status = new();
    private readonly Label _deadline = new();
    private readonly VerticalStackLayout _messages = new() { Spacing = 8 };
    private readonly Editor _text = new()
    {
        Placeholder = "Escribe una sugerencia",
        MaxLength = 1000,
        AutoSize = EditorAutoSizeOption.TextChanges
    };
    private readonly Button _send = new() { Text = "Enviar sugerencia", IsEnabled = false };
    private bool _loading;

    public SuggestionsPage(SuggestionApiClient api)
    {
        _api = api;
        Title = "Sugerencias";
        _incidentPicker.ItemDisplayBinding = new Binding(nameof(ClosedChoice.Display));
        _incidentPicker.SelectedIndexChanged += async (_, _) => await LoadThreadAsync();
        var refresh = new Button { Text = "Actualizar incidentes" };
        refresh.Clicked += async (_, _) => await LoadIncidentsAsync();
        _send.Clicked += OnSendClicked;
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24, 28),
                Spacing = 14,
                Children =
                {
                    new Label { Text = "Buzón de sugerencias", FontSize = 24,
                        FontAttributes = FontAttributes.Bold },
                    new Label { Text = "Puedes comentar durante seis horas después del cierre. La lectura continúa después." },
                    _incidentPicker, refresh, _status, _deadline, _messages, _text, _send
                }
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadIncidentsAsync();
    }

    private async Task LoadIncidentsAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            var selectedId = (_incidentPicker.SelectedItem as ClosedChoice)?.IncidentId;
            var incidents = await _api.ListAsync();
            var choices = incidents.Select(x => new ClosedChoice(
                x.IncidentId, $"{x.ModuleName} · {x.FloorName} · {Bolivia(x.ClosedAtUtc):dd/MM HH:mm}"))
                .ToList();
            _incidentPicker.ItemsSource = choices;
            _incidentPicker.SelectedIndex = choices.FindIndex(x => x.IncidentId == selectedId);
            _status.Text = choices.Count == 0
                ? "No hay incidentes cerrados disponibles."
                : "Selecciona un incidente para ver su buzón.";
            if (choices.Count == 1 && _incidentPicker.SelectedIndex < 0)
                _incidentPicker.SelectedIndex = 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            _status.Text = ex is HttpRequestException ? "No se pudo conectar con la API." : ex.Message;
        }
        finally { _loading = false; }
    }

    private async Task LoadThreadAsync()
    {
        if (_incidentPicker.SelectedItem is not ClosedChoice selected)
        {
            _messages.Clear();
            _send.IsEnabled = false;
            _deadline.Text = string.Empty;
            return;
        }
        try
        {
            var thread = await _api.GetAsync(selected.IncidentId);
            _messages.Clear();
            foreach (var suggestion in thread.Suggestions)
            {
                _messages.Add(new Label
                {
                    Text = $"{suggestion.AuthorName} · {Bolivia(suggestion.CreatedAtUtc):dd/MM/yyyy HH:mm}\n{suggestion.Text}"
                });
            }
            if (thread.Suggestions.Count == 0)
                _messages.Add(new Label { Text = "Todavía no hay sugerencias." });
            _deadline.Text = $"Plazo de escritura: {Bolivia(thread.WriteUntilUtc):dd/MM/yyyy HH:mm} (Bolivia)";
            _text.IsVisible = thread.CanWrite;
            _send.IsVisible = thread.CanWrite;
            _send.IsEnabled = thread.CanWrite;
            _status.Text = thread.CanWrite
                ? "El buzón está abierto para comentar."
                : "El plazo terminó. Las sugerencias siguen disponibles para lectura.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            _status.Text = ex is HttpRequestException ? "No se pudo conectar con la API." : ex.Message;
        }
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        if (_incidentPicker.SelectedItem is not ClosedChoice selected) return;
        _send.IsEnabled = false;
        try
        {
            await _api.CreateAsync(selected.IncidentId, _text.Text?.Trim() ?? string.Empty);
            _text.Text = string.Empty;
            await LoadThreadAsync();
            _status.Text = "Sugerencia registrada.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            _status.Text = ex is HttpRequestException ? "No se pudo conectar con la API." : ex.Message;
            await LoadThreadAsync();
        }
        finally { _send.IsEnabled = _send.IsVisible; }
    }

    private static DateTimeOffset Bolivia(DateTimeOffset utc) =>
        utc.ToUniversalTime().ToOffset(TimeSpan.FromHours(-4));

    private sealed record ClosedChoice(Guid IncidentId, string Display);
}
