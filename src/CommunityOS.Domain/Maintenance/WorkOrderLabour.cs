using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Maintenance;

/// <summary>
/// A single labour entry recorded against a <see cref="WorkOrder"/> (Req 36.4): maintenance staff
/// record who did the work, the hours spent and the hourly rate; the service computes the labour
/// cost. A tenant-owned child of a work order carrying the SAME
/// <see cref="TenantOwnedEntity.CommunityId"/> as its parent, NOT <see cref="IUnitScoped"/> —
/// maintenance cost data is community infrastructure, not resident self-view.
///
/// <para><b>Costs as decimal (steering: money/precision).</b> <see cref="HourlyRate"/> and
/// <see cref="LabourCost"/> are decimals mapped with explicit precision in EF config, never floats.
/// Hours and hourly rate are validated non-negative by the service; the labour cost is
/// <see cref="Hours"/> × <see cref="HourlyRate"/> computed server-side, never trusted from the
/// client.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns.</para>
/// </summary>
public class WorkOrderLabour : TenantOwnedEntity
{
    /// <summary>The work order this labour entry is recorded against (Req 36.4). Required.</summary>
    public Guid WorkOrderId { get; set; }

    /// <summary>
    /// The <c>User</c> who performed the labour (Req 36.4), or null when recorded against an external
    /// worker named only by <see cref="WorkerName"/>.
    /// </summary>
    public Guid? WorkerUserId { get; set; }

    /// <summary>Human-readable worker/technician name or role description (Req 36.4). Required.</summary>
    public string WorkerName { get; set; } = string.Empty;

    /// <summary>Hours of labour spent (Req 36.4). Non-negative decimal (e.g. 1.5 h).</summary>
    public decimal Hours { get; set; }

    /// <summary>The hourly labour rate (Req 36.4). Non-negative decimal, stored with money precision.</summary>
    public decimal HourlyRate { get; set; }

    /// <summary>
    /// The computed labour cost (Req 36.4): <see cref="Hours"/> × <see cref="HourlyRate"/>, computed
    /// server-side so the stored cost always matches its inputs.
    /// </summary>
    public decimal LabourCost { get; set; }

    /// <summary>Optional free-text note describing the labour performed, or null.</summary>
    public string? Notes { get; set; }
}
