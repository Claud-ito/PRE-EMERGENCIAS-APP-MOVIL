using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Diana;
using Univalle.Emergencias.Api.Identity;
using Univalle.Emergencias.Api.Incidents;
using Univalle.Emergencias.Api.Services;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Diana;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Incidents;
using Univalle.Emergencias.Contracts.Locations;
using Univalle.Emergencias.Tests.Incidents;
using Xunit;

namespace Univalle.Emergencias.Tests;

// MVC, filtros, sesiones, directorio y persistencia reales. Solo reloj y transporte son de ensayo.
public sealed class DianaIdentityIntegrationTests
{
    [Fact]
    public async Task RealManagerSessionsMaintainCatalogsAndRejectOtherRoles()
    {
        await using var f = await DianaIdentityHost.StartAsync();
        foreach (var role in Enum.GetValues<UserRole>()) {
            await f.SignInAsync(role);
            using var response = await f.Http.PostAsJsonAsync("api/v1/locations/modules",
                new ModuleInput(role.ToString(), "Módulo " + role), DianaIdentityHost.Json);
            if (role is not (UserRole.SySO or UserRole.Mantenimiento)) {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden,
                    (await f.Http.GetAsync("api/v1/brigades/users")).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden,
                    (await f.Http.GetAsync("api/v1/support-contacts")).StatusCode);
                continue;
            }
            var module = await f.ReadAsync<ModuleLocation>(response);
            var floor = await f.PostAsync<FloorLocation>($"api/v1/locations/modules/{module.ModuleId}/floors", new FloorInput("Planta baja"));
            var contact = await f.PostAsync<SupportContactView>("api/v1/support-contacts",
                new SupportContactInput("Consultorio " + role, SupportContactKind.Consultorio, "1234567"));
            Assert.Equal(HttpStatusCode.NoContent,
                (await f.Http.PutAsJsonAsync($"api/v1/locations/floors/{floor.FloorId}", new FloorInput("Piso actualizado"), DianaIdentityHost.Json)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await f.Http.DeleteAsync($"api/v1/support-contacts/{contact.ContactId}")).StatusCode);
            Assert.Contains(await f.GetAsync<SupportContactView[]>("api/v1/support-contacts?includeInactive=true"),
                x => x.ContactId == contact.ContactId && !x.IsActive);
            Assert.Equal(HttpStatusCode.Conflict,
                (await f.Http.PostAsJsonAsync("api/v1/locations/modules", new ModuleInput(module.Code, "Duplicado"), DianaIdentityHost.Json)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await f.Http.PostAsJsonAsync("api/v1/locations/modules", new ModuleInput("", "Inválido"), DianaIdentityHost.Json)).StatusCode);
        }
    }

    [Fact]
    public async Task RealUserDirectoryResolvesBrigadeAndStopsReturningDeactivatedLeader()
    {
        await using var f = await DianaIdentityHost.StartAsync();
        await VerifyBrigadesAsync(f);
    }

    private static async Task VerifyBrigadesAsync(DianaIdentityHost f)
    {
        await f.SignInAsync(UserRole.SySO);
        var moduleA = await f.PostAsync<ModuleLocation>("api/v1/locations/modules", new ModuleInput("A", "Módulo A"));
        var moduleB = await f.PostAsync<ModuleLocation>("api/v1/locations/modules", new ModuleInput("B", "Módulo B"));
        var leader = await f.PostAsync<UserView>("api/v1/users",
            new CreateUserRequest("lider.nuevo", "lider.nuevo@example.test", "Líder real de ensayo", UserRole.LiderBrigada, DianaIdentityHost.Password));
        var member = f.Users[UserRole.Brigadista];
        var directory = await f.GetAsync<BrigadeUser[]>("api/v1/brigades/users");
        Assert.Contains(directory, u => u.UserId == leader.UserId);
        Assert.DoesNotContain(directory, u => u.UserId == f.Users[UserRole.Medico]);
        var input = new BrigadeInput("Brigada integrada", leader.UserId,
            [new(leader.UserId, "Primeros auxilios"), new(member, "Evacuación")], [moduleA.ModuleId, moduleB.ModuleId]);
        var brigade = await f.PostAsync<BrigadeView>("api/v1/brigades", input);
        foreach (var module in new[] { moduleA, moduleB }) {
            var status = await f.GetAsync<ModuleBrigadeStatus>($"api/v1/brigades/modules/{module.ModuleId}/leader");
            Assert.Equal(leader.UserId, status.Leader!.LeaderUserId);
            Assert.Equal(leader.FullName, status.Leader.LeaderName);
            Assert.Contains(status.Leader.Members, m => m.UserId == member && m.Specialty == "Evacuación");
        }
        Assert.Equal(HttpStatusCode.NoContent, (await f.Http.DeleteAsync($"api/v1/users/{leader.UserId}")).StatusCode);
        Assert.DoesNotContain(await f.GetAsync<BrigadeUser[]>("api/v1/brigades/users"), u => u.UserId == leader.UserId);
        Assert.Null((await f.GetAsync<ModuleBrigadeStatus>($"api/v1/brigades/modules/{moduleA.ModuleId}/leader")).Leader);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await f.Http.PutAsJsonAsync($"api/v1/brigades/{brigade.BrigadeId}", input, DianaIdentityHost.Json)).StatusCode);
        await using var scope = f.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Set<BrigadeModuleAssignment>().CountAsync());
        Assert.Equal(leader.UserId, (await db.Set<Brigade>().SingleAsync()).LeaderUserId);
    }

    [Fact]
    public async Task RealPhysicianSessionCreatesMultipleRecordsWithoutLeakingToOtherRoles()
    {
        await using var f = await DianaIdentityHost.StartAsync();
        await VerifyInjuriesAsync(f);
    }

    private static async Task VerifyInjuriesAsync(DianaIdentityHost f)
    {
        await f.SignInAsync(UserRole.SySO);
        var module = await f.PostAsync<ModuleLocation>("api/v1/locations/modules", new ModuleInput("MED", "Atención"));
        var floor = await f.PostAsync<FloorLocation>($"api/v1/locations/modules/{module.ModuleId}/floors", new FloorInput("Piso 1"));
        var incident = Guid.NewGuid();
        await using (var scope = f.App.Services.CreateAsyncScope()) {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Incidents.Add(new Incident { IncidentId = incident, ModuleId = module.ModuleId, FloorId = floor.FloorId,
                OpenedAtUtc = f.Clock.UtcNow, InitialAlertEventId = Guid.NewGuid(), ConcurrencyStamp = Guid.NewGuid() });
            await db.SaveChangesAsync();
        }
        var route = $"api/v1/incidents/{incident}/injury-records";
        foreach (var role in Enum.GetValues<UserRole>().Where(r => r != UserRole.Medico)) {
            await f.SignInAsync(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await f.Http.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await f.Http.PostAsJsonAsync(route, new CreateInjuryRecordInput(true), DianaIdentityHost.Json)).StatusCode);
        }
        await f.SignInAsync(UserRole.Medico);
        Assert.Contains(await f.GetAsync<InjuryIncidentView[]>("api/v1/injury-records/incidents"), i => i.IncidentId == incident);
        Assert.Empty(await f.GetAsync<InjuryRecordView[]>(route));
        Assert.Equal(HttpStatusCode.BadRequest,
            (await f.Http.PostAsJsonAsync(route, new CreateInjuryRecordInput(false), DianaIdentityHost.Json)).StatusCode);
        for (var i = 0; i < 2; i++) {
            var record = await f.PostAsync<InjuryRecordView>(route, new { evaluatedOrAttended = true,
                physicianUserId = f.Users[UserRole.SySO], createdAtUtc = DateTimeOffset.MinValue });
            Assert.Equal(f.Users[UserRole.Medico], record.PhysicianUserId);
            Assert.Equal(f.Clock.UtcNow, record.CreatedAtUtc);
        }
        Assert.Equal(2, (await f.GetAsync<InjuryRecordView[]>(route)).Length);
        Assert.Equal(HttpStatusCode.NotFound,
            (await f.Http.PostAsJsonAsync($"api/v1/incidents/{Guid.NewGuid()}/injury-records", new CreateInjuryRecordInput(true), DianaIdentityHost.Json)).StatusCode);
    }

    [Fact]
    public async Task CatalogRequestsRejectMissingExpiredLoggedOutAndDeactivatedRealSessions()
    {
        await using var f = await DianaIdentityHost.StartAsync();
        const string route = "api/v1/locations/modules";
        f.Http.DefaultRequestHeaders.Add("X-Dev-Role", "SySO");
        f.Http.DefaultRequestHeaders.Add("X-Dev-User-Id", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Http.GetAsync(route)).StatusCode);
        await f.SignInAsync(UserRole.SySO);
        Assert.Equal(HttpStatusCode.OK, (await f.Http.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await f.Http.PostAsync("api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Http.GetAsync(route)).StatusCode);
        await f.SignInAsync(UserRole.SySO);
        f.Clock.Now += TimeSpan.FromHours(8);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Http.GetAsync(route)).StatusCode);
        await f.SignInAsync(UserRole.Mantenimiento);
        var token = f.Http.DefaultRequestHeaders.Authorization;
        await f.SignInAsync(UserRole.SySO);
        Assert.Equal(HttpStatusCode.NoContent,
            (await f.Http.DeleteAsync($"api/v1/users/{f.Users[UserRole.Mantenimiento]}")).StatusCode);
        f.Http.DefaultRequestHeaders.Authorization = token;
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Http.GetAsync(route)).StatusCode);
    }

    [SqlServerFact]
    public async Task MigratedSqlServerConnectsRealAccountsBrigadesAndPhysicianRecords()
    {
        await using var f = await DianaIdentityHost.StartAsync(sqlServer: true);
        await VerifyBrigadesAsync(f);
        await VerifyInjuriesAsync(f);
    }
}

