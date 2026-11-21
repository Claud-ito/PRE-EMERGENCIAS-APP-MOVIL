using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PRE_EMERGENCIAS_APP_MOVIL.Diana;

public static class DianaMobileRegistration
{
    public static IServiceCollection AddDianaMobile(this IServiceCollection services) {
        services.TryAddSingleton<IDianaMobileSession, PendingDianaMobileSession>();
        services.AddSingleton<DianaApiClient>();
        services.AddTransient<DianaMenuPage>(); services.AddTransient<LocationsPage>();
        services.AddTransient<ContactsPage>(); services.AddTransient<BrigadesPage>(); services.AddTransient<InjuryRecordsPage>();
        Routing.RegisterRoute("diana", typeof(DianaMenuPage));
        Routing.RegisterRoute("diana-locations", typeof(LocationsPage));
        Routing.RegisterRoute("diana-contacts", typeof(ContactsPage));
        Routing.RegisterRoute("diana-brigades", typeof(BrigadesPage));
        Routing.RegisterRoute("diana-injuries", typeof(InjuryRecordsPage));
        return services;
    }
}
