using CommunityOS.Application.Auth;

namespace CommunityOS.Application.Onboarding;

/// <summary>
/// Public self-service onboarding: an UNAUTHENTICATED caller creates their own community and becomes
/// its admin. In one atomic flow it provisions an organization + community, a user credential, and a
/// CommunityAdmin role assignment SCOPED to the new community (CommunityId = the new community), then
/// logs the user in. The resulting admin can only ever see/manage THAT community's data — the role
/// assignment is community-scoped, so the global tenant filter isolates them from every other
/// community (never a platform super-admin).
/// </summary>
public interface IOnboardingService
{
    Task<OnboardingResult> CreateCommunityAsync(
        CreateCommunityOnboardingRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Inputs for self-service community creation. The admin signs in afterwards with
/// <paramref name="AdminIdentifier"/> (email or phone) + <paramref name="AdminPassword"/>.
/// </summary>
public sealed record CreateCommunityOnboardingRequest(
    string CommunityName,
    string CommunityType,
    string AdminName,
    string? AdminEmail,
    string? AdminPhone,
    string AdminPassword);

/// <summary>
/// The outcome: the created community's identity plus a ready-to-use session so the portal logs the
/// new admin straight in. Reuses the standard <see cref="LoginResult"/> token shape.
/// </summary>
public sealed record OnboardingResult(
    Guid CommunityId,
    string CommunityName,
    LoginResult Login);
