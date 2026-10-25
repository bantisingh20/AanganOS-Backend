namespace UrbanNest.Domain.Common;

/// <summary>
/// Base for platform-scoped entities (e.g. Organization, Permission, platform Configuration)
/// that are NOT owned by a single community. Carries the same audit, soft-delete and
/// optimistic-concurrency columns as <see cref="TenantOwnedEntity"/> but no CommunityId.
/// </summary>
public abstract class PlatformEntity : IAuditable, ISoftDeletable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Soft delete (Req 3.7)
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }

    // Optimistic concurrency token (Req 22.4). Mapped as a rowversion/timestamp.
    public byte[] RowVersion { get; set; } = [];

    // Audit columns (stamped by the SaveChanges interceptor).
    public DateTime CreatedAtUtc { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}
