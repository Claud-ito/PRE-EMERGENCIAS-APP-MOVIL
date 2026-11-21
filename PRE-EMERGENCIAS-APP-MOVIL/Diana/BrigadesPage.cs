using Univalle.Emergencias.Contracts.Diana;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Locations;

namespace PRE_EMERGENCIAS_APP_MOVIL.Diana;

public sealed class BrigadesPage : DianaPage
{
    private readonly Entry name = new() { Placeholder = "Nombre de la brigada", MaxLength = 150 };
    private readonly Picker leader = new() { Title = "Líder", ItemDisplayBinding = new Binding(nameof(BrigadeUser.DisplayName)) };
    private readonly VerticalStackLayout rows = new() { Spacing = 8 };
    private readonly VerticalStackLayout memberRows = new() { Spacing = 8 };
    private readonly VerticalStackLayout moduleRows = new() { Spacing = 8 };
    private readonly Dictionary<Guid, (CheckBox Check, Picker Specialty)> members = [];
    private readonly Dictionary<Guid, CheckBox> modules = [];
    private BrigadeView? selected;
    private List<BrigadeUser> users = [];
    private string[] specialties = [];
    public BrigadesPage(DianaApiClient api, IDianaMobileSession session) : base(api, session, "Brigadas y líderes", PermissionCodes.ManageBrigades) {
        Form.Add(Action("Actualizar", LoadAsync)); Form.Add(rows); Form.Add(name); Form.Add(leader);
        Form.Add(Heading("Integrantes y especialidad")); Form.Add(memberRows);
        Form.Add(Heading("Módulos asignados")); Form.Add(moduleRows);
        Form.Add(Action("Nueva brigada", () => { Select(null); return Task.CompletedTask; }));
        Form.Add(Action("Guardar brigada", Save));
        Form.Add(Action("Desactivar seleccionada", async () => {
            if (selected is not { IsActive: true }) return; if (!await ConfirmDeactivate()) return;
            await Api.SendAsync(HttpMethod.Delete, $"brigades/{selected.BrigadeId}"); selected = null; await LoadAsync();
        }));
    }
    protected override async Task LoadAsync() {
        // No se ofrecen especialidades supuestas ni usuarios ficticios si falta una dependencia.
        specialties = await Api.GetAsync<string[]>("brigades/specialties");
        users = await Api.GetAsync<List<BrigadeUser>>("brigades/users");
        var locations = await Api.GetAsync<List<ModuleLocation>>("locations/modules");
        var brigades = await Api.GetAsync<List<BrigadeView>>("brigades?includeInactive=true");
        leader.ItemsSource = users.Where(x => x.Role == UserRole.LiderBrigada).ToList();
        rows.Clear(); memberRows.Clear(); moduleRows.Clear(); members.Clear(); modules.Clear();
        foreach (var row in brigades) rows.Add(Action(row.Name + (row.IsActive ? "" : " (inactiva)"), () => { Select(row); return Task.CompletedTask; }));
        if (brigades.Count == 0) rows.Add(new Label { Text = "No hay brigadas registradas." });
        foreach (var user in users) {
            var check = new CheckBox(); var specialty = new Picker { Title = "Especialidad", ItemsSource = specialties };
            var group = new VerticalStackLayout { Spacing = 4 }; group.Add(new Label { Text = user.DisplayName }); group.Add(check); group.Add(specialty);
            memberRows.Add(group); members[user.UserId] = (check, specialty);
        }
        foreach (var location in locations) {
            var check = new CheckBox(); var group = new HorizontalStackLayout { Spacing = 8 };
            group.Add(check); group.Add(new Label { Text = location.Name, VerticalOptions = LayoutOptions.Center }); moduleRows.Add(group); modules[location.ModuleId] = check;
        }
        Select(selected is null ? null : brigades.SingleOrDefault(x => x.BrigadeId == selected.BrigadeId));
    }
    private void Select(BrigadeView? brigade) {
        selected = brigade; name.Text = brigade?.Name ?? "";
        leader.SelectedItem = users.SingleOrDefault(x => x.UserId == brigade?.LeaderUserId);
        foreach (var (id, controls) in members) {
            var member = brigade?.Members.SingleOrDefault(x => x.UserId == id);
            controls.Check.IsChecked = member is not null; controls.Specialty.SelectedItem = member?.Specialty;
        }
        foreach (var (id, check) in modules) check.IsChecked = brigade?.ModuleIds.Contains(id) == true;
    }
    private async Task Save() {
        if (selected is { IsActive: false }) throw new InvalidOperationException("La brigada está inactiva.");
        if (leader.SelectedItem is not BrigadeUser chosen) throw new InvalidOperationException("Selecciona un líder activo.");
        var selectedMembers = members.Where(x => x.Value.Check.IsChecked).Select(x => new BrigadeMemberInput(x.Key, x.Value.Specialty.SelectedItem as string ?? "")).ToArray();
        if (!selectedMembers.Any(x => x.UserId == chosen.UserId) || selectedMembers.Any(x => string.IsNullOrWhiteSpace(x.Specialty))) throw new InvalidOperationException("Incluye al líder y selecciona la especialidad de cada integrante.");
        var input = new BrigadeInput(Required(name, "el nombre", 150), chosen.UserId, selectedMembers, modules.Where(x => x.Value.IsChecked).Select(x => x.Key).ToArray());
        await Api.SendAsync(selected is null ? HttpMethod.Post : HttpMethod.Put, selected is null ? "brigades" : $"brigades/{selected.BrigadeId}", input);
        selected = null; await LoadAsync(); Status.Text = "Brigada guardada.";
    }
}
