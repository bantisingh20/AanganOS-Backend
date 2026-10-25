namespace CommunityOS.Application.Onboarding;

/// <summary>
/// Resident self-onboarding + admin approval. Anonymous callers (unregistered people) can:
/// check whether a phone already has a login, search communities by name, list a community's wings,
/// list units under a wing, and submit a join request (community + unit + name + 4-digit PIN). A
/// community admin then lists pending requests and approves (which provisions the resident + login +
/// household membership so they can log in) or rejects. One active resident per unit is enforced at
/// submit AND approve.
/// </summary>
public interface IResidentOnboardingService
{
    /// <summary>Anonymous: does a user already exist for this phone/email? Returns only a boolean.</summary>
    Task<bool> IdentifierExistsAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>Anonymous: search communities by name (capped). Returns minimal fields.</summary>
    Task<IReadOnlyList<PublicCommunityDto>> SearchCommunitiesAsync(string? search, CancellationToken cancellationToken = default);

    /// <summary>Anonymous: the wing/block nodes of a community (its hierarchy), for the picker.</summary>
    Task<IReadOnlyList<PublicWingDto>> ListWingsAsync(Guid communityId, CancellationToken cancellationToken = default);

    /// <summary>Anonymous: units in a community, optionally under a wing / matching a number (capped).</summary>
    Task<IReadOnlyList<PublicUnitDto>> ListUnitsAsync(Guid communityId, Guid? wingId, string? search, CancellationToken cancellationToken = default);

    /// <summary>Anonymous: submit a join request. Rejects if the unit is already taken or the phone exists.</summary>
    Task<SubmitJoinRequestResult> SubmitJoinRequestAsync(SubmitJoinRequest request, CancellationToken cancellationToken = default);

    /// <summary>Admin: list pending join requests for the caller's community (permission-gated).</summary>
    Task<IReadOnlyList<JoinRequestDto>> ListPendingJoinRequestsAsync(CancellationToken cancellationToken = default);

    /// <summary>Admin: approve a request → provision the resident + login + household. Idempotent.</summary>
    Task<JoinRequestDto> ApproveJoinRequestAsync(Guid requestId, CancellationToken cancellationToken = default);

    /// <summary>Admin: reject a request with an optional reason.</summary>
    Task<JoinRequestDto> RejectJoinRequestAsync(Guid requestId, string? reason, CancellationToken cancellationToken = default);
}

/// <summary>Minimal public community row for the onboarding search (no org/internal fields).</summary>
public sealed record PublicCommunityDto(Guid Id, string Name, string CommunityType);

/// <summary>A wing/block node for the onboarding picker.</summary>
public sealed record PublicWingDto(Guid Id, string Name, string Level, Guid? ParentId);

/// <summary>A unit for the onboarding picker (number + wing only).</summary>
public sealed record PublicUnitDto(Guid Id, string UnitNumber, Guid? WingId);

/// <summary>Submit a resident join request (anonymous). PIN is the 4-digit login code.</summary>
public sealed record SubmitJoinRequest(
    Guid CommunityId,
    Guid UnitId,
    string Name,
    string Phone,
    string Pin);

/// <summary>The outcome of a submitted request (its id + status, so the app can show "pending").</summary>
public sealed record SubmitJoinRequestResult(Guid RequestId, string Status);

/// <summary>An admin-facing join request row.</summary>
public sealed record JoinRequestDto(
    Guid Id,
    Guid CommunityId,
    Guid UnitId,
    string UnitNumber,
    string Name,
    string Phone,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? DecidedAtUtc,
    string? RejectReason);
