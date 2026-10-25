using UrbanNest.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="OtpToken"/> (design Identity table, Req 2.1–2.4). The code is persisted only as
/// a salted <c>HashedCode</c>, never plaintext. The <c>(UserId, PurposeType, UsedAtUtc)</c> index
/// serves the redemption lookup (a user's unused reset OTPs) the Auth_Service reset flow runs.
/// </summary>
public sealed class OtpTokenEntityConfiguration : IEntityTypeConfiguration<OtpToken>
{
    public void Configure(EntityTypeBuilder<OtpToken> builder)
    {
        builder.ToTable("OtpTokens");

        builder.Property(o => o.HashedCode).HasMaxLength(512).IsRequired();
        builder.Property(o => o.PurposeType).HasConversion<int>();

        builder.HasIndex(o => new { o.UserId, o.PurposeType, o.UsedAtUtc })
            .HasDatabaseName("IX_OtpToken_User_Purpose_Used");
    }
}
