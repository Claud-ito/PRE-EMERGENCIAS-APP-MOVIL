using System.ComponentModel.DataAnnotations;
using Univalle.Emergencias.Contracts.Identity;

namespace Univalle.Emergencias.Contracts.Diana;

public sealed record ModuleInput([param: Required, StringLength(30)] string Code,
    [param: Required, StringLength(150)] string Name);
public sealed record FloorInput([param: Required, StringLength(100)] string Name);
public enum SupportContactKind { Consultorio, CentroMedico, Bomberos }
public sealed record SupportContactInput(
    [param: Required, StringLength(150)] string Name,
    SupportContactKind Kind,
    [param: Required, StringLength(40)] string Phone);
public sealed record SupportContactView(Guid ContactId, string Name, SupportContactKind Kind,
    string Phone, bool IsActive);
public sealed record BrigadeMemberInput(Guid UserId, [param: Required] string Specialty);
public sealed record BrigadeInput([param: Required, StringLength(150)] string Name,
    Guid LeaderUserId, IReadOnlyList<BrigadeMemberInput> Members, IReadOnlyList<Guid> ModuleIds);
public sealed record BrigadeView(Guid BrigadeId, string Name, Guid LeaderUserId, bool IsActive,
    IReadOnlyList<BrigadeMemberInput> Members, IReadOnlyList<Guid> ModuleIds);
public sealed record BrigadeUser(Guid UserId, string DisplayName, UserRole Role, bool IsActive);

/// <summary>Adaptador de SA-02: consulta cuentas reales; no se mantienen cuentas paralelas.</summary>
public interface IBrigadeUserDirectory
{
    Task<IReadOnlyList<BrigadeUser>> GetAssignableUsersAsync(CancellationToken cancellationToken = default);
}

/// <summary>Base RF10 con una plantilla opcional, ficticia y provisional para ensayos.</summary>
public sealed record InjuryRecordView(Guid InjuryRecordId, Guid IncidentId, Guid PhysicianUserId,
    DateTimeOffset CreatedAtUtc, TestClinicalDraft? TestClinicalDraft = null);
public sealed record CreateInjuryRecordInput(bool EvaluatedOrAttended, TestClinicalDraft? TestClinicalDraft = null);

/// <summary>Esquema de ensayo; no representa una plantilla aprobada ni admite datos reales.</summary>
public sealed record TestClinicalDraft(
    [param: Required, StringLength(100)] string PatientAlias,
    [param: Range(0, 120)] int? Age,
    [param: Required, StringLength(1500)] string Observations,
    [param: Required, StringLength(1500)] string CareProvided,
    [param: Required, StringLength(1500)] string Report,
    bool IsTestData = true);
public sealed record TestClinicalReport(Guid InjuryRecordId, Guid IncidentId, Guid PhysicianUserId,
    DateTimeOffset CreatedAtUtc, string TemplateVersion, bool IsTestData, string Text);

public sealed record InjuryIncidentView(Guid IncidentId, string ModuleName, string FloorName,
    string Status, DateTimeOffset OpenedAtUtc);

public sealed record BrigadeMemberView(Guid UserId, string DisplayName, string Specialty);
public sealed record BrigadeLeaderView(Guid BrigadeId, string BrigadeName, Guid LeaderUserId,
    string LeaderName, IReadOnlyList<BrigadeMemberView> Members);
public sealed record ModuleBrigadeStatus(Guid ModuleId, BrigadeLeaderView? Leader);
