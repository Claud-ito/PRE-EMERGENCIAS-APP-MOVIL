using Univalle.Emergencias.Contracts.Diana;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Locations;

namespace PRE_EMERGENCIAS_APP_MOVIL.Diana;

public sealed class LocationsPage : DianaPage
{
    private readonly Entry code = new() { Placeholder = "Código del módulo", MaxLength = 30 };
    private readonly Entry name = new() { Placeholder = "Nombre del módulo", MaxLength = 150 };
    private readonly Entry floorName = new() { Placeholder = "Nombre del piso", MaxLength = 100 };
    private readonly Label selection = new() { Text = "Selecciona un módulo para consultar sus pisos." };
    private readonly VerticalStackLayout modules = new() { Spacing = 8 };
    private readonly Label brigadeInfo = new();
    private readonly VerticalStackLayout floors = new() { Spacing = 8 };
    private ModuleLocation? selectedModule;
    private FloorLocation? selectedFloor;
    public LocationsPage(DianaApiClient api, IDianaMobileSession session) : base(api, session, "Módulos y pisos", PermissionCodes.ManageLocations) {
        Form.Add(Action("Actualizar", LoadAsync)); Form.Add(Heading("Módulos")); Form.Add(modules);
        Form.Add(code); Form.Add(name);
        Form.Add(Action("Nuevo módulo", () => { selectedModule = null; selectedFloor = null; code.Text = name.Text = floorName.Text = ""; selection.Text = "Nuevo módulo"; floors.Clear(); brigadeInfo.Text = ""; return Task.CompletedTask; }));
        Form.Add(Action("Guardar módulo", SaveModule));
        Form.Add(Action("Desactivar módulo seleccionado", async () => { if (selectedModule is not { IsActive: true }) return; if (!await ConfirmDeactivate()) return; await Api.SendAsync(HttpMethod.Delete, $"locations/modules/{selectedModule.ModuleId}"); selectedModule = null; code.Text = name.Text = ""; await LoadAsync(); }));
        Form.Add(Action("Reactivar módulo seleccionado", async () => {
            if (selectedModule is not { IsActive: false }) throw new InvalidOperationException("Selecciona un módulo inactivo.");
            await Api.SendAsync(HttpMethod.Post, $"locations/modules/{selectedModule.ModuleId}/reactivate");
            await LoadAsync(); Status.Text = "Módulo reactivado. Sus pisos activos vuelven a estar disponibles para reportar.";
        }));
        Form.Add(Heading("Brigada del módulo")); Form.Add(brigadeInfo);
        Form.Add(Heading("Pisos")); Form.Add(selection); Form.Add(floors); Form.Add(floorName);
        Form.Add(Action("Nuevo piso", () => { selectedFloor = null; floorName.Text = ""; return Task.CompletedTask; }));
        Form.Add(Action("Guardar piso", SaveFloor));
        Form.Add(Action("Desactivar piso seleccionado", async () => { if (selectedFloor is not { IsActive: true }) return; if (!await ConfirmDeactivate()) return; await Api.SendAsync(HttpMethod.Delete, $"locations/floors/{selectedFloor.FloorId}"); selectedFloor = null; floorName.Text = ""; await LoadFloors(); }));
        Form.Add(Action("Reactivar piso seleccionado", async () => {
            if (selectedFloor is not { IsActive: false }) throw new InvalidOperationException("Selecciona un piso inactivo.");
            await Api.SendAsync(HttpMethod.Post, $"locations/floors/{selectedFloor.FloorId}/reactivate");
            selectedFloor = null; floorName.Text = ""; await LoadFloors(); Status.Text = "Piso reactivado.";
        }));
    }
    protected override async Task LoadAsync() {
        var rows = await Api.GetAsync<List<ModuleLocation>>("locations/modules?includeInactive=true"); modules.Clear();
        foreach (var row in rows) modules.Add(Action($"{row.Code} · {row.Name}" + (row.IsActive ? "" : " (inactivo)"), async () => {
            selectedModule = row; selectedFloor = null; code.Text = row.Code; name.Text = row.Name; floorName.Text = ""; await LoadFloors();
        }));
        if (rows.Count == 0) modules.Add(new Label { Text = "No hay módulos registrados." });
        if (selectedModule is not null) selectedModule = rows.SingleOrDefault(x => x.ModuleId == selectedModule.ModuleId);
        await LoadFloors();
    }
    private async Task LoadFloors() {
        floors.Clear(); brigadeInfo.Text = "";
        if (selectedModule is null) { selection.Text = "Selecciona un módulo."; return; }
        selection.Text = selectedModule.Name + (selectedModule.IsActive ? "" : " (inactivo)");
        var rows = await Api.GetAsync<List<FloorLocation>>($"locations/modules/{selectedModule.ModuleId}/floors?includeInactive=true");
        foreach (var row in rows) floors.Add(Action(row.Name + (row.IsActive ? "" : " (inactivo)"), () => { selectedFloor = row; floorName.Text = row.Name; return Task.CompletedTask; }));
        if (rows.Count == 0) floors.Add(new Label { Text = "Este módulo no tiene pisos registrados." });
        var user = await Session.GetCurrentAsync();
        if (user?.Permissions.Contains(PermissionCodes.ManageBrigades) != true) return;
        if (!selectedModule.IsActive) { brigadeInfo.Text = "Módulo inactivo. Se conserva el historial de asignaciones."; return; }
        try {
            var status = await Api.GetAsync<ModuleBrigadeStatus>($"brigades/modules/{selectedModule.ModuleId}/leader");
            brigadeInfo.Text = status.Leader is null ? "Este módulo no tiene un líder activo asignado." :
                $"Brigada: {status.Leader.BrigadeName}\nLíder: {status.Leader.LeaderName}\n" +
                string.Join("\n", status.Leader.Members.Select(m => $"{m.DisplayName} · {m.Specialty}"));
        } catch (InvalidOperationException ex) { brigadeInfo.Text = ex.Message; }
    }
    private async Task SaveModule() {
        var input = new ModuleInput(Required(code, "el código", 30), Required(name, "el nombre", 150));
        if (selectedModule is { IsActive: false }) throw new InvalidOperationException("El módulo está inactivo.");
        await Api.SendAsync(selectedModule is null ? HttpMethod.Post : HttpMethod.Put,
            selectedModule is null ? "locations/modules" : $"locations/modules/{selectedModule.ModuleId}", input);
        selectedModule = null; code.Text = name.Text = ""; await LoadAsync(); Status.Text = "Módulo guardado. Selecciónalo para gestionar sus pisos.";
    }
    private async Task SaveFloor() {
        if (selectedModule is not { IsActive: true }) throw new InvalidOperationException("Selecciona un módulo activo.");
        if (selectedFloor is { IsActive: false }) throw new InvalidOperationException("El piso está inactivo.");
        var input = new FloorInput(Required(floorName, "el piso", 100));
        await Api.SendAsync(selectedFloor is null ? HttpMethod.Post : HttpMethod.Put,
            selectedFloor is null ? $"locations/modules/{selectedModule.ModuleId}/floors" : $"locations/floors/{selectedFloor.FloorId}", input);
        selectedFloor = null; floorName.Text = ""; await LoadFloors(); Status.Text = "Piso guardado.";
    }
}
