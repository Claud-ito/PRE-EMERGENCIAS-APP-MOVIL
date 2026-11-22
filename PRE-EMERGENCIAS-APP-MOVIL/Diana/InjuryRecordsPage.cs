
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
    private readonly CheckBox useTestDraft = new();
    private readonly Entry alias = new() { Placeholder = "Alias ficticio", MaxLength = 100 };
    private readonly Entry age = new() { Placeholder = "Edad de ensayo (opcional)", Keyboard = Keyboard.Numeric };
    private readonly Editor observations = new() { Placeholder = "Observaciones de ensayo", MaxLength = 1500, HeightRequest = 80 };
    private readonly Editor care = new() { Placeholder = "Atención de ensayo", MaxLength = 1500, HeightRequest = 80 };
    private readonly Editor report = new() { Placeholder = "Informe de ensayo", MaxLength = 1500, HeightRequest = 80 };
    private readonly Label reportView = new();
    private readonly VerticalStackLayout rows = new() { Spacing = 8 };
    public string IncidentId { get; set; } = "";
    public InjuryRecordsPage(DianaApiClient api, IDianaMobileSession session) : base(api, session, "Registro de accidentados", PermissionCodes.RecordInjury, physician: true) {
        incidentPicker.SelectedIndexChanged += async (_, _) => {
            if (incidentPicker.SelectedItem is not IncidentChoice choice) return;
            IncidentId = choice.Value.IncidentId.ToString(); evaluated.IsChecked = false;
            await Run(LoadRecordsAsync);
        };
        Form.Add(incidentPicker); Form.Add(incident);
        Form.Add(new Label { Text = "Plantilla provisional de prueba. Usar solo personas y datos ficticios; sin validez clínica. La definición final corresponde a SySO." });
        Form.Add(new Label { Text = "Visibilidad de ensayo: solo Médico. Sin médico no se registra una ficha en nombre de otra persona; el incidente puede continuar sin fichas." });
        Form.Add(Action("Actualizar fichas", LoadAsync)); Form.Add(rows);
        Form.Add(new Label { Text = "Agregar ficha clínica de ensayo" }); Form.Add(useTestDraft);
        var testFields = new VerticalStackLayout { Spacing = 8, IsVisible = false };
        testFields.Add(alias); testFields.Add(age); testFields.Add(observations); testFields.Add(care); testFields.Add(report);
        useTestDraft.CheckedChanged += (_, args) => testFields.IsVisible = args.Value;
        Form.Add(Action("Cargar datos ficticios", () => {
            useTestDraft.IsChecked = true; alias.Text = "PERSONA-DEMO-001"; age.Text = "22";
            observations.Text = "Observación ficticia para comprobar la ficha.";
            care.Text = "Atención de ensayo registrada por el médico.";
            report.Text = "Informe de prueba sin validez clínica. No corresponde a una persona real.";
            evaluated.IsChecked = false; return Task.CompletedTask;
        }));
        Form.Add(testFields);
        Form.Add(new Label { Text = "Ya atendí o evalué al accidentado." }); Form.Add(evaluated);
        Form.Add(Action("Guardar ficha", async () => {
            var id = GetIncident();
            if (!evaluated.IsChecked) throw new InvalidOperationException("Confirma primero la atención o evaluación.");
            TestClinicalDraft? draft = null;
            if (useTestDraft.IsChecked) {
                int? testAge = null;
                if (!string.IsNullOrWhiteSpace(age.Text)) {
                    if (!int.TryParse(age.Text, out var value) || value < 0 || value > 120)
                        throw new InvalidOperationException("La edad de ensayo debe estar entre 0 y 120.");
                    testAge = value;
                }
                draft = new(Required(alias, "el alias ficticio", 100), testAge,
                    TestText(observations), TestText(care), TestText(report));
            }
            await Api.SendAsync(HttpMethod.Post, $"incidents/{id}/injury-records", new CreateInjuryRecordInput(true, draft));
            evaluated.IsChecked = false; await LoadRecordsAsync(); Status.Text = draft is null ? "Ficha base creada." : "Ficha de ensayo guardada. Sin validez clínica.";
        }));
        Form.Add(reportView);
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
        rows.Clear(); reportView.Text = "";
        if (!Guid.TryParse(IncidentId, out var id) || id == Guid.Empty) {
            incident.Text = "Selecciona el incidente que atendiste o evaluaste."; return;
        }
        incident.Text = $"Incidente: {id}";
        var records = await Api.GetAsync<List<InjuryRecordView>>($"incidents/{id}/injury-records");
        if (records.Count == 0) rows.Add(new Label { Text = "Sin fichas registradas. El incidente puede continuar sin accidentados." });
        foreach (var item in records) {
            rows.Add(new Label { Text = $"Ficha {item.InjuryRecordId}\nRegistro (Bolivia): {item.CreatedAtUtc.ToOffset(TimeSpan.FromHours(-4)):dd/MM/yyyy HH:mm}\nMédico: {item.PhysicianUserId}" });
            if (item.TestClinicalDraft is not null) rows.Add(Action("Ver informe de ensayo", async () => {
                var result = await Api.GetAsync<TestClinicalReport>($"incidents/{id}/injury-records/{item.InjuryRecordId}/test-report");
                reportView.Text = result.Text;
            }));
        }
    }
    private static string TestText(Editor editor) => !string.IsNullOrWhiteSpace(editor.Text) && editor.Text.Trim().Length <= 1500
        ? editor.Text.Trim() : throw new InvalidOperationException("Completa las observaciones, atención e informe de ensayo (máximo 1500 caracteres cada uno).");
    private sealed record IncidentChoice(InjuryIncidentView Value, string Label);
}
