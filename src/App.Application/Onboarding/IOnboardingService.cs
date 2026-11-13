namespace App.Application.Onboarding;


public interface IOnboardingService
{
    Task<OnboardingResult> CreateCommunityAsync(
        CreateCommunityOnboardingRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record CreateCommunityOnboardingRequest(
    string OrganizationName,
    string CommunityName,
    string CommunityType,
    string AdminName,
    string? AdminEmail,
    string? AdminPhone,
    string AdminPassword,
    int PlanMonths = 0,
    int Floors = 0,
    int RoomsPerFloor = 0,
    IReadOnlyList<string>? Wings = null);


public sealed record OnboardingResult(
    Guid CommunityId,
    string CommunityName);
