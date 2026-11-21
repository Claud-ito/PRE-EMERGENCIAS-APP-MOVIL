using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Diana;
using Univalle.Emergencias.Api.Incidents;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Incidents;

namespace Univalle.Emergencias.Api.Suggestions;

/// <summary>Prepara un caso cerrado de ensayo solo desde la consola de Development.</summary>
public static class SuggestionDevelopmentSeeder
{
    private static readonly Guid TrialIncidentId =
        Guid.Parse("5e550000-0000-4000-8000-00000000c011");

    public static async Task RunAsync(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            throw new InvalidOperationException("El incidente de ensayo solo existe en Development.");
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.Incidents.AnyAsync(x => x.IncidentId == TrialIncidentId))
        {
            Console.WriteLine($"Incidente cerrado de ensayo existente: {TrialIncidentId}");
            return;
        }
        var floor = await db.Set<LocationFloor>().AsNoTracking()
            .FirstOrDefaultAsync(x => x.IsActive
                && db.Set<LocationModule>().Any(module =>
                    module.ModuleId == x.ModuleId && module.IsActive));
        var witness = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            x => x.NormalizedUsername == "TESTIGO.PRUEBA" && x.IsActive);
        if (floor is null || witness is null)
            throw new InvalidOperationException(
                "Crea un módulo y piso activos y ejecuta --seed-development-users antes de preparar el cierre.");

        var clock = scope.ServiceProvider.GetRequiredService<IServerClock>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditTrail>();
        var now = clock.UtcNow.ToUniversalTime();
        var opened = now.AddHours(-2);
        db.Incidents.Add(new Incident
        {
            IncidentId = TrialIncidentId,
            ModuleId = floor.ModuleId,
            FloorId = floor.FloorId,
            Type = IncidentType.PrimerosAuxilios,
            Status = IncidentStatuses.Closed,
            OpenedAtUtc = opened,
            ClosedAtUtc = now.AddMinutes(-30),
            InitialAlertEventId = Guid.NewGuid(),
            InitialAlertState = InitialAlertStates.NoCoverage,
            ConcurrencyStamp = Guid.NewGuid()
        });
        db.IncidentReports.Add(new IncidentReport
        {
            ReportId = Guid.NewGuid(),
            IncidentId = TrialIncidentId,
            ReporterUserId = witness.UserId,
            ClientRequestId = Guid.NewGuid(),
            Type = IncidentType.PrimerosAuxilios,
            ModuleId = floor.ModuleId,
            FloorId = floor.FloorId,
            PreliminarySeverity = PreliminarySeverity.Leve,
            ReportedAtUtc = opened,
            IsInitial = true
        });
        await audit.RecordAsync(new AuditWriteRequest(
            null, AuditActorKind.System, "incident.seed-closed",
            "Incident", TrialIncidentId, TrialIncidentId));
        Console.WriteLine($"Incidente cerrado de ensayo creado: {TrialIncidentId}");
    }
}
