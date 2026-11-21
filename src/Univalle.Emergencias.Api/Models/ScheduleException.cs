
namespace Univalle.Emergencias.Api.Models;

public class ScheduleException
{
    public Guid ScheduleExceptionId { get; set; }

    // Usuario al que pertenece la excepcion
    public Guid CoordinatorUserId { get; set; }

    // Fecha especifica de la excepcion
    public DateOnly Date { get; set; }

    // Indica si estara disponible ese dia
    public bool IsAvailable { get; set; }

    // Horario especial, si corresponde
    public TimeOnly? StartTime { get; set; }
    public TimeOnly? EndTime { get; set; }

    // Motivo de la excepcion
    public string? Reason { get; set; }

    public bool IsActive { get; set; } = true;
}
