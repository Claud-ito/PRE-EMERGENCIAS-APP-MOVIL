using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Diana;
using Univalle.Emergencias.Api.Services;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Diana;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Incidents;

// Pruebas HTTP de autorización sin cuentas ni bases de producción.
// Complementan las pruebas de persistencia SQLite del proyecto xUnit.
var assertions = 0;
void Check(bool condition, string scenario) {
    assertions++; if (!condition) throw new InvalidOperationException("FALLÓ: " + scenario);
    Console.WriteLine("OK: " + scenario);
}
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer("Server=localhost;Database=DianaChecksUnused;Integrated Security=true;TrustServerCertificate=true").Options;
using (var db = new AppDbContext(options)) {
    var model = db.GetService<IDesignTimeModel>().Model;
    var floor = model.FindEntityType(typeof(LocationFloor))!;
    Check(floor.GetForeignKeys().Single().DeleteBehavior == DeleteBehavior.Restrict, "FK piso-módulo conserva referencias");
    Check(floor.GetIndexes().Any(i => i.IsUnique && i.Properties.Select(p => p.Name).SequenceEqual(new[] { "ModuleId", "Name" })), "Un piso único dentro de su módulo");
    var assignment = model.FindEntityType(typeof(BrigadeModuleAssignment))!;
    Check(assignment.GetIndexes().Any(i => i.IsUnique && i.GetFilter() == "[EndedAtUtc] IS NULL"), "Un único líder/brigada vigente por módulo incluso ante concurrencia");
    Check(!assignment.GetIndexes().Any(i => i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == "BrigadeId"), "Una brigada admite varios módulos");
    var injury = model.FindEntityType(typeof(InjuryRecord))!;
    Check(injury.GetForeignKeys().Single().DeleteBehavior == DeleteBehavior.Restrict, "La ficha referencia un incidente persistido sin borrado en cascada");
    Check(!injury.GetIndexes().Any(i => i.IsUnique && i.Properties.Any(p => p.Name == "IncidentId")), "Un incidente admite cero o varias fichas");
    Check(!db.Database.HasPendingModelChanges(), "La instantánea de migración coincide con el modelo");
    var migrationScript = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>()
        .GenerateScript("20261120040743_Sa00AuditInitial");
    Check(!migrationScript.Contains("DROP TABLE [AuditEntries]"), "La migración conserva la auditoría existente");
    var script = db.Database.GenerateCreateScript();
    Check(script.Contains("CREATE TABLE [SupportContacts]") && script.Contains("CREATE TABLE [InjuryRecords]"), "El modelo SQL Server incluye contactos y fichas");
}

