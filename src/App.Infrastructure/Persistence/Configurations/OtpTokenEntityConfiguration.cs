using App.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;


public sealed class OtpTokenEntityConfiguration : IEntityTypeConfiguration<OtpToken>
{
    public void Configure(EntityTypeBuilder<OtpToken> builder)
    {
        builder.ToTable("OtpTokens");

        builder.Property(o => o.HashedCode).HasMaxLength(512).IsRequired();
        builder.Property(o => o.PurposeType).HasConversion<int>();
        builder.Property(o => o.Phone).HasMaxLength(32);

        builder.HasIndex(o => new { o.UserId, o.PurposeType, o.UsedAtUtc })
            .HasDatabaseName("IX_OtpToken_User_Purpose_Used");
    }
}
