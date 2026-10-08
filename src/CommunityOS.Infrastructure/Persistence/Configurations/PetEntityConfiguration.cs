using CommunityOS.Domain.Community;
using CommunityOS.Domain.Residents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="Pet"/> table (Req 19.3). As a <c>TenantOwnedEntity</c> that is also
/// <c>IUnitScoped</c> it inherits the tenant + soft-delete + resident self-scope global filter
/// (Req 5.4, 19.5) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> — a resident
/// principal only sees their own unit's pets.
///
/// <para>No uniqueness constraint — a unit may register several pets (Req 19.3 stores type + owning
/// unit only). The <c>(CommunityId, UnitId)</c> index backs "a unit's pets" as a single indexed read
/// (no N+1). The unit FK is <c>Restrict</c> on delete; deletes are soft deletes anyway.</para>
/// </summary>
public sealed class PetEntityConfiguration : IEntityTypeConfiguration<Pet>
{
    public void Configure(EntityTypeBuilder<Pet> builder)
    {
        builder.ToTable("Pets");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.PetType).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Name).HasMaxLength(200);

        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(p => p.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => new { p.CommunityId, p.UnitId })
            .HasDatabaseName("IX_Pet_CommunityId_UnitId");
    }
}
