using System.Text.Json;
using CommunityOS.Domain.Community;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the <see cref="Unit"/> table (Req 14.1–14.4). As a <c>TenantOwnedEntity</c> it inherits the
/// global tenant + soft-delete query filter and the rowversion mapping from
/// <c>AppDbContext.OnModelCreating</c>, so unit reads are tenant-scoped (Req 5.2), optimistic
/// concurrency on edit is enforced (Req 22.4) and delete is a soft delete that stays traceable
/// (Req 14.6).
///
/// <para><b>Concurrency-safe uniqueness (Req 14.4, 22.1).</b> The unique index on
/// <c>(CommunityId, UnitNumber)</c> is the single source of truth for unit-number uniqueness: two
/// concurrent creates race to the index and exactly one wins. The service does NOT pre-check with a
/// read-then-write; it lets the index reject the loser and translates the violation to a CONFLICT
/// (via <c>UniqueConstraintGuard</c>).</para>
///
/// <para><b>List-filter index (Req 14.5).</b> <c>(CommunityId, UnitStatus)</c> backs the
/// "units in this community with status X" list the Task 11.3 endpoint runs, so it hits an index
/// at scale.</para>
///
/// <para><b>Custom attributes (Req 14.1).</b> <see cref="Unit.CustomAttributes"/> is persisted as a
/// single JSON string column via a value converter — provider-agnostic (works the same on the SQL
/// Server production path and the SQLite test path, neither needing native JSON-column support) and
/// free-form, so a community can store arbitrary key/values without a schema change.</para>
/// </summary>
public sealed class UnitEntityConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("Units");
        builder.HasKey(u => u.Id);

        builder.Property(u => u.UnitNumber).IsRequired().HasMaxLength(100);
        builder.Property(u => u.UnitType).IsRequired().HasMaxLength(100);
        builder.Property(u => u.UnitStatus).IsRequired().HasMaxLength(100);
        builder.Property(u => u.Floor).HasMaxLength(100);
        builder.Property(u => u.Area).HasColumnType("decimal(18,2)");

        // Free-form custom attributes stored as JSON (Req 14.1). A value converter keeps the SQL
        // Server and SQLite paths identical; a value comparer lets EF detect in-place dictionary
        // edits so an attribute change is persisted.
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        builder.Property(u => u.CustomAttributes)
            .HasConversion(
                v => JsonSerializer.Serialize(v, jsonOptions),
                v => string.IsNullOrWhiteSpace(v)
                    ? new Dictionary<string, string>()
                    : JsonSerializer.Deserialize<Dictionary<string, string>>(v, jsonOptions) ?? new Dictionary<string, string>())
            .Metadata.SetValueComparer(new ValueComparer<IDictionary<string, string>>(
                (a, b) => JsonSerializer.Serialize(a, jsonOptions) == JsonSerializer.Serialize(b, jsonOptions),
                v => v == null ? 0 : JsonSerializer.Serialize(v, jsonOptions).GetHashCode(),
                v => v == null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(v)));
        builder.Property(u => u.CustomAttributes).HasColumnName("CustomAttributes");

        // Optional link to the Floor/Building/… node (Req 14.1). Restrict so a node cannot be hard-
        // deleted out from under its units; delete is a soft delete anyway.
        builder.HasOne<HierarchyNode>()
            .WithMany()
            .HasForeignKey(u => u.HierarchyNodeId)
            .OnDelete(DeleteBehavior.Restrict);

        // DB-enforced unit-number uniqueness within a community (Req 14.4, 22.1): two concurrent
        // creates race to this index and exactly one wins — the single source of truth, not an
        // application read-then-write. ponytail: a soft-deleted number is not reusable while the
        // archived row remains; reuse-after-archive would need a filtered index (SQL Server only) and
        // isn't a Task 11.2 requirement.
        builder.HasIndex(u => new { u.CommunityId, u.UnitNumber })
            .IsUnique()
            .HasDatabaseName("UX_Unit_CommunityId_UnitNumber");

        // List-filter index (Req 14.5): units in a community by status.
        builder.HasIndex(u => new { u.CommunityId, u.UnitStatus })
            .HasDatabaseName("IX_Unit_CommunityId_UnitStatus");
    }
}
