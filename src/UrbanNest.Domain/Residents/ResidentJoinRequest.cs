using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Residents;

/// <summary>
/// A resident's self-service request to join a community (mobile onboarding). An UNREGISTERED person
/// searches for their community, picks their wing + unit, sets a login PIN and submits this request;
/// a community admin then approves (which provisions the resident + login + household membership) or
/// rejects it. Until approved the person cannot log in.
///
/// <para>Belongs to the target community (<see cref="TenantOwnedEntity.CommunityId"/>) and carries the
/// chosen <see cref="UnitId"/>. The login PIN is stored ONLY as a salted hash (never plaintext); it is
/// moved onto the created <c>User.PasswordHash</c> at approval. One active resident per unit is
/// enforced in the service (no DB unique index), checked at BOTH submit and approve.</para>
/// </summary>
public class ResidentJoinRequest : TenantOwnedEntity
{
    /// <summary>The unit the requester claims to live in (their chosen wing's room). Required.</summary>
    public Guid UnitId { get; set; }

    /// <summary>The requester's display name. Required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The phone number the requester will log in with (the identifier). Required.</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>Salted hash of the 4-digit login PIN. Never the plaintext PIN. Required.</summary>
    public string PinHash { get; set; } = string.Empty;

    /// <summary>Lifecycle status: <c>pending</c> | <c>approved</c> | <c>rejected</c>.</summary>
    public string Status { get; set; } = ResidentJoinRequestStatuses.Pending;

    /// <summary>The admin user id that approved/rejected, or null while pending.</summary>
    public string? DecidedBy { get; set; }

    /// <summary>When the request was approved/rejected, or null while pending.</summary>
    public DateTime? DecidedAtUtc { get; set; }

    /// <summary>Optional reason captured on rejection.</summary>
    public string? RejectReason { get; set; }

    /// <summary>The resident row created on approval (so a repeat approve is a no-op), else null.</summary>
    public Guid? CreatedResidentId { get; set; }
}

/// <summary>The well-known join-request status codes.</summary>
public static class ResidentJoinRequestStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}
