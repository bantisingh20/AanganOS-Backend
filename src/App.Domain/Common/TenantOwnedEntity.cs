namespace App.Domain.Common;

/// <summary>
/// Base for every community-owned entity. Carries the tenant discriminator
/// (<see cref="CommunityId"/>, Req 5.1), soft-delete (Req 3.7), an optimistic-concurrency
/// token (Req 22.4) and audit columns. The DbContext registers a global query filter on
/// all <see cref="TenantOwnedEntity"/> types so ordinary queries physically cannot return
/// foreign-tenant or soft-deleted rows (Req 5.2, 5.3, 5.5).
/// </summary>
public abstract class TenantOwnedEntity : ITenantOwned, IAuditable, ISoftDeletable
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CommunityId { get; set; }

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
