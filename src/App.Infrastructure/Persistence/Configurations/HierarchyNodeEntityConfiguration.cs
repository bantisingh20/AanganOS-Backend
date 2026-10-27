using App.Domain.Community;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace App.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="HierarchyNode"/> self-referencing tree (Req 13.1). As a
/// <c>TenantOwnedEntity</c> it inherits the global tenant + soft-delete filter and the rowversion
/// mapping from <c>AppDbContext.OnModelCreating</c>, so hierarchy reads are tenant-scoped (Req 13.4)
/// and delete is a soft delete that stays traceable (Req 13.5). The <c>(CommunityId, ParentId)</c>
/// index backs the "load a community's whole hierarchy" read as a single indexed scan — no N+1.
/// </summary>
public sealed class HierarchyNodeEntityConfiguration : IEntityTypeConfiguration<HierarchyNode>
{
    public void Configure(EntityTypeBuilder<HierarchyNode> builder)
    {
        builder.ToTable("HierarchyNodes");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.Name).IsRequired().HasMaxLength(200);
        builder.Property(n => n.Level).IsRequired().HasMaxLength(100);

        // Self-reference: parent → children, both within the same community (enforced in the
        // service). Restrict so a parent cannot be hard-deleted out from under its children; delete
        // is a soft delete anyway.
        builder.HasOne(n => n.Parent)
            .WithMany(n => n.Children)
            .HasForeignKey(n => n.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Primary read path: fetch a community's hierarchy (optionally a subtree by parent).
        builder.HasIndex(n => new { n.CommunityId, n.ParentId })
            .HasDatabaseName("IX_HierarchyNode_CommunityId_ParentId");
    }
}
