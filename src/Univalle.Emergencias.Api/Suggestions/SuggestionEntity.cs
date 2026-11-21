using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Univalle.Emergencias.Api.Identity;
using Univalle.Emergencias.Api.Incidents;

namespace Univalle.Emergencias.Api.Suggestions;

public sealed class IncidentSuggestion
{
    public Guid SuggestionId { get; set; }
    public Guid IncidentId { get; set; }
    public Incident Incident { get; set; } = null!;
    public Guid AuthorUserId { get; set; }
    public UserAccount Author { get; set; } = null!;
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class IncidentSuggestionConfiguration : IEntityTypeConfiguration<IncidentSuggestion>
{
    public void Configure(EntityTypeBuilder<IncidentSuggestion> entity)
    {
        entity.ToTable("IncidentSuggestions");
        entity.HasKey(x => x.SuggestionId);
        entity.Property(x => x.SuggestionId).ValueGeneratedNever();
        entity.Property(x => x.Text).HasMaxLength(1000).IsRequired();
        entity.Property(x => x.CreatedAtUtc).IsRequired();
        entity.HasIndex(x => new { x.IncidentId, x.CreatedAtUtc });
        entity.HasOne(x => x.Incident).WithMany()
            .HasForeignKey(x => x.IncidentId).OnDelete(DeleteBehavior.Restrict);
        entity.HasOne(x => x.Author).WithMany()
            .HasForeignKey(x => x.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
