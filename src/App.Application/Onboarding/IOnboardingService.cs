namespace App.Application.Onboarding;

/// <summary>
/// Public self-service onboarding: an UNAUTHENTICATED caller registers their society. In one atomic
/// flow it provisions an organization + community, a user credential, and a CommunityAdmin role
/// assignment SCOPED to the new community (CommunityId = the new community). The admin's login is
/// created SUSPENDED and is NOT logged in — a platform PowerUser must approve/activate the account
/// before it can sign in (sales-qualified gate). The community-scoped role means that once activated,
/// the admin only ever sees/manages THAT community's data (never a platform super-admin).
/// </summary>
public interface IOnboardingService
{
    Task<OnboardingResult> CreateCommunityAsync(
        CreateCommunityOnboardingRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Inputs for self-service society registration. The admin will sign in (once activated) with
/// <paramref name="AdminEmail"/>/<paramref name="AdminPhone"/> + <paramref name="AdminPassword"/>.
/// </summary>
public sealed record CreateCommunityOnboardingRequest(
    string OrganizationName,
    string CommunityName,
    string CommunityType,
    string AdminName,
    string? AdminEmail,
    string? AdminPhone,
    string AdminPassword);

/// <summary>
/// The outcome of a registration: the created community's identity. The account is created PENDING
/// (the admin user is suspended) — there is NO login session; the caller shows a "we'll be in touch"
/// message and the admin can sign in only after a PowerUser activates them.
/// </summary>
public sealed record OnboardingResult(
    Guid CommunityId,
    string CommunityName);
