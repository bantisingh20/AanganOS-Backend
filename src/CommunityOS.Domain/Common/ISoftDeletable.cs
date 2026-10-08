namespace CommunityOS.Domain.Common;

/// <summary>
/// Soft-delete marker. Deleting a record flips <see cref="IsDeleted"/> and stamps
/// <see cref="DeletedAtUtc"/>; the row is physically retained for audit (Req 3.7).
/// </summary>
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAtUtc { get; set; }
}
