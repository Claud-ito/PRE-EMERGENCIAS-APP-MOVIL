using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PRE_EMERGENCIAS_APP_MOVIL.Diana;
using PRE_EMERGENCIAS_APP_MOVIL.Identity;
using PRE_EMERGENCIAS_APP_MOVIL.Suggestions;
using PRE_EMERGENCIAS_APP_MOVIL.Incidents;
using PRE_EMERGENCIAS_APP_MOVIL.Services;

namespace PRE_EMERGENCIAS_APP_MOVIL;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<IApiEndpointSettings, ApiEndpointSettings>();
        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(10) });
        builder.Services.AddSingleton<ApiClockClient>();
        builder.Services.AddSingleton<MobileSession>();
        builder.Services.AddSingleton<IDianaMobileSession, DianaMobileSession>();
        builder.Services.AddSingleton<UserApiClient>();
        builder.Services.AddSingleton<SuggestionApiClient>();
        builder.Services.AddSingleton<IncidentApiClient>();
        PRE_EMERGENCIAS_APP_MOVIL.Diana.DianaMobileRegistration.AddDianaMobile(builder.Services);
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<AccountPage>();
        builder.Services.AddTransient<UsersPage>();
        builder.Services.AddTransient<SuggestionsPage>();
        builder.Services.AddTransient<ReportIncidentPage>();
        builder.Services.AddTransient<CoordinatedIncidentsPage>();
        builder.Services.AddTransient<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
