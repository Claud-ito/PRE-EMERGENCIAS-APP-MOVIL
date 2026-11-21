
using Univalle.Emergencias.Contracts.Diana;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Incidents;

namespace PRE_EMERGENCIAS_APP_MOVIL.Diana;

[QueryProperty(nameof(IncidentId), "incidentId")]
public sealed class InjuryRecordsPage : DianaPage
{
    private readonly Label incident = new();
    private readonly Picker incidentPicker = new() { Title = "Incidente atendido", ItemDisplayBinding = new Binding("Label") };
    private readonly CheckBox evaluated = new();
    private readonly VerticalStackLayout rows = new() { Spacing = 8 };
    public string IncidentId { get; set; } = "";
    public InjuryRecordsPage(DianaApiClient api, IDianaMobileSession session) : base(api, session, "Registro de accidentados", PermissionCodes.RecordInjury, physician: true) {
        incidentPicker.SelectedIndexChanged += async (_, _) => {
            if (incidentPicker.SelectedItem is not IncidentChoice choice) return;
            IncidentId = choice.Value.IncidentId.ToString(); evaluated.IsChecked = false;
            await Run(LoadRecordsAsync);
        };
        Form.Add(incidentPicker); Form.Add(incident);
        Form.Add(new Label { Text = "Base de la ficha: contenido clínico pendiente de validación de SySO." });
        Form.Add(Action("Actualizar fichas", LoadAsync)); Form.Add(rows);
        Form.Add(new Label { Text = "Ya atendí o evalué al accidentado." }); Form.Add(evaluated);
        Form.Add(Action("Crear ficha base", async () => {
            var id = GetIncident();
            if (!evaluated.IsChecked) throw new InvalidOperationException("Confirma primero la atención o evaluación.");
            await Api.SendAsync(HttpMethod.Post, $"incidents/{id}/injury-records", new CreateInjuryRecordInput(true));
            evaluated.IsChecked = false; await LoadRecordsAsync(); Status.Text = "Ficha base creada. Campos clínicos pendientes.";
        }));
    }
    private Guid GetIncident() => Guid.TryParse(IncidentId, out var id) && id != Guid.Empty ? id :
        throw new InvalidOperationException("Selecciona el incidente atendido.");
    protected override async Task LoadAsync() {
        var cases = await Api.GetAsync<List<InjuryIncidentView>>("injury-records/incidents");
        var choices = cases.Select(x => new IncidentChoice(x,
            $"{x.ModuleName} · {x.FloorName} · {x.OpenedAtUtc.ToOffset(TimeSpan.FromHours(-4)):dd/MM HH:mm} · {(x.Status == IncidentStatuses.Closed ? "cerrado" : "activo")}")).ToList();
        incidentPicker.ItemsSource = choices;
        incidentPicker.SelectedItem = choices.FirstOrDefault(x => x.Value.IncidentId.ToString() == IncidentId);
        if (choices.Count == 0) { rows.Clear(); incident.Text = "No hay incidentes registrados."; return; }
        await LoadRecordsAsync();
    }
    private async Task LoadRecordsAsync() {
        rows.Clear();
        if (!Guid.TryParse(IncidentId, out var id) || id == Guid.Empty) {
            incident.Text = "Selecciona el incidente que atendiste o evaluaste."; return;
        }
        incident.Text = $"Incidente: {id}";
        var records = await Api.GetAsync<List<InjuryRecordView>>($"incidents/{id}/injury-records");
        if (records.Count == 0) rows.Add(new Label { Text = "Sin fichas registradas. El incidente puede continuar sin accidentados." });
        foreach (var item in records) rows.Add(new Label { Text = $"Ficha {item.InjuryRecordId}\nRegistro (Bolivia): {item.CreatedAtUtc.ToOffset(TimeSpan.FromHours(-4)):dd/MM/yyyy HH:mm}\nMédico: {item.PhysicianUserId}" });
    }
    private sealed record IncidentChoice(InjuryIncidentView Value, string Label);
}
