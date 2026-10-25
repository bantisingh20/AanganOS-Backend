using UrbanNest.Domain.Tenancy;
using FsCheck.Xunit;

namespace UrbanNest.PropertyTests;

/// <summary>
/// Confirms the FsCheck + xUnit property-based harness runs under `dotnet test`. The real
/// correctness properties (Property 1 multi-tenant read isolation, Property 2 resident
/// self-scoping) are tasks 1.2 and 1.3 — not implemented here.
/// </summary>
public sealed class HarnessSmokeProperty
{
    [Property]
    public bool Any_CommunityId_round_trips_onto_a_tenant_owned_entity(Guid communityId)
    {
        var record = new SampleTenantRecord { CommunityId = communityId };
        return record.CommunityId == communityId;
    }
}
