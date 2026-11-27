using PRE_EMERGENCIAS_APP_MOVIL.Identity;
using Univalle.Emergencias.Contracts.Turns;

namespace PRE_EMERGENCIAS_APP_MOVIL.Coordination;

/// <summary>
/// RF09: horarios, excepciones y AUTO del encargado. La convocatoria al líder y el relevo se hacen
/// en la tarjeta de cada caso, en la pestaña Incidentes.
/// </summary>
public sealed class TurnPage : ContentPage
{
    private readonly CoordinationClient api;
    private readonly MobileSession session;
    private readonly VerticalStackLayout form = new() { Padding = 16, Spacing = 10 };
    private readonly Label status = new();
    private readonly DatePicker day = new() { Date = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-4)).Date };
    private readonly TimePicker start = new() { Format = "HH:mm" };
    private readonly TimePicker end = new() { Format = "HH:mm" };
    private readonly Picker schedules = new() { Title = "Horario propio" };
    private readonly Label calendar = new();
    private ScheduleExceptionDto? datedException;
    private readonly Label exceptionLabel = new();
    private readonly Switch auto = new() { OnColor = Color.FromArgb("#2E9E4F"), ThumbColor = Colors.White };
    private readonly Label autoLabel = new() { VerticalOptions = LayoutOptions.Center };
    private readonly RefreshView refresher = new();
    private bool showingAuto;
    private Guid? loadedUser;
    private bool busy;
    private bool visible;

    public TurnPage(CoordinationClient api, MobileSession session)
    {
        this.api = api;
        this.session = session;
        Title = "Turnos";
        refresher.Content = new ScrollView { Content = form };
        refresher.Refreshing += async (_, _) => { await Run(Refresh); refresher.IsRefreshing = false; };
        Content = refresher;
        form.Add(status);
        form.Add(new Label { Text = "Modo AUTO", FontAttributes = FontAttributes.Bold, FontSize = 17 });
        form.Add(new HorizontalStackLayout { Spacing = 12, Children = { auto, autoLabel } });
        form.Add(new Label { Text = "Encendido: en tus incidentes nuevos, si no registras la convocatoria en 120 s, el sistema avisa al líder. Apagarlo cancela los plazos pendientes.", FontSize = 12 });
        auto.Toggled += async (_, e) =>
        {
            if (showingAuto) return;
            var previous = !e.Value;
            if (busy || session.CurrentUser is null) { ShowAuto(previous); return; }
            autoLabel.Text = e.Value ? "AUTO encendido" : "AUTO apagado";
            await Run(() => Change(HttpMethod.Put, "coordination/auto", new AutoSetting(e.Value)));
            // Si la API rechazó el cambio, el switch vuelve a mostrar el estado guardado.
            if (status.Text.Length > 0) ShowAuto(previous);
        };
        form.Add(new Label { Text = "Calendario compartido · hora de Bolivia", FontAttributes = FontAttributes.Bold, FontSize = 17 });
        form.Add(day);
        form.Add(calendar);
        day.DateSelected += async (_, _) => await Run(Refresh);
        form.Add(schedules);
        form.Add(new Label { Text = "Inicio y fin del turno" });
        form.Add(start);
        form.Add(end);
        ActionButton("Crear turno semanal para este día", () => Change(HttpMethod.Post, "turns", Schedule()));
        ActionButton("Actualizar horario seleccionado", () => Change(HttpMethod.Put, OwnSchedule(), Schedule()));
        ActionButton("Desactivar horario seleccionado", () => Change(HttpMethod.Delete, OwnSchedule()));
        form.Add(exceptionLabel);
        ActionButton("Guardar ausencia en esta fecha", () => SaveException(false));
        ActionButton("Guardar horario especial en esta fecha", () => SaveException(true));
        ActionButton("Quitar excepción de esta fecha", () => Change(HttpMethod.Delete, ExceptionPath()));
        session.Changed += () => Dispatcher.Dispatch(() =>
        {
            loadedUser = null;
            Clear();
            if (visible) _ = Run(Refresh);
        });
    }

    protected override async void OnAppearing() { base.OnAppearing(); visible = true; await Run(Refresh); }
    protected override void OnDisappearing() { visible = false; base.OnDisappearing(); }

    private void Clear()
    {
        schedules.ItemsSource = null;
        datedException = null;
        ShowAuto(false);
        calendar.Text = status.Text = exceptionLabel.Text = "";
    }

    private DateOnly Date => DateOnly.FromDateTime(day.Date ?? DateTime.Today);
    private SaveWorkScheduleRequest Schedule() => new(Date.DayOfWeek,
        TimeOnly.FromTimeSpan(start.Time ?? TimeSpan.Zero), TimeOnly.FromTimeSpan(end.Time ?? TimeSpan.Zero));
    private string ExceptionPath() => datedException is { } x && x.Date == Date ? $"turns/exceptions/{x.ScheduleExceptionId}"
        : throw new InvalidOperationException("No hay una excepción para esta fecha.");
    private Task SaveException(bool available) => Change(datedException?.Date == Date ? HttpMethod.Put : HttpMethod.Post,
        datedException?.Date == Date ? ExceptionPath() : "turns/exceptions",
        new CreateScheduleExceptionRequest(Date, available, available ? Schedule().StartTime : null,
            available ? Schedule().EndTime : null, null));
    private string OwnSchedule() => $"turns/{Selected<WorkScheduleDto>(schedules).WorkScheduleId}";
    private void ShowAuto(bool enabled)
    {
        showingAuto = true;
        auto.IsToggled = enabled;
        autoLabel.Text = enabled ? "AUTO encendido" : "AUTO apagado";
        showingAuto = false;
    }
    private static T Selected<T>(Picker picker) where T : class => picker.SelectedItem as T
        ?? throw new InvalidOperationException("Selecciona un elemento primero.");

    private void ActionButton(string label, Func<Task> action)
    {
        var button = new Button { Text = label };
        button.Clicked += async (_, _) => await Run(action);
        form.Add(button);
    }

    private async Task Run(Func<Task> action)
    {
        if (busy || session.CurrentUser is null) return;
        busy = true;
        try { status.Text = ""; await action(); }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { busy = false; }
    }

    private async Task Change(HttpMethod method, string route, object? payload = null)
    {
        if (loadedUser != session.CurrentUser?.UserId) throw new InvalidOperationException("Actualiza los datos de la sesión.");
        await api.ChangeAsync(method, route, payload);
        await Refresh();
    }

    private async Task Refresh()
    {
        var user = session.CurrentUser?.UserId;
        if (user is null) return;
        var intervals = await api.GetAsync<List<CalendarInterval>>($"turns/calendar?date={Date:yyyy-MM-dd}");
        var own = await api.GetAsync<List<WorkScheduleDto>>("turns/mine");
        var exceptions = await api.GetAsync<List<ScheduleExceptionDto>>("turns/exceptions/mine");
        var options = await api.GetAsync<List<CoordinatorOption>>("coordination/coordinators");
        var setting = await api.GetAsync<AutoSetting>("coordination/auto");
        if (user != session.CurrentUser?.UserId) return;
        loadedUser = user;
        datedException = exceptions.SingleOrDefault(x => x.Date == Date);
        exceptionLabel.Text = datedException is null ? "Sin excepción propia" : datedException.IsAvailable
            ? $"Horario especial {datedException.StartTime:HH:mm}–{datedException.EndTime:HH:mm}" : "Ausencia registrada";
        string Name(Guid? id) => id is null ? "Sin cobertura" : id == user ? "Mi turno" :
            options.FirstOrDefault(x => x.UserId == id)?.FullName ?? id.ToString()!;
        var offset = TimeSpan.FromHours(-4);
        calendar.Text = string.Join("\n", intervals.Select(x =>
            $"{x.StartsAtUtc.ToOffset(offset):HH:mm} – {x.EndsAtUtc.ToOffset(offset):HH:mm}  {Name(x.CoordinatorUserId)}"));
        ShowAuto(setting.Enabled);
        schedules.ItemDisplayBinding = new Binding(".", stringFormat: "{0}", converter: new ScheduleLabel());
        schedules.ItemsSource = own;
    }

    private sealed class ScheduleLabel : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            value is WorkScheduleDto x ? $"{culture.DateTimeFormat.GetDayName(x.DayOfWeek)} {x.StartTime:HH:mm}–{x.EndTime:HH:mm}" : "";
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
