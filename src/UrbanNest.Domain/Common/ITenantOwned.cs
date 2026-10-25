namespace UrbanNest.Domain.Common;

/// <summary>
/// Marks a community-owned record. The <see cref="CommunityId"/> is the multi-tenant
/// discriminator (Req 5.1); global query filters key off it so one community can never
/// read another's rows (Req 5.2, 5.3).
/// </summary>
public interface ITenantOwned
{
    Guid CommunityId { get; set; }
}
