using PRE_EMERGENCIAS_APP_MOVIL.Identity;
using Univalle.Emergencias.Contracts.Notifications;

namespace PRE_EMERGENCIAS_APP_MOVIL.Coordination;

public sealed class InboxPage : ContentPage
{
    private readonly CoordinationClient client;
    private readonly MobileSession session;
    private readonly VerticalStackLayout rows = new() { Spacing = 12, Padding = 16 };
    private readonly Label status = new();
    private CancellationTokenSource? polling;
    private bool visible;

    public InboxPage(CoordinationClient client, MobileSession session)
    {
        this.client = client;
        this.session = session;
        Title = "Avisos";
        Content = new ScrollView { Content = rows };
        session.Changed += () => Dispatcher.Dispatch(Restart);
    }

    protected override void OnAppearing() { base.OnAppearing(); visible = true; Restart(); }
    protected override void OnDisappearing() { visible = false; Restart(); base.OnDisappearing(); }

    private void Restart()
    {
        polling?.Cancel();
        rows.Clear();
        status.Text = "";
        rows.Add(status);
        if (visible && session.CurrentUser is not null)
        {
            polling = new();
            _ = PollAsync(polling);
        }
    }

    private async Task PollAsync(CancellationTokenSource source)
    {
        var ct = source.Token;
        List<InboxItem>? previous = null;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var items = await client.GetAsync<List<InboxItem>>("notifications/mine", ct);
                    ct.ThrowIfCancellationRequested();
                    status.Text = items.Count == 0 ? "Sin avisos." : "";
                    if (previous is null || !items.SequenceEqual(previous))
                    {
                        rows.Clear();
                        rows.Add(status);
                        foreach (var item in items) AddItem(item, ct);
                        previous = items;
                    }
                    foreach (var item in items.Where(x => x.DeliveryStatus == "Pending"))
                        await client.ChangeAsync(HttpMethod.Patch, $"notifications/{item.EventId}/delivered", ct: ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested) { status.Text = ex.Message; }
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { if (polling == source) polling = null; source.Dispose(); }
    }

    private void AddItem(InboxItem item, CancellationToken ct)
    {
        var kind = item.Type == "incident.auto" ? "Convocatoria AUTO" : "Alerta inicial";
        rows.Add(new Label { Text = $"{kind} · {item.RegisteredAtUtc.ToOffset(TimeSpan.FromHours(-4)):dd/MM HH:mm:ss}\nIncidente {item.IncidentId}" });
        if (item.ReadAtUtc.HasValue) { rows.Add(new Label { Text = "Leído" }); return; }
        var read = new Button { Text = "Marcar como leído" };
        read.Clicked += async (_, _) =>
        {
            try { await client.ChangeAsync(HttpMethod.Patch, $"notifications/{item.EventId}/read", ct: ct); }
            catch (Exception ex) { if (!ct.IsCancellationRequested) status.Text = ex.Message; }
        };
        rows.Add(read);
    }
}
