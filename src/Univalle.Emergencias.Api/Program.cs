
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

// ==========================================
// MODULOS DE LA API
// ==========================================

using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Incidents;
using Univalle.Emergencias.Api.Identity;
using Univalle.Emergencias.Api.Suggestions;
using Univalle.Emergencias.Api.Services;
using Univalle.Emergencias.Api.Notifications;

// ==========================================
// CONTRATOS COMPARTIDOS
// ==========================================

using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Identity;
using Univalle.Emergencias.Contracts.Turns;
using Univalle.Emergencias.Contracts.Notifications;

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// CONTROLADORES Y CONFIGURACION JSON
// ==========================================

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var problem = new ValidationProblemDetails(
                context.ModelState)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "La solicitud no es válida."
            };

            problem.Extensions["code"] =
                ApiErrorCodes.InvalidInput;

            return new BadRequestObjectResult(problem);
        };
    });

// ==========================================
// OPENAPI NATIVO DE .NET 10
// ==========================================

// Genera la documentacion de la API.
// No necesita Swashbuckle.
builder.Services.AddOpenApi();

// ==========================================
// MANEJO DE ERRORES
// ==========================================

builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        var problem = context.ProblemDetails;

        if (!problem.Extensions.ContainsKey("code"))
        {
            problem.Extensions["code"] =
                problem.Status switch
                {
                    StatusCodes.Status400BadRequest =>
                        ApiErrorCodes.InvalidInput,

                    StatusCodes.Status401Unauthorized =>
                        ApiErrorCodes.Unauthenticated,

                    StatusCodes.Status403Forbidden =>
                        ApiErrorCodes.Forbidden,

                    StatusCodes.Status404NotFound =>
                        ApiErrorCodes.NotFound,

                    _ => ApiErrorCodes.Unexpected
                };
        }
    };
});

// ==========================================
// CONEXION A SQL SERVER
// ==========================================

var connectionString =
    builder.Configuration.GetConnectionString(
        "DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Configure ConnectionStrings:DefaultConnection " +
        "mediante appsettings.Development.json " +
        "o una variable de entorno.");
}

builder.Services.AddDbContext<AppDbContext>(
    options => options.UseSqlServer(connectionString));

// ==========================================
// SERVICIOS DE SAID
// ==========================================

// Reloj del servidor
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton<
    IServerClock,
    SystemServerClock>();

// Auditoria
builder.Services.AddScoped<
    IAuditTrail,
    EfAuditTrail>();

// Autenticacion y sesiones
builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<
    IIdentityContext,
    DatabaseIdentityContext>();

builder.Services.AddScoped<SessionService>();

// Buzon de sugerencias
builder.Services.AddScoped<SuggestionService>();

// ==========================================
// SERVICIOS DE DIANA
// ==========================================

builder.Services.AddScoped<
    Univalle.Emergencias.Contracts.Diana.IBrigadeUserDirectory,
    BrigadeUserDirectory>();

Univalle.Emergencias.Api.Diana.DianaRegistration
    .AddDianaModules(builder.Services);

// ==========================================
// MODULO DE TURNOS - JORGE (JO-01)
// ==========================================

// Consulta de turnos desde SQL Server
builder.Services.AddScoped<
    ITurnResolver,
    DatabaseTurnResolver>();

// Administracion de horarios y excepciones
builder.Services.AddScoped<
    ScheduleManagementService>();

// ==========================================
// MODULO DE NOTIFICACIONES - JORGE (JO-02)
// ==========================================

// Registro persistente de notificaciones.
// Se registra antes de los sustitutos de desarrollo.
builder.Services.AddScoped<
    INotificationSender,
    PersistentNotificationSender>();

// ==========================================
// SERVICIOS DE ALEJANDRO
// ==========================================

// Registro y consulta de incidentes
// y despacho de alertas iniciales.
builder.Services.AddIncidentModule();
builder.Services.AddScoped<Univalle.Emergencias.Api.Coordination.CalloutService>();
builder.Services.AddHostedService<Univalle.Emergencias.Api.Coordination.AutoWorker>();

// ==========================================
// SUSTITUCIONES DE DESARROLLO
// ==========================================

// TryAdd conserva los servicios reales
// registrados anteriormente.
if (builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue(
        "IncidentDevSubstitutes:Enabled", true))
{
    builder.Services.AddIncidentDevelopmentSubstitutes();
}

// ==========================================
// CONSTRUCCION DE LA APLICACION
// ==========================================

// Todos los servicios anteriores se registran
// antes de construir la aplicacion.
var app = builder.Build();

// ==========================================
// DOCUMENTACION OPENAPI
// ==========================================

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// ==========================================
// MIDDLEWARE HTTP
// ==========================================

// Manejo de excepciones
app.UseExceptionHandler();
app.UseStatusCodePages();

// Redireccion HTTPS fuera de Development
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Nota:
// La autenticacion del proyecto utiliza
// IIdentityContext y DatabaseIdentityContext.
// No agregamos UseAuthorization() sin configurar
// previamente el esquema de autenticacion.

// ==========================================
// RUTAS DE CONTROLADORES
// ==========================================

app.MapControllers();

// ==========================================
// COMANDOS DE DESARROLLO
// ==========================================

if (args.Contains("--bootstrap-siso",
        StringComparer.Ordinal)
    || args.Contains("--seed-development-users",
        StringComparer.Ordinal))
{
    await AccountBootstrapper.RunAsync(app, args);
    return;
}

if (args.Contains("--seed-closed-incident",
        StringComparer.Ordinal))
{
    await SuggestionDevelopmentSeeder.RunAsync(app);
    return;
}

// ==========================================
// INICIAR API
// ==========================================

app.Run();

public partial class Program;
