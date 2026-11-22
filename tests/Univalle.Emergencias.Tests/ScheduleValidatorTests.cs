
using Univalle.Emergencias.Api.Models;
using Univalle.Emergencias.Api.Services;
using Xunit;

namespace Univalle.Emergencias.Tests;

public class ScheduleValidatorTests
{
    [Fact]
    public void HorarioValido_NoDebeGenerarError()
    {
        var result = ScheduleValidator.ValidateHours(
            new TimeOnly(8, 0),
            new TimeOnly(17, 0));

        Assert.Null(result);
    }

    [Fact]
    public void HorarioInvertido_DebeGenerarError()
    {
        var result = ScheduleValidator.ValidateHours(
            new TimeOnly(17, 0),
            new TimeOnly(8, 0));

        Assert.NotNull(result);
    }

    [Fact]
    public void HorariosSolapados_DebenDetectarse()
    {
        var schedules = new List<WorkSchedule>
        {
            new()
            {
                WorkScheduleId = Guid.NewGuid(),
                CoordinatorUserId = Guid.NewGuid(),
                DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(12, 0),
                IsActive = true
            }
        };

        var result = ScheduleValidator.HasOverlap(
            schedules,
            DayOfWeek.Monday,
            new TimeOnly(10, 0),
            new TimeOnly(15, 0));

        Assert.True(result);
    }

    [Fact]
    public void HorariosConsecutivos_NoDebenSolaparse()
    {
        var schedules = new List<WorkSchedule>
        {
            new()
            {
                WorkScheduleId = Guid.NewGuid(),
                CoordinatorUserId = Guid.NewGuid(),
                DayOfWeek = DayOfWeek.Monday,
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(12, 0)
            }
        };

        var result = ScheduleValidator.HasOverlap(
            schedules,
            DayOfWeek.Monday,
            new TimeOnly(12, 0),
            new TimeOnly(17, 0));

        Assert.False(result);
    }

    [Fact]
    public void AusenciaSinHoras_DebeSerValida()
    {
        var exception = new ScheduleException
        {
            ScheduleExceptionId = Guid.NewGuid(),
            CoordinatorUserId = Guid.NewGuid(),
            Date = new DateOnly(2026, 11, 23),
            IsAvailable = false
        };

        var result =
            ScheduleValidator.ValidateException(exception);

        Assert.Null(result);
    }

    [Fact]
    public void ExcepcionConUnaSolaHora_DebeSerInvalida()
    {
        var exception = new ScheduleException
        {
            ScheduleExceptionId = Guid.NewGuid(),
            CoordinatorUserId = Guid.NewGuid(),
            Date = new DateOnly(2026, 11, 23),
            IsAvailable = true,
            StartTime = new TimeOnly(8, 0)
        };

        var result =
            ScheduleValidator.ValidateException(exception);

        Assert.NotNull(result);
    }
}