Task<(WebApplication App, HttpClient Http)> Start(TestIdentity? identity) {
    var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
    foreach (var entry in builder.Configuration.GetSection("Diana:BrigadeSpecialties").GetChildren()) entry.Value = null;
    builder.Services.AddControllers().AddApplicationPart(typeof(LocationsController).Assembly);
    builder.Services.AddDbContext<AppDbContext>(b => b.UseSqlServer("Server=localhost;Database=Unused;Integrated Security=true"));
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<IServerClock, SystemServerClock>();
    builder.Services.AddScoped<IAuditTrail, EfAuditTrail>();
    if (identity is not null) builder.Services.AddSingleton<IIdentityContext>(identity);
    builder.Services.AddSingleton<IIncidentReader, TestIncidents>();
    var app = builder.Build();
    app.UseRouting();
#pragma warning disable ASP0014 // Pipeline construido sin servidor para verificar MVC en memoria.
    app.UseEndpoints(endpoints => endpoints.MapControllers());
#pragma warning restore ASP0014
    var pipeline = ((IApplicationBuilder)app).Build();
    return Task.FromResult((app, new HttpClient(new PipelineHandler(pipeline, app.Services)) { BaseAddress = new Uri("http://in-process") }));
}
var routes = new[] { "api/v1/locations/modules", "api/v1/support-contacts", "api/v1/brigades", $"api/v1/incidents/{TestIncidents.Id}/injury-records", "api/v1/injury-records/incidents" };
var noIdentity = await Start(null);
try { foreach (var route in routes) Check((await noIdentity.Http.GetAsync(route)).StatusCode == HttpStatusCode.ServiceUnavailable, "Sin SA-01 el endpoint protegido permanece cerrado: " + route); }
finally { noIdentity.Http.Dispose(); await noIdentity.App.DisposeAsync(); }
var identity = new TestIdentity(); var host = await Start(identity);
try {
    foreach (var route in routes) Check((await host.Http.GetAsync(route)).StatusCode == HttpStatusCode.Unauthorized, "Sin sesión devuelve 401: " + route);
    var allPermissions = new HashSet<string> { PermissionCodes.ManageLocations, PermissionCodes.ManageBrigades, PermissionCodes.ManageSupportContacts, PermissionCodes.RecordInjury };
    foreach (var role in Enum.GetValues<UserRole>()) {
        identity.User = new(Guid.NewGuid(), true, role, allPermissions);
        if (role is not (UserRole.SySO or UserRole.Mantenimiento))
            foreach (var route in routes.Take(3)) Check((await host.Http.GetAsync(route)).StatusCode == HttpStatusCode.Forbidden, role + " no gestiona catálogos");
        if (role != UserRole.Medico)
            foreach (var route in routes.Skip(3)) Check((await host.Http.GetAsync(route)).StatusCode == HttpStatusCode.Forbidden, role + " no accede al registro médico");
    }
    foreach (var role in new[] { UserRole.SySO, UserRole.Mantenimiento }) {
        identity.User = new(Guid.NewGuid(), true, role, allPermissions);
        Check((await host.Http.GetAsync("api/v1/brigades/specialties")).StatusCode == HttpStatusCode.ServiceUnavailable, role + " recibe rechazo si el catálogo no está configurado");
        identity.User = identity.User with { IsActive = false };
        foreach (var route in routes) Check((await host.Http.GetAsync(route)).StatusCode == HttpStatusCode.Forbidden, "La baja revoca la sesión emitida: " + role);
    }
    identity.User = new(Guid.NewGuid(), true, UserRole.Medico, new HashSet<string>());
    Check((await host.Http.GetAsync(routes[3])).StatusCode == HttpStatusCode.Forbidden, "Médico sin permiso no accede");
    identity.User = identity.User with { Permissions = allPermissions };
    Check((await host.Http.GetAsync($"api/v1/incidents/{Guid.NewGuid()}/injury-records")).StatusCode == HttpStatusCode.NotFound, "Médico autorizado valida un incidente real con AL-01");
    var invalid = await host.Http.PostAsJsonAsync(routes[3], new CreateInjuryRecordInput(false));
    Check(invalid.StatusCode == HttpStatusCode.BadRequest, "No se registra una ficha antes de la evaluación/atención");
    foreach (var operation in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete }) {
        identity.User = new(Guid.NewGuid(), false, UserRole.SySO, allPermissions);
        using var request = new HttpRequestMessage(operation, operation == HttpMethod.Post ? routes[0] : routes[0] + "/" + Guid.NewGuid()) { Content = JsonContent.Create(new ModuleInput("A", "Módulo")) };
        Check((await host.Http.SendAsync(request)).StatusCode == HttpStatusCode.Forbidden, "La API bloquea escritura directa con cuenta inactiva: " + operation);
    }
    var sourceConfiguration = new ConfigurationBuilder()
        .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.diana-source.json")).Build();
    var specialties = sourceConfiguration.GetSection("Diana:BrigadeSpecialties").Get<string[]>() ?? [];
    var expectedSpecialties = new[] { "Primeros auxilios", "Evacuación", "Extinción de fuegos" };
    Check(specialties.SequenceEqual(expectedSpecialties), "La configuración de la API contiene las tres especialidades de RF05");
    for (var i = 0; i < specialties.Length; i++) host.App.Configuration[$"Diana:BrigadeSpecialties:{i}"] = specialties[i];
    foreach (var role in new[] { UserRole.SySO, UserRole.Mantenimiento }) {
        identity.User = new(Guid.NewGuid(), true, role, allPermissions);
        using var response = await host.Http.GetAsync("api/v1/brigades/specialties");
        Check(response.StatusCode == HttpStatusCode.OK, role + " consulta el catálogo confirmado");
        Check((await response.Content.ReadFromJsonAsync<string[]>())!.SequenceEqual(expectedSpecialties), role + " recibe los nombres exactos de RF05");
    }
} finally { host.Http.Dispose(); await host.App.DisposeAsync(); }
Console.WriteLine($"Resultado: {assertions} comprobaciones correctas. No comprueba persistencia SQL ni ejecución MAUI.");

sealed class TestIdentity : IIdentityContext {
    public AuthenticatedUser? User { get; set; }
    public Task<AuthenticatedUser?> GetCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(User);
}
sealed class TestIncidents : IIncidentReader {
    public static readonly Guid Id = Guid.NewGuid();
    public Task<IncidentSnapshot?> GetByIdAsync(Guid incidentId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IncidentSnapshot?>(incidentId == Id ? new(Id, Guid.NewGuid(), Guid.NewGuid(), "Active", null, DateTimeOffset.UtcNow, Guid.NewGuid(), null) : null);
}

// Ejecuta el pipeline HTTP real de MVC en memoria; no requiere sockets ni una base real.
sealed class PipelineHandler(RequestDelegate pipeline, IServiceProvider services) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
        await using var scope = services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = ct };
        context.Request.Method = request.Method.Method; context.Request.Scheme = "http";
        context.Request.Host = new HostString("in-process"); context.Request.Protocol = "HTTP/1.1";
        context.Request.Path = request.RequestUri!.AbsolutePath;
        context.Request.QueryString = new QueryString(request.RequestUri.Query);
        if (request.Content is not null) {
            var body = await request.Content.ReadAsByteArrayAsync(ct);
            context.Request.Body = new MemoryStream(body); context.Request.ContentLength = body.Length;
            context.Request.ContentType = request.Content.Headers.ContentType?.ToString();
        }
        using var output = new MemoryStream(); context.Response.Body = output;
        await pipeline(context);
        var response = new HttpResponseMessage((HttpStatusCode)context.Response.StatusCode) { Content = new ByteArrayContent(output.ToArray()) };
        if (context.Response.ContentType is not null) response.Content.Headers.TryAddWithoutValidation("Content-Type", context.Response.ContentType);
        return response;
    }
}
