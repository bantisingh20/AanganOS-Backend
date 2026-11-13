using App.Domain.Common;

namespace App.Domain.Subscription;

public class SubscriptionRenewalRequest : PlatformEntity
{
    public Guid OrganizationId { get; set; }

    public int RequestedPlanMonths { get; set; }

    public string Status { get; set; } = SubscriptionRenewalStatuses.Pending;

    public Guid RequestedBy { get; set; }

    public DateTime RequestedAtUtc { get; set; }

    public Guid? ResolvedBy { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }

    public DateTime? ResultExpiresAtUtc { get; set; }
}

public static class SubscriptionRenewalStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
}
