using Univalle.Emergencias.Contracts.Brigades;
using Univalle.Emergencias.Contracts.Locations;

namespace Univalle.Emergencias.Api.Diana;

public static class DianaRegistration
{
    public static IServiceCollection AddDianaModules(this IServiceCollection services) {
        services.AddScoped<ILocationCatalog, LocationCatalog>();
        services.AddScoped<IBrigadeLeaderLookup, BrigadeLeaderLookup>();
        return services;
    }
}
