namespace App.Application.Subscription;

public sealed record SubscriptionDto(
    Guid OrganizationId,
    int PlanMonths,
    DateTime? StartsAtUtc,
    DateTime? ExpiresAtUtc,
    string Status,
    int DaysRemaining,
    bool IsExpired,
    bool IsExpiringSoon,
    bool HasSubscription,
    bool HasPendingRenewal,
    int? RequestedPlanMonths);

public sealed record RenewalRequestDto(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    int RequestedPlanMonths,
    string Status,
    DateTime RequestedAtUtc);
