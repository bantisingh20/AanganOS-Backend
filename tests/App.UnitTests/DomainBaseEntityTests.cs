using App.Domain.Tenancy;

namespace App.UnitTests;

/// <summary>
/// Harness sanity + a real assertion on the shared tenant-owned base: a new entity gets a
/// non-empty Id and starts not-deleted. (Property tests for tenant isolation / self-scoping are
/// tasks 1.2 and 1.3.)
/// </summary>
public sealed class DomainBaseEntityTests
{
    [Fact]
    public void New_tenant_owned_entity_has_identity_and_is_not_deleted()
    {
        var record = new SampleTenantRecord { Name = "x" };

        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.False(record.IsDeleted);
        Assert.Null(record.DeletedAtUtc);
    }
}
