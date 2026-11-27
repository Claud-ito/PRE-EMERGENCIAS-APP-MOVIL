using Univalle.Emergencias.Contracts.Diana;
using Univalle.Emergencias.Contracts.Identity;

namespace PRE_EMERGENCIAS_APP_MOVIL.Diana;

/// <summary>
/// RF06: cada contacto se puede llamar con un toque, editar o eliminar.
/// «Eliminar» es una baja lógica en la API: el contacto deja de listarse y conserva su historial.
/// </summary>
public sealed class ContactsPage : DianaPage
{
    private readonly Entry name = new() { Placeholder = "Nombre", MaxLength = 150 };
    private readonly Entry phone = new() { Placeholder = "Teléfono", Keyboard = Keyboard.Telephone, MaxLength = 40 };
    private readonly Picker kind = new() { Title = "Tipo de contacto", ItemsSource = new[] { "Consultorio", "Centro médico", "Bomberos" } };
    private readonly Label formTitle = new() { FontAttributes = FontAttributes.Bold };
    private readonly Button cancelEdit;
    private readonly VerticalStackLayout rows = new() { Spacing = 10 };
    private SupportContactView? selected;

    public ContactsPage(DianaApiClient api, IDianaMobileSession session) : base(api, session, "Contactos de apoyo", PermissionCodes.ManageSupportContacts) {
        cancelEdit = Action("Cancelar edición", () => { Reset(); return Task.CompletedTask; });
        Form.Add(rows);
        Form.Add(Heading("Agregar o editar"));
        Form.Add(formTitle); Form.Add(name); Form.Add(kind); Form.Add(phone);
        Form.Add(Action("Guardar", async () => {
            if (kind.SelectedIndex < 0) throw new InvalidOperationException("Selecciona el tipo de contacto.");
            var input = new SupportContactInput(Required(name, "el nombre", 150), (SupportContactKind)kind.SelectedIndex, Required(phone, "el teléfono", 40));
            var editing = selected is not null;
            await Api.SendAsync(editing ? HttpMethod.Put : HttpMethod.Post, editing ? $"support-contacts/{selected!.ContactId}" : "support-contacts", input);
            Reset(); await LoadAsync(); Status.Text = editing ? "Contacto actualizado." : "Contacto agregado.";
        }));
        Form.Add(cancelEdit);
        Reset();
    }

    private void Reset() {
        selected = null; name.Text = phone.Text = ""; kind.SelectedIndex = -1;
        formTitle.Text = "Nuevo contacto"; cancelEdit.IsVisible = false;
    }

    private void Edit(SupportContactView row) {
        selected = row; name.Text = row.Name; phone.Text = row.Phone; kind.SelectedIndex = (int)row.Kind;
        formTitle.Text = $"Editando: {row.Name}"; cancelEdit.IsVisible = true;
    }

    protected override async Task LoadAsync() {
        // Solo contactos vigentes: los eliminados ya no están disponibles para nuevas comunicaciones.
        var contacts = await Api.GetAsync<List<SupportContactView>>("support-contacts");
        rows.Clear();
        foreach (var row in contacts) rows.Add(BuildRow(row));
        if (contacts.Count == 0) rows.Add(new Label { Text = "No hay contactos registrados." });
    }

    private Border BuildRow(SupportContactView row) {
        var call = Action("Llamar", () => CallAsync(row));
        call.BackgroundColor = Color.FromArgb("#1F8A4C");
        var buttons = new HorizontalStackLayout { Spacing = 8, Children = {
            call,
            Action("Editar", () => { Edit(row); return Task.CompletedTask; }),
            Action("Eliminar", () => DeleteAsync(row))
        } };
        return new Border {
            Padding = 12,
            Content = new VerticalStackLayout { Spacing = 6, Children = {
                new Label { Text = row.Name, FontAttributes = FontAttributes.Bold, FontSize = 16 },
                new Label { Text = $"{KindName(row.Kind)} · {row.Phone}" },
                buttons
            } }
        };
    }

    private async Task CallAsync(SupportContactView row) {
        var number = new string(row.Phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
        try {
            if (!await Launcher.Default.OpenAsync(new Uri($"tel:{number}")))
                Status.Text = "Este dispositivo no puede hacer llamadas.";
        } catch (Exception ex) when (ex is FeatureNotSupportedException or InvalidOperationException or UriFormatException) {
            Status.Text = "Este dispositivo no puede hacer llamadas.";
        }
    }

    private async Task DeleteAsync(SupportContactView row) {
        if (!await DisplayAlertAsync("Eliminar contacto", $"¿Eliminar a {row.Name}? Dejará de aparecer en la lista; las comunicaciones anteriores se conservan.", "Eliminar", "Cancelar")) return;
        await Api.SendAsync(HttpMethod.Delete, $"support-contacts/{row.ContactId}");
        if (selected?.ContactId == row.ContactId) Reset();
        await LoadAsync(); Status.Text = "Contacto eliminado.";
    }

    private static string KindName(SupportContactKind kind) => kind switch {
        SupportContactKind.Consultorio => "Consultorio",
        SupportContactKind.Bomberos => "Bomberos",
        _ => "Centro médico"
    };
}
