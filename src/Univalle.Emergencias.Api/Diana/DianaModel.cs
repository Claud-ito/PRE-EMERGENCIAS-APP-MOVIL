using Microsoft.EntityFrameworkCore;

namespace Univalle.Emergencias.Api.Diana;

public static class DianaModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<LocationModule>(e => {
            e.ToTable("LocationModules"); e.HasKey(x => x.ModuleId);
            e.Property(x => x.ModuleId).ValueGeneratedNever();
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
        });
        model.Entity<LocationFloor>(e => {
            e.ToTable("LocationFloors"); e.HasKey(x => x.FloorId);
            e.Property(x => x.FloorId).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.ModuleId, x.Name }).IsUnique();
            e.HasOne<LocationModule>().WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<SupportContact>(e => {
            e.ToTable("SupportContacts"); e.HasKey(x => x.ContactId);
            e.Property(x => x.ContactId).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(40).IsRequired();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
        });
        model.Entity<Brigade>(e => {
            e.ToTable("Brigades"); e.HasKey(x => x.BrigadeId);
            e.Property(x => x.BrigadeId).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.BrigadeId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<BrigadeMembership>(e => {
            e.ToTable("BrigadeMembers"); e.HasKey(x => new { x.BrigadeId, x.UserId });
            e.Property(x => x.Specialty).HasMaxLength(100).IsRequired();
        });
        model.Entity<BrigadeModuleAssignment>(e => {
            e.ToTable("BrigadeModuleAssignments"); e.HasKey(x => x.AssignmentId);
            e.Property(x => x.AssignmentId).ValueGeneratedNever();
            e.Property(x => x.MembersJson).IsRequired();
            e.HasOne<Brigade>().WithMany().HasForeignKey(x => x.BrigadeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LocationModule>().WithMany().HasForeignKey(x => x.ModuleId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ModuleId).IsUnique().HasFilter("[EndedAtUtc] IS NULL");
            e.HasIndex(x => new { x.ModuleId, x.StartedAtUtc });
        });
        model.Entity<InjuryRecord>(e => {
            e.ToTable("InjuryRecords"); e.HasKey(x => x.InjuryRecordId);
            e.Property(x => x.InjuryRecordId).ValueGeneratedNever();
            e.HasIndex(x => x.IncidentId);
            // La entidad es la de AL-01: no se duplica el catálogo de incidentes.
            e.HasOne<Univalle.Emergencias.Api.Incidents.Incident>().WithMany()
                .HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
