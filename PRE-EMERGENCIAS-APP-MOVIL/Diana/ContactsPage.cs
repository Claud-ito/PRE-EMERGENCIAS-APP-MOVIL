using Univalle.Emergencias.Contracts.Diana;
using Univalle.Emergencias.Contracts.Identity;

namespace PRE_EMERGENCIAS_APP_MOVIL.Diana;

public sealed class ContactsPage : DianaPage
{
    private readonly Entry name = new() { Placeholder = "Nombre", MaxLength = 150 };
    private readonly Entry phone = new() { Placeholder = "Teléfono", Keyboard = Keyboard.Telephone, MaxLength = 40 };
    private readonly Picker kind = new() { Title = "Tipo de contacto", ItemsSource = new[] { "Consultorio", "Centro médico", "Bomberos" } };
    private readonly VerticalStackLayout rows = new() { Spacing = 8 };
    private SupportContactView? selected;
    public ContactsPage(DianaApiClient api, IDianaMobileSession session) : base(api, session, "Contactos de apoyo", PermissionCodes.ManageSupportContacts) {
        Form.Add(Action("Actualizar", LoadAsync)); Form.Add(rows); Form.Add(name); Form.Add(kind); Form.Add(phone);
        Form.Add(Action("Nuevo contacto", () => { Reset(); return Task.CompletedTask; }));
        Form.Add(Action("Guardar", async () => {
            if (selected is { IsActive: false }) throw new InvalidOperationException("El contacto está inactivo.");
            if (kind.SelectedIndex < 0) throw new InvalidOperationException("Selecciona el tipo de contacto.");
            var input = new SupportContactInput(Required(name, "el nombre", 150), (SupportContactKind)kind.SelectedIndex, Required(phone, "el teléfono", 40));
            await Api.SendAsync(selected is null ? HttpMethod.Post : HttpMethod.Put, selected is null ? "support-contacts" : $"support-contacts/{selected.ContactId}", input);
            Reset(); await LoadAsync(); Status.Text = "Contacto guardado.";
        }));
        Form.Add(Action("Desactivar seleccionado", async () => {
            if (selected is not { IsActive: true }) return; if (!await ConfirmDeactivate()) return;
            await Api.SendAsync(HttpMethod.Delete, $"support-contacts/{selected.ContactId}"); Reset(); await LoadAsync();
        }));
    }
    private void Reset() { selected = null; name.Text = phone.Text = ""; kind.SelectedIndex = -1; }
    protected override async Task LoadAsync() {
        var contacts = await Api.GetAsync<List<SupportContactView>>("support-contacts?includeInactive=true"); rows.Clear();
        foreach (var row in contacts) rows.Add(Action($"{row.Name} · {row.Phone}" + (row.IsActive ? "" : " (inactivo)"), () => {
            selected = row; name.Text = row.Name; phone.Text = row.Phone; kind.SelectedIndex = (int)row.Kind; return Task.CompletedTask;
        }));
        if (contacts.Count == 0) rows.Add(new Label { Text = "No hay contactos registrados." });
    }
}
