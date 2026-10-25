using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Maintenance;

/// <summary>
/// A single part/material consumed against a <see cref="WorkOrder"/> (Req 36.4): maintenance staff
/// record the part name, quantity used and unit cost; the service computes the line total. A
/// tenant-owned child of a work order carrying the SAME
/// <see cref="TenantOwnedEntity.CommunityId"/> as its parent (so the tenant global filter bounds it
/// to the caller's communities with no bespoke predicate), NOT <see cref="IUnitScoped"/> —
/// maintenance cost data is community infrastructure, not resident self-view.
///
/// <para><b>Costs as decimal (steering: money/precision).</b> <see cref="UnitCost"/> and
/// <see cref="LineTotal"/> are decimals mapped with explicit precision in EF config, never floats.
/// Quantity and unit cost are validated non-negative by the service; the line total is
/// <see cref="Quantity"/> × <see cref="UnitCost"/> computed server-side, never trusted from the
/// client.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns.</para>
/// </summary>
public class WorkOrderPart : TenantOwnedEntity
{
    /// <summary>The work order this part is recorded against (Req 36.4). Required.</summary>
    public Guid WorkOrderId { get; set; }

    /// <summary>Human-readable part/material name (e.g. "Lift cable 8mm"). Required (Req 36.4).</summary>
    public string PartName { get; set; } = string.Empty;

    /// <summary>Optional part/SKU reference number, or null.</summary>
    public string? PartNumber { get; set; }

    /// <summary>Quantity of the part used (Req 36.4). Non-negative; decimal so fractional units (e.g. 2.5 m) are allowed.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Cost per unit of the part (Req 36.4). Non-negative decimal, stored with money precision.</summary>
    public decimal UnitCost { get; set; }

    /// <summary>
    /// The line total for this part (Req 36.4): <see cref="Quantity"/> × <see cref="UnitCost"/>,
    /// computed server-side so the stored total always matches its inputs.
    /// </summary>
    public decimal LineTotal { get; set; }
}
