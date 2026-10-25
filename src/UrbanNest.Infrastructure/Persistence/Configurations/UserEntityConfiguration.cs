using UrbanNest.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="User"/>: Unique(Email) and Unique(Phone) (design Identity table, Req 3.1).
/// Both columns are nullable (a user may be email- or phone-only), so uniqueness uses <b>filtered</b>
/// unique indexes that ignore NULLs — otherwise several NULL-email rows would collide. Same technique
/// the Configuration/MasterData maps use for their nullable discriminators.
/// </summary>
public sealed class UserEntityConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.Property(u => u.Email).HasMaxLength(256);
        builder.Property(u => u.Phone).HasMaxLength(32);
        builder.Property(u => u.PasswordHash).HasMaxLength(512);
        builder.Property(u => u.Status).HasConversion<int>();

        // One ACTIVE account per email, ignoring users with no email and soft-deleted users
        // (filtered unique index). A soft-deleted user frees their email for reuse — only live rows
        // compete for uniqueness.
        builder.HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter("[Email] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName("UX_User_Email");

        // One ACTIVE account per phone, ignoring users with no phone and soft-deleted users. A
        // soft-deleted user frees their phone for reuse (the phone is the login identifier).
        builder.HasIndex(u => u.Phone)
            .IsUnique()
            .HasFilter("[Phone] IS NOT NULL AND [IsDeleted] = 0")
            .HasDatabaseName("UX_User_Phone");
    }
}
