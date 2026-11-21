using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Univalle.Emergencias.Api.Data;
using Univalle.Emergencias.Api.Incidents;
using Univalle.Emergencias.Api.Identity;
using Univalle.Emergencias.Api.Services;
using Univalle.Emergencias.Contracts.Audit;
using Univalle.Emergencias.Contracts.Common;
using Univalle.Emergencias.Contracts.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var problem = new ValidationProblemDetails(context.ModelState)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "La solicitud no es válida."
            };
            problem.Extensions["code"] = ApiErrorCodes.InvalidInput;
            return new BadRequestObjectResult(problem);
        };
    });
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = context =>
    {
        var problem = context.ProblemDetails;
        if (!problem.Extensions.ContainsKey("code"))
        {
            problem.Extensions["code"] = problem.Status switch
            {
                StatusCodes.Status400BadRequest => ApiErrorCodes.InvalidInput,
                StatusCodes.Status401Unauthorized => ApiErrorCodes.Unauthenticated,
                StatusCodes.Status403Forbidden => ApiErrorCodes.Forbidden,
                StatusCodes.Status404NotFound => ApiErrorCodes.NotFound,
                _ => ApiErrorCodes.Unexpected
            };
        }
    };
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Configure ConnectionStrings:DefaultConnection mediante appsettings.Development.json o una variable de entorno.");
}

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IServerClock, SystemServerClock>();
builder.Services.AddScoped<IAuditTrail, EfAuditTrail>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IIdentityContext, DatabaseIdentityContext>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<Univalle.Emergencias.Contracts.Diana.IBrigadeUserDirectory, BrigadeUserDirectory>();

// Módulos (cada dueño registra los suyos).
Univalle.Emergencias.Api.Diana.DianaRegistration.AddDianaModules(builder.Services);
builder.Services.AddIncidentModule();

// Sustituciones de desarrollo: siempre al final, después de los módulos reales.
if (builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue("IncidentDevSubstitutes:Enabled", true))
{
    builder.Services.AddIncidentDevelopmentSubstitutes();
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.MapControllers();

if (args.Contains("--bootstrap-siso", StringComparer.Ordinal)
    || args.Contains("--seed-development-users", StringComparer.Ordinal))
{
    await AccountBootstrapper.RunAsync(app, args);
    return;
}

app.Run();

public partial class Program;
