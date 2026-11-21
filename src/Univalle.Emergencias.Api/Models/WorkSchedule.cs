
using System;

namespace Univalle.Emergencias.Api.Models;

public class WorkSchedule
{
    public Guid WorkScheduleId { get; set; }

    // Usuario encargado: SySO o Mantenimiento
    public Guid CoordinatorUserId { get; set; }

    // Dia de la semana
    public DayOfWeek DayOfWeek { get; set; }

    // Horario de entrada y salida
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    // Permite desactivar horarios sin eliminarlos
    public bool IsActive { get; set; } = true;
}
