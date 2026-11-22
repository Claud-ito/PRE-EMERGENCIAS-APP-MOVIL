using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Diana;
using Univalle.Emergencias.Api.Incidents;
using Univalle.Emergencias.Contracts.Diana;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Locations;
using Univalle.Emergencias.Tests.Incidents;
using Xunit;

namespace Univalle.Emergencias.Tests;

public sealed class DianaClinicalPrototypeTests
{
    [Fact]
    public async Task PhysicianStoresFictitiousDraftAndReportWhileOtherRolesCannotReadIt() {
        await using var f = await DianaIdentityHost.StartAsync();
        await VerifyDraftAsync(f);
    }

    [SqlServerFact]
    public async Task SqlServerMigrationPreservesBaseAndStoresFictitiousClinicalDraft() {
        await using var f = await DianaIdentityHost.StartAsync(sqlServer: true);
        await VerifyDraftAsync(f);
        await using var scope = f.App.Services.CreateAsyncScope();
        Assert.False(scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task DisabledPrototypeAndUnmarkedDataAreRejectedWithoutBlockingBaseRecords() {
        await using var f = await DianaIdentityHost.StartAsync();
        var incident = await CreateIncidentAsync(f);
        await f.SignInAsync(UserRole.Medico);
        var route = $"api/v1/incidents/{incident}/injury-records";
        using var disabled = await f.Http.PostAsJsonAsync(route, new CreateInjuryRecordInput(true, Sample()), DianaIdentityHost.Json);
        Assert.Equal(HttpStatusCode.Conflict, disabled.StatusCode);
        f.App.Configuration["Diana:ClinicalPrototype:Enabled"] = "true";
        using var unmarked = await f.Http.PostAsJsonAsync(route, new CreateInjuryRecordInput(true, Sample() with { IsTestData = false }), DianaIdentityHost.Json);
        Assert.Equal(HttpStatusCode.BadRequest, unmarked.StatusCode);
        using var invalid = await f.Http.PostAsJsonAsync(route, new CreateInjuryRecordInput(true, Sample() with { Age = 121 }), DianaIdentityHost.Json);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var unattended = await f.Http.PostAsJsonAsync(route, new CreateInjuryRecordInput(false, Sample()), DianaIdentityHost.Json);
        Assert.Equal(HttpStatusCode.BadRequest, unattended.StatusCode);
        var record = await f.PostAsync<InjuryRecordView>(route, new CreateInjuryRecordInput(true));
        Assert.Null(record.TestClinicalDraft);
        Assert.Single(await f.GetAsync<InjuryRecordView[]>(route));
        Assert.Equal(HttpStatusCode.Conflict, (await f.Http.GetAsync($"{route}/{record.InjuryRecordId}/test-report")).StatusCode);
    }

    private static TestClinicalDraft Sample() => new("PERSONA-DEMO-001", 22,
        "Observación ficticia.", "Atención de ensayo.", "Informe ficticio, sin validez clínica.");

    private static async Task VerifyDraftAsync(DianaIdentityHost f) {
        var incident = await CreateIncidentAsync(f);
        f.App.Configuration["Diana:ClinicalPrototype:Enabled"] = "true";
        await f.SignInAsync(UserRole.Medico);
        var route = $"api/v1/incidents/{incident}/injury-records";
        var baseline = await f.PostAsync<InjuryRecordView>(route, new CreateInjuryRecordInput(true));
        var record = await f.PostAsync<InjuryRecordView>(route, new CreateInjuryRecordInput(true, Sample()));
        Assert.Equal(Sample(), record.TestClinicalDraft);
        Assert.Equal(f.Users[UserRole.Medico], record.PhysicianUserId);
        Assert.Equal(f.Clock.UtcNow, record.CreatedAtUtc);
        var list = await f.GetAsync<InjuryRecordView[]>(route);
        Assert.Equal(2, list.Length);
        Assert.Null(list.Single(x => x.InjuryRecordId == baseline.InjuryRecordId).TestClinicalDraft);
        Assert.Equal(Sample(), list.Single(x => x.InjuryRecordId == record.InjuryRecordId).TestClinicalDraft);
        var reportRoute = $"{route}/{record.InjuryRecordId}/test-report";
        var report = await f.GetAsync<TestClinicalReport>(reportRoute);
        Assert.True(report.IsTestData);
        Assert.Equal("SYSO-ENSAYO-v1", report.TemplateVersion);
        Assert.Contains("SIN VALIDEZ CLÍNICA", report.Text);
        Assert.Contains(Sample().PatientAlias, report.Text);
        Assert.Equal(HttpStatusCode.NotFound, (await f.Http.GetAsync($"{route}/{Guid.NewGuid()}/test-report")).StatusCode);
        foreach (var role in Enum.GetValues<UserRole>().Where(r => r != UserRole.Medico)) {
            await f.SignInAsync(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await f.Http.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await f.Http.GetAsync(reportRoute)).StatusCode);
        }
        await using var scope = f.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await db.Set<InjuryRecord>().CountAsync());
        Assert.DoesNotContain(Sample().PatientAlias, System.Text.Json.JsonSerializer.Serialize(await db.AuditEntries.ToListAsync()));
    }

    private static async Task<Guid> CreateIncidentAsync(DianaIdentityHost f) {
        await f.SignInAsync(UserRole.SySO);
        var module = await f.PostAsync<ModuleLocation>("api/v1/locations/modules", new ModuleInput("CLIN-TEST", "Módulo de ensayo"));
        var floor = await f.PostAsync<FloorLocation>($"api/v1/locations/modules/{module.ModuleId}/floors", new FloorInput("Piso de ensayo"));
        await using var scope = f.App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.NewGuid();
        db.Incidents.Add(new Incident { IncidentId = id, ModuleId = module.ModuleId, FloorId = floor.FloorId,
            OpenedAtUtc = f.Clock.UtcNow, InitialAlertEventId = Guid.NewGuid(), ConcurrencyStamp = Guid.NewGuid() });
        await db.SaveChangesAsync(); return id;
    }
}
