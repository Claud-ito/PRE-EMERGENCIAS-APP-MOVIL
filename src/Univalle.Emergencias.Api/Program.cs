
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Incidents;
using Univalle.Emergencias.Api.Identity;
using Univalle.Emergencias.Api.Suggestions;
using Univalle.Emergencias.Api.Services;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Common;
<<<<<<< Updated upstream
using Univalle.Emergencias.Contracts.Identity;
=======
using Univalle.Emergencias.Contracts.Turns;
>>>>>>> Stashed changes

var builder = WebApplication.CreateBuilder(args);

// Configuracion de controladores y respuestas JSON
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

            problem.Extensions["code"] = ApiErrorCodes.InvalidInput;

            return new BadRequestObjectResult(problem);
        };
    });

// OpenAPI
builder.Services.AddOpenApi();

// Manejo de errores
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        var problem = context.ProblemDetails;

        if (!problem.Extensions.ContainsKey("code"))
        {
            problem.Extensions["code"] = problem.Status switch
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

// Conexion a SQL Server
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

// Entity Framework Core
builder.Services.AddDbContext<AppDbContext>(
    options => options.UseSqlServer(connectionString));

// Servicios originales de Said
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IServerClock, SystemServerClock>();
builder.Services.AddScoped<IAuditTrail, EfAuditTrail>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IIdentityContext, DatabaseIdentityContext>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<Univalle.Emergencias.Contracts.Diana.IBrigadeUserDirectory, BrigadeUserDirectory>();
builder.Services.AddScoped<SuggestionService>();

// Módulos (cada dueño registra los suyos).
Univalle.Emergencias.Api.Diana.DianaRegistration.AddDianaModules(builder.Services);
builder.Services.AddIncidentModule();

// Sustituciones de desarrollo: siempre al final, después de los módulos reales.
if (builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue("IncidentDevSubstitutes:Enabled", true))
{
    builder.Services.AddIncidentDevelopmentSubstitutes();
}

// ==========================================
// MODULO DE TURNOS - JORGE
// ==========================================

// Servicio para consultar los turnos desde SQL Server
builder.Services.AddScoped<ITurnResolver, DatabaseTurnResolver>();

// ==========================================
// CONSTRUCCION DE LA APLICACION
// ==========================================

// IMPORTANTE: Todos los servicios deben
// registrarse ANTES de esta instruccion.
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Manejo de excepciones HTTP
app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Registrar las rutas de los controladores
app.MapControllers();

if (args.Contains("--bootstrap-siso", StringComparer.Ordinal)
    || args.Contains("--seed-development-users", StringComparer.Ordinal))
{
    await AccountBootstrapper.RunAsync(app, args);
    return;
}
if (args.Contains("--seed-closed-incident", StringComparer.Ordinal))
{
    await SuggestionDevelopmentSeeder.RunAsync(app);
    return;
}

app.Run();

public partial class Program;
