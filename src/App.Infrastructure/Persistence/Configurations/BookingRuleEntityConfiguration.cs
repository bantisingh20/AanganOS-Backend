using App.Domain.Amenities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="BookingRule"/> table (Req 38.3) — the per-amenity configurable booking-rule
/// set. As a <c>TenantOwnedEntity</c> it inherits the tenant + soft-delete global filter and the
/// rowversion mapping from <c>AppDbContext.OnModelCreating</c>, so reads are tenant-scoped. It is
/// deliberately NOT <c>IUnitScoped</c> — rule configuration is community infrastructure.
///
/// <para><b>One rule-set per amenity (Req 38.3).</b> The unique index on <c>(AmenityId)</c> makes
/// "an amenity has at most one rule-set" a DB guarantee, so the service's upsert
/// (<c>SetBookingRuleAsync</c>) is race-safe — a concurrent duplicate insert maps to CONFLICT via
/// <c>UniqueConstraintGuard</c> rather than a read-then-write gap. The index is also the single
/// indexed read the rule lookup rides.</para>
///
/// <para><b>Money precision (steering).</b> <see cref="BookingRule.BookingCharge"/>,
/// <see cref="BookingRule.SecurityDeposit"/> and <see cref="BookingRule.RefundPercentOnCancellation"/>
/// are decimals mapped with explicit precision, never floats.</para>
/// </summary>
public sealed class BookingRuleEntityConfiguration : IEntityTypeConfiguration<BookingRule>
{
    public void Configure(EntityTypeBuilder<BookingRule> builder)
    {
        builder.ToTable("BookingRules");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.AllowedResidentTypes).HasMaxLength(500);
        builder.Property(r => r.BookingCharge).HasPrecision(18, 2);
        builder.Property(r => r.SecurityDeposit).HasPrecision(18, 2);
        builder.Property(r => r.RefundPercentOnCancellation).HasPrecision(5, 2);

        // Link to the owning amenity (Req 38.3). Restrict on delete; deletes are soft deletes anyway.
        builder.HasOne<Amenity>()
            .WithMany()
            .HasForeignKey(r => r.AmenityId)
            .OnDelete(DeleteBehavior.Restrict);

        // One rule-set per amenity (Req 38.3): the DB-level source of truth behind the upsert, so a
        // concurrent duplicate insert maps to CONFLICT rather than a read-then-write gap.
        builder.HasIndex(r => r.AmenityId)
            .IsUnique()
            .HasDatabaseName("UX_BookingRule_AmenityId");
    }
}
