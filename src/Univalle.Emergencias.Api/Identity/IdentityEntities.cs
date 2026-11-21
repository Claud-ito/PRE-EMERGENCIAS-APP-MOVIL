using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Univalle.Emergencias.Contracts.Identity;

namespace Univalle.Emergencias.Api.Identity;

public sealed class UserAccount
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string NormalizedUsername { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] PasswordSalt { get; set; } = [];
    public byte[] PasswordHash { get; set; } = [];
    public int PasswordIterations { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? DeactivatedAtUtc { get; set; }
    public List<UserSession> Sessions { get; set; } = [];
}

public sealed class UserSession
{
    public Guid SessionId { get; set; }
    public Guid UserId { get; set; }
    public UserAccount User { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}

public sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> entity)
    {
        entity.ToTable("Users");
        entity.HasKey(x => x.UserId);
        entity.Property(x => x.UserId).ValueGeneratedNever();
        entity.Property(x => x.Username).HasMaxLength(50).IsRequired();
        entity.Property(x => x.NormalizedUsername).HasMaxLength(50).IsRequired();
        entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
        entity.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
        entity.Property(x => x.FullName).HasMaxLength(150).IsRequired();
        entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(32).IsRequired();
        entity.Property(x => x.PasswordSalt).HasMaxLength(16).IsRequired();
        entity.Property(x => x.PasswordHash).HasMaxLength(32).IsRequired();
        entity.HasIndex(x => x.NormalizedUsername).IsUnique();
        entity.HasIndex(x => x.NormalizedEmail).IsUnique();
    }
}

public sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> entity)
    {
        entity.ToTable("UserSessions");
        entity.HasKey(x => x.SessionId);
        entity.Property(x => x.SessionId).ValueGeneratedNever();
        entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        entity.HasIndex(x => x.TokenHash).IsUnique();
        entity.HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
        entity.HasOne(x => x.User).WithMany(x => x.Sessions)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
