using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Community;

/// <summary>
/// Top-level tenant owner (Req 12.1). An Organization groups the Communities a single operator runs
/// — a management company may own one Community or many. It is <b>platform-scoped</b>
/// (<see cref="PlatformEntity"/>): an Organization is not owned by any one community, so it carries
/// no CommunityId and is governed only by the soft-delete global filter, not the tenant predicate.
/// Creating an Organization/Community is a platform super-admin action; the owned
/// <see cref="Community"/> rows carry the <c>OrganizationId</c> back-reference.
/// </summary>
public class Organization : PlatformEntity
{
    /// <summary>Human-readable organization name. Required.</summary>
    public string Name { get; set; } = string.Empty;
}
