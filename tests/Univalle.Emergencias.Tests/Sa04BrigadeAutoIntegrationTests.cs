using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Univalle.Emergencias.Api.Coordination;
using Univalle.Emergencias.Api.Diana;
using Univalle.Emergencias.Api.Identity;
using Univalle.Emergencias.Api.Incidents;
using Univalle.Emergencias.Api.Notifications;
using Univalle.Emergencias.Api.Services;
using Univalle.Emergencias.Contracts.Diana;
using Univalle.Emergencias.Contracts.Incidents;
using Xunit;

namespace Univalle.Emergencias.Tests;

public sealed class Sa04BrigadeAutoIntegrationTests
{
    [Fact]
    public async Task AutoUsesCurrentLeaderFromDianasBrigadeAndRealUserDirectory()
    {
        await using var fixture = await BrigadeFixture.SqliteAsync();
        var brigade = Assert.IsType<BrigadeView>(Assert.IsType<OkObjectResult>(
            await fixture.Controller.Create(fixture.Input("Brigada integrada"), default)).Value);

        for (var index = 0; index < fixture.Users.Accounts.Count; index++)
        {
            var account = fixture.Users.Accounts[index];
            var user = new UserAccount
            {
                UserId = account.UserId,
                Username = $"brigada.{index}",
                NormalizedUsername = $"BRIGADA.{index}",
                Email = $"brigada{index}@example.test",
                NormalizedEmail = $"BRIGADA{index}@EXAMPLE.TEST",
                FullName = account.DisplayName,
                Role = account.Role,
                CreatedAtUtc = fixture.Clock.UtcNow,
                UpdatedAtUtc = fixture.Clock.UtcNow
            };
            PasswordCredentials.Set(user, "clave-únicamente-para-prueba");
            fixture.Db.Users.Add(user);
        }
        await fixture.Db.SaveChangesAsync();

        using var services = new ServiceCollection()
            .AddSingleton<IBrigadeUserDirectory>(new BrigadeUserDirectory(fixture.Db))
            .BuildServiceProvider();
        var leaders = new BrigadeLeaderLookup(fixture.Db, services);
        var audit = new EfAuditTrail(fixture.Db, fixture.Clock);
        var callouts = new CalloutService(fixture.Db, leaders,
            new PersistentNotificationSender(fixture.Db, fixture.Clock),
            fixture.Clock, audit);
        var coordinator = Guid.NewGuid();

        var first = DueIncident(fixture, coordinator);
        fixture.Db.Incidents.Add(first);
        await fixture.Db.SaveChangesAsync();
        Assert.True(await callouts.RegisterAsync(first.IncidentId, null, default));
        Assert.Equal(fixture.Users.Accounts[0].UserId,
            (await fixture.Db.NotificationEntries.SingleAsync(x => x.IncidentId == first.IncidentId)).RecipientUserId);

        fixture.Clock.Now += TimeSpan.FromMinutes(1);
        Assert.IsType<OkObjectResult>(await fixture.Controller.Update(brigade.BrigadeId,
            fixture.Input("Brigada integrada") with { LeaderUserId = fixture.Users.Accounts[1].UserId }, default));
        var second = DueIncident(fixture, coordinator);
        fixture.Db.Incidents.Add(second);
        await fixture.Db.SaveChangesAsync();
        Assert.True(await callouts.RegisterAsync(second.IncidentId, null, default));
        Assert.Equal(fixture.Users.Accounts[1].UserId,
            (await fixture.Db.NotificationEntries.SingleAsync(x => x.IncidentId == second.IncidentId)).RecipientUserId);
        Assert.Equal(2, await fixture.Db.AuditEntries.CountAsync(x => x.Action == "incident.callout.auto"));
    }

    private static Incident DueIncident(BrigadeFixture fixture, Guid coordinator) => new()
    {
        IncidentId = Guid.NewGuid(),
        ModuleId = fixture.ModuleIds[0],
        FloorId = Guid.NewGuid(),
        Type = IncidentType.PrimerosAuxilios,
        Status = IncidentStatuses.Active,
        OpenedAtUtc = fixture.Clock.UtcNow.AddMinutes(-3),
        InitialAlertEventId = Guid.NewGuid(),
        InitialAlertRecipientUserId = coordinator,
        CurrentCoordinatorUserId = coordinator,
        AutoDueAtUtc = fixture.Clock.UtcNow.UtcDateTime.AddSeconds(-1),
        ConcurrencyStamp = Guid.NewGuid()
    };
}
