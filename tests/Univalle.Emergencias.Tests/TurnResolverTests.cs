
using Univalle.Emergencias.Api.Models;
using Univalle.Emergencias.Api.Services;
using Xunit;

namespace Univalle.Emergencias.Tests;

public class TurnResolverTests
{
    private readonly Guid sysoId = Guid.NewGuid();
    private readonly Guid mantenimientoId = Guid.NewGuid();

    private TurnResolver CrearServicio(
        List<ScheduleException>? excepciones = null)
    {
        var horarios = new List<WorkSchedule>
        {
            new()
            {
                WorkScheduleId = Guid.NewGuid(),
                CoordinatorUserId = sysoId,
                DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(17, 0)
            },
            new()
            {
                WorkScheduleId = Guid.NewGuid(),
                CoordinatorUserId = mantenimientoId,
                DayOfWeek = DayOfWeek.Tuesday,
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(17, 0)
            }
        };

        return new TurnResolver(
            horarios,
            excepciones ?? new List<ScheduleException>());
    }

    [Fact]
    public async Task Lunes_DebeAsignarSySO()
    {
        var servicio = CrearServicio();

        // Lunes 23 de noviembre, 10:00 Bolivia
        var fecha = new DateTimeOffset(
            2026, 11, 23, 14, 0, 0, TimeSpan.Zero);

        var resultado = await servicio.ResolveAsync(fecha);

        Assert.NotNull(resultado);
        Assert.Equal(sysoId, resultado.CoordinatorUserId);
    }

    [Fact]
    public async Task Martes_DebeAsignarMantenimiento()
    {
        var servicio = CrearServicio();

        // Martes 24 de noviembre, 10:00 Bolivia
        var fecha = new DateTimeOffset(
            2026, 11, 24, 14, 0, 0, TimeSpan.Zero);

        var resultado = await servicio.ResolveAsync(fecha);

        Assert.NotNull(resultado);
        Assert.Equal(
            mantenimientoId,
            resultado.CoordinatorUserId);
    }

    [Fact]
    public async Task FueraDeHorario_NoDebeAsignar()
    {
        var servicio = CrearServicio();

        // Lunes 23 de noviembre, 20:00 Bolivia
        var fecha = new DateTimeOffset(
            2026, 11, 24, 0, 0, 0, TimeSpan.Zero);

        var resultado = await servicio.ResolveAsync(fecha);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task Ausencia_NoDebeAsignar()
    {
        var excepciones = new List<ScheduleException>
        {
            new()
            {
                ScheduleExceptionId = Guid.NewGuid(),
                CoordinatorUserId = sysoId,
                Date = new DateOnly(2026, 11, 23),
                IsAvailable = false,
                Reason = "Vacaciones"
            }
        };

        var servicio = CrearServicio(excepciones);

        var fecha = new DateTimeOffset(
            2026, 11, 23, 14, 0, 0, TimeSpan.Zero);

        var resultado = await servicio.ResolveAsync(fecha);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task HorarioEspecial_DebeAplicarse()
    {
        var excepciones = new List<ScheduleException>
        {
            new()
            {
                ScheduleExceptionId = Guid.NewGuid(),
                CoordinatorUserId = sysoId,
                Date = new DateOnly(2026, 11, 23),
                IsAvailable = true,
                StartTime = new TimeOnly(12, 0),
                EndTime = new TimeOnly(18, 0)
            }
        };

        var servicio = CrearServicio(excepciones);

        // Lunes 13:00 Bolivia
        var fecha = new DateTimeOffset(
            2026, 11, 23, 17, 0, 0, TimeSpan.Zero);

        var resultado = await servicio.ResolveAsync(fecha);

        Assert.NotNull(resultado);
        Assert.Equal(sysoId, resultado.CoordinatorUserId);
    }

    [Fact]
    public async Task HorariosSolapados_NoDebeAsignar()
    {
        var horarios = new List<WorkSchedule>
        {
            new()
            {
                WorkScheduleId = Guid.NewGuid(),
                CoordinatorUserId = sysoId,
                DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(17, 0)
            },
            new()
            {
                WorkScheduleId = Guid.NewGuid(),
                CoordinatorUserId = mantenimientoId,
                DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(16, 0)
            }
        };

        var servicio = new TurnResolver(
            horarios,
            new List<ScheduleException>());

        var fecha = new DateTimeOffset(
            2026, 11, 23, 14, 0, 0, TimeSpan.Zero);

        var resultado = await servicio.ResolveAsync(fecha);

        Assert.Null(resultado);
    }
}