internal sealed class DianaIdentityHost : IAsyncDisposable
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    internal const string Password = "clave-exclusiva-de-pruebas";
    private readonly SqliteConnection? connection;
    private readonly string? temporaryDatabase;
    internal WebApplication App { get; }
    internal HttpClient Http { get; }
    internal BrigadeClock Clock { get; }
    internal Dictionary<UserRole, Guid> Users { get; } = [];
    private DianaIdentityHost(WebApplication app, SqliteConnection? connection, BrigadeClock clock, string? temporaryDatabase) {
        App = app; this.connection = connection; Clock = clock; this.temporaryDatabase = temporaryDatabase;
        Http = new HttpClient(new IdentityPipelineHandler(((IApplicationBuilder)app).Build(), app.Services)) { BaseAddress = new Uri("http://in-process/") };
    }
    internal static async Task<DianaIdentityHost> StartAsync(bool sqlServer = false) {
        SqliteConnection? connection = null;
        string? temporaryDatabase = null;
        string? sqlConnection = null;
        if (sqlServer) {
            temporaryDatabase = "DianaIdentityTest_" + Guid.NewGuid().ToString("N");
            sqlConnection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable(SqlServerFactAttribute.EnvironmentVariable))
                { InitialCatalog = temporaryDatabase }.ConnectionString;
        } else {
            connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        }
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(LocationsController).Assembly)
            .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddDbContext<AppDbContext>(o => {
            if (sqlServer) o.UseSqlServer(sqlConnection);
            else o.UseSqlite(connection!).ReplaceService<IModelCustomizer, BrigadeSqliteModelCustomizer>();
        });
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IIdentityContext, DatabaseIdentityContext>();
        builder.Services.AddScoped<SessionService>();
        builder.Services.AddScoped<IAuditTrail, EfAuditTrail>();
        builder.Services.AddScoped<IBrigadeUserDirectory, BrigadeUserDirectory>();
        builder.Services.AddScoped<IIncidentReader, EfIncidentReader>();
        builder.Services.AddDianaModules();
        var clock = new BrigadeClock(); builder.Services.AddSingleton<IServerClock>(clock);
        foreach (var (value, i) in new[] { "Primeros auxilios", "Evacuación", "Extinción de fuegos" }.Select((v, i) => (v, i)))
            builder.Configuration[$"Diana:BrigadeSpecialties:{i}"] = value;
        var app = builder.Build(); app.UseRouting();
