
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Univalle.Emergencias.Api.Notifications;

public sealed class NotificationEntryConfiguration
    : IEntityTypeConfiguration<NotificationEntry>
{
    public void Configure(
        EntityTypeBuilder<NotificationEntry> entity)
    {
        entity.ToTable("NotificationEntries");

        entity.HasKey(x => x.EventId);

        entity.Property(x => x.EventId)
            .ValueGeneratedNever();

        entity.Property(x => x.IncidentId)
            .IsRequired();

        entity.Property(x => x.Type)
            .HasMaxLength(80)
            .IsRequired();

        entity.Property(x => x.RecipientUserId)
            .IsRequired();

        entity.Property(x => x.RegisteredAtUtc)
            .IsRequired();

        entity.Property(x => x.DeliveryStatus)
            .HasMaxLength(30)
            .IsRequired();

        entity.HasIndex(x => new
        {
            x.RecipientUserId,
            x.RegisteredAtUtc
        });
    }
}
