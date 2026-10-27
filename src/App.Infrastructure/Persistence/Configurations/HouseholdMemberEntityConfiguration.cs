using App.Domain.Community;
using App.Domain.Residents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="HouseholdMember"/> link table (Req 15.2) that associates a
/// <see cref="Resident"/> with a Unit. As a <c>TenantOwnedEntity</c> that is also
/// <c>IUnitScoped</c> it inherits the tenant + soft-delete + <b>resident self-scope</b> global
/// filter (Req 5.4, 66.3) and the rowversion mapping from <c>AppDbContext.OnModelCreating</c> — a
/// resident-self-scoped principal therefore only sees their own household rows.
///
/// <para>Relationships are <c>Restrict</c> on delete so a resident or unit cannot be hard-deleted
/// out from under its household rows; deletes are soft deletes anyway. The
/// <c>(CommunityId, ResidentId)</c> index backs "a resident's household associations" and the
/// <c>(CommunityId, UnitId)</c> index backs "a unit's current/past household" — both as single
/// indexed reads (no N+1).</para>
/// </summary>
public sealed class HouseholdMemberEntityConfiguration : IEntityTypeConfiguration<HouseholdMember>
{
    public void Configure(EntityTypeBuilder<HouseholdMember> builder)
    {
        builder.ToTable("HouseholdMembers");
        builder.HasKey(h => h.Id);

        builder.Property(h => h.Relationship).IsRequired().HasMaxLength(100);

        // Resident → household members (one-to-many). Restrict: keep the link traceable; soft delete
        // handles archival.
        builder.HasOne(h => h.Resident)
            .WithMany(r => r.HouseholdMembers)
            .HasForeignKey(h => h.ResidentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Link to the unit (Req 15.2). No navigation on Unit's side; restrict on delete.
        builder.HasOne<Unit>()
            .WithMany()
            .HasForeignKey(h => h.UnitId)
            .OnDelete(DeleteBehavior.Restrict);

        // Read paths: by resident (a resident's associations) and by unit (a unit's household).
        builder.HasIndex(h => new { h.CommunityId, h.ResidentId })
            .HasDatabaseName("IX_HouseholdMember_CommunityId_ResidentId");
        builder.HasIndex(h => new { h.CommunityId, h.UnitId })
            .HasDatabaseName("IX_HouseholdMember_CommunityId_UnitId");
    }
}