#pragma warning disable ASP0014 // Transporte MVC en memoria sin sockets.
        app.UseEndpoints(e => e.MapControllers());
#pragma warning restore ASP0014
        var host = new DianaIdentityHost(app, connection, clock, temporaryDatabase);
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (sqlServer) await db.Database.MigrateAsync();
        else await db.Database.EnsureCreatedAsync();
        foreach (var role in Enum.GetValues<UserRole>()) {
            var username = role.ToString(); var id = Guid.NewGuid(); host.Users[role] = id;
            var user = new UserAccount { UserId = id, Username = username, NormalizedUsername = username.ToUpperInvariant(),
                Email = username + "@example.test", NormalizedEmail = (username + "@example.test").ToUpperInvariant(),
                FullName = "Ensayo " + role, Role = role, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow };
            PasswordCredentials.Set(user, Password); db.Users.Add(user);
        }
        await db.SaveChangesAsync(); return host;
    }
    internal async Task SignInAsync(UserRole role) {
        var login = await PostAsync<LoginResponse>("api/v1/auth/login", new LoginRequest(role.ToString(), Password));
        Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }
    internal async Task<T> PostAsync<T>(string route, object input) {
        using var response = await Http.PostAsJsonAsync(route, input, Json); return await ReadAsync<T>(response);
    }
    internal async Task<T> GetAsync<T>(string route) {
        using var response = await Http.GetAsync(route); return await ReadAsync<T>(response);
    }
    internal async Task<T> ReadAsync<T>(HttpResponseMessage response) {
        Assert.True(response.IsSuccessStatusCode, $"HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }
    public async ValueTask DisposeAsync() {
        Http.Dispose();
        try {
            if (temporaryDatabase?.StartsWith("DianaIdentityTest_", StringComparison.Ordinal) == true) {
                await using var scope = App.Services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();
            }
        } finally { await App.DisposeAsync(); if (connection is not null) await connection.DisposeAsync(); }
    }
}

internal sealed class IdentityPipelineHandler(RequestDelegate pipeline, IServiceProvider services) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
        await using var scope = services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = ct };
        context.Request.Method = request.Method.Method; context.Request.Scheme = "http";
        context.Request.Host = new HostString("in-process"); context.Request.Protocol = "HTTP/1.1";
        context.Request.Path = request.RequestUri!.AbsolutePath; context.Request.QueryString = new QueryString(request.RequestUri.Query);
        foreach (var header in request.Headers) context.Request.Headers[header.Key] = header.Value.ToArray();
        if (request.Content is not null) {
            var body = await request.Content.ReadAsByteArrayAsync(ct); context.Request.Body = new MemoryStream(body);
            context.Request.ContentLength = body.Length; context.Request.ContentType = request.Content.Headers.ContentType?.ToString();
        }
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = context;
        try {
            using var output = new MemoryStream(); context.Response.Body = output; await pipeline(context);
            var response = new HttpResponseMessage((HttpStatusCode)context.Response.StatusCode) { Content = new ByteArrayContent(output.ToArray()) };
            if (context.Response.ContentType is not null) response.Content.Headers.TryAddWithoutValidation("Content-Type", context.Response.ContentType);
            return response;
        } finally { accessor.HttpContext = null; }
    }
}
