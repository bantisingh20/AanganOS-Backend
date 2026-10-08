namespace CommunityOS.Domain.Common;

/// <summary>
/// Audit columns stamped by the persistence layer (never set by callers directly).
/// </summary>
public interface IAuditable
{
    DateTime CreatedAtUtc { get; set; }
    string? CreatedBy { get; set; }
    DateTime? UpdatedAtUtc { get; set; }
    string? UpdatedBy { get; set; }
}
