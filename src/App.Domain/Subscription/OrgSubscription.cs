using App.Domain.Common;

namespace App.Domain.Subscription;

public class OrgSubscription : PlatformEntity
{
    public Guid OrganizationId { get; set; }

    public int PlanMonths { get; set; }

    public DateTime StartsAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public string Status { get; set; } = OrgSubscriptionStatuses.Active;
}

public static class OrgSubscriptionStatuses
{
    public const string Active = "active";
    public const string Expired = "expired";
}

public static class OrgSubscriptionPlans
{
    public static readonly int[] AllowedMonths = [1, 3, 6, 9, 12];

    public static bool IsValid(int months) => Array.IndexOf(AllowedMonths, months) >= 0;
}
