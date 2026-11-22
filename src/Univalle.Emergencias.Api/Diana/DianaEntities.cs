using Univalle.Emergencias.Contracts.Diana;

namespace Univalle.Emergencias.Api.Diana;

public sealed class LocationModule
{
    public Guid ModuleId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
public sealed class LocationFloor
{
    public Guid FloorId { get; set; }
    public Guid ModuleId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
public sealed class SupportContact
{
    public Guid ContactId { get; set; }
    public string Name { get; set; } = "";
    public SupportContactKind Kind { get; set; }
    public string Phone { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
public sealed class Brigade
{
    public Guid BrigadeId { get; set; }
    public string Name { get; set; } = "";
    public Guid LeaderUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public List<BrigadeMembership> Members { get; set; } = [];
}
public sealed class BrigadeMembership
{
    public Guid BrigadeId { get; set; }
    public Guid UserId { get; set; }
    public string Specialty { get; set; } = "";
}
// Las asignaciones cerradas conservan el líder y los integrantes que tuvieron.
public sealed class BrigadeModuleAssignment
{
    public Guid AssignmentId { get; set; }
    public Guid BrigadeId { get; set; }
    public Guid ModuleId { get; set; }
    public Guid LeaderUserId { get; set; }
    public string MembersJson { get; set; } = "[]";
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }
}
public sealed class InjuryRecord
{
    public Guid InjuryRecordId { get; set; }
    public Guid IncidentId { get; set; }
    public Guid PhysicianUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public string? TestClinicalDraftJson { get; set; }
}
