using UrbanNest.Domain.Community;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the platform-scoped <see cref="Organization"/> table (Req 12.1). As a
/// <c>PlatformEntity</c> it inherits the soft-delete global filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c> — no tenant predicate (an Organization owns communities, it
/// is not owned by one).
/// </summary>
public sealed class OrganizationEntityConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Name).IsRequired().HasMaxLength(200);
    }
}
