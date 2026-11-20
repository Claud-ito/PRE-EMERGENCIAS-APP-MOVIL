using Microsoft.Extensions.DependencyInjection.Extensions;
using Univalle.Emergencias.Api.Incidents.Development;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Incidents;
using Univalle.Emergencias.Contracts.Locations;
using Univalle.Emergencias.Contracts.Notifications;
using Univalle.Emergencias.Contracts.Turns;

namespace Univalle.Emergencias.Api.Incidents;

public static class IncidentServiceCollectionExtensions
{
    /// <summary>Servicios reales del módulo de incidentes (AL-01, AL-02).</summary>
    public static IServiceCollection AddIncidentModule(this IServiceCollection services)
    {
        services.AddScoped<IIncidentReader, EfIncidentReader>();
        services.AddScoped<IncidentReportService>();
        services.AddScoped<IncidentQueries>();
        services.AddScoped<IInitialAlertDispatcher, InitialAlertDispatcher>();
        services.AddHostedService<InitialAlertRetryWorker>();
        return services;
    }

    /// <summary>
    /// Sustituciones de desarrollo para contratos de otros integrantes que aún no tienen implementación.
    /// Usa TryAdd: si SA-01, DI-01, JO-01 o JO-02 ya registraron la suya, se conserva la real.
    /// Llamar después de registrar los módulos reales y solo en Development.
    /// </summary>
    public static IServiceCollection AddIncidentDevelopmentSubstitutes(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<IIdentityContext, DevHeaderIdentityContext>();
        services.TryAddSingleton<ILocationCatalog, DevLocationCatalog>();
        services.TryAddScoped<ITurnResolver, DevTurnResolver>();
        services.TryAddScoped<INotificationSender, LoggingNotificationSender>();
        return services;
    }
}
