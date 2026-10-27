using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="Domain.Community.Community"/> tenant-root table (Req 12.1). As a
/// <c>TenantOwnedEntity</c> it inherits the global tenant + soft-delete query filter and the
/// rowversion mapping from <c>AppDbContext.OnModelCreating</c>; because the row's own
/// <c>CommunityId</c> is set equal to its <c>Id</c> on create, that standard filter scopes the
/// Community row itself to the owning tenant with no bespoke predicate (see the modeling note on
/// the entity). Soft-delete keeps an archived Community traceable (Req 12.5).
/// </summary>
public sealed class CommunityEntityConfiguration : IEntityTypeConfiguration<Domain.Community.Community>
{
    public void Configure(EntityTypeBuilder<Domain.Community.Community> builder)
    {
        builder.ToTable("Communities");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.CommunityType).IsRequired().HasMaxLength(100);

        // "Which communities belong to this organization?" lookups.
        builder.HasIndex(c => c.OrganizationId)
            .HasDatabaseName("IX_Community_OrganizationId");

        builder.HasOne<Domain.Community.Organization>()
            .WithMany()
            .HasForeignKey(c => c.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
