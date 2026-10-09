using CommunityOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="LoginSession"/> (design Identity table, Req 1.3). A unique index on
/// <c>TokenId</c> gives the revocation check (Task 2.2 bearer validation) and logout an O(1)
/// lookup by the token's <c>jti</c>, and prevents two sessions ever sharing a token id. The
/// <c>(UserId, RevokedAtUtc)</c> index serves "revoke all active sessions for a user" style reads.
/// </summary>
public sealed class LoginSessionEntityConfiguration : IEntityTypeConfiguration<LoginSession>
{
    public void Configure(EntityTypeBuilder<LoginSession> builder)
    {
        builder.ToTable("LoginSessions");

        builder.Property(s => s.TokenId).HasMaxLength(64).IsRequired();
        builder.Property(s => s.RefreshTokenHash).HasMaxLength(512);

        builder.HasIndex(s => s.TokenId)
            .IsUnique()
            .HasDatabaseName("UX_LoginSession_TokenId");

        builder.HasIndex(s => new { s.UserId, s.RevokedAtUtc })
            .HasDatabaseName("IX_LoginSession_User_Revoked");
    }
}
