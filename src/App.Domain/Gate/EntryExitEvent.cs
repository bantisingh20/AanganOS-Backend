using App.Domain.Common;

namespace App.Domain.Gate;

/// <summary>
/// A timestamped record of a visitor entering and later exiting the community through a gate
/// (Req 25.3). One row is created on entry (<see cref="EntryTsUtc"/> set, <see cref="ExitTsUtc"/>
/// null — the "open" event) and later closed on exit by stamping <see cref="ExitTsUtc"/>. Carries
/// its owning <see cref="TenantOwnedEntity.CommunityId"/>.
///
/// <para><b>Which authorization admitted them (Req 25.3).</b> Exactly one of
/// <see cref="VisitorId"/> (an approved walk-in), <see cref="VisitPassId"/> (a verified pre-approved
/// pass) or <see cref="RecurringVisitorId"/> (an authorized recurring visitor for today) links the
/// event to the authorization the guard used to admit the visitor. The denormalized
/// <see cref="VisitorName"/> / <see cref="Category"/> are captured on the event so the entry/exit log
/// is self-describing without a join (and survives if the source authorization is later edited).</para>
///
/// <para><b>Not <c>IUnitScoped</c>.</b> The gate entry/exit log is a security operations record, not
/// resident self-view data — it is scoped by community (the tenant filter) but not pinned to one
/// unit for a resident principal. <see cref="DestinationUnitId"/> records where the visitor was
/// headed (optional) without turning the log into resident-self-scoped data.</para>
///
/// <para><b>Scalable log (Req 25.4).</b> The entity config declares an index on
/// <c>(CommunityId, EntryTsUtc)</c> so the chronological entry/exit log and the "open events for a
/// visitor" exit-matching read are single indexed queries (no scan, no N+1).</para>
///
/// <para><b>Concurrency-safe exit (Req 25.3).</b> Closing the open event stamps
/// <see cref="ExitTsUtc"/> on the TRACKED row so the inherited <c>RowVersion</c> rides the UPDATE:
/// two concurrent exit records race on the row-version check and exactly one wins, so a double-exit
/// can't both stamp a different time.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns.</para>
/// </summary>
public class EntryExitEvent : TenantOwnedEntity
{
    /// <summary>
    /// Optional gate the event occurred at (Req 25.3). The Gate master entity is Task 16.3, so this
    /// is nullable here; <see cref="GateLabel"/> carries a free-text gate identifier in the meantime.
    /// </summary>
    public Guid? GateId { get; set; }

    /// <summary>Optional free-text gate label/identifier captured at the gate (until the Gate master lands in 16.3).</summary>
    public string? GateLabel { get; set; }

    /// <summary>Optional destination unit the visitor was headed to (Req 25.3).</summary>
    public Guid? DestinationUnitId { get; set; }

    /// <summary>The approved walk-in <see cref="Visitor"/> that admitted this entry (Req 25.3), or null.</summary>
    public Guid? VisitorId { get; set; }

    /// <summary>The verified <see cref="VisitPass"/> that admitted this entry (Req 25.3), or null.</summary>
    public Guid? VisitPassId { get; set; }

    /// <summary>The authorized <see cref="RecurringVisitor"/> that admitted this entry (Req 25.3), or null.</summary>
    public Guid? RecurringVisitorId { get; set; }

    /// <summary>Denormalized visitor name captured on the event (Req 25.3). Required.</summary>
    public string VisitorName { get; set; } = string.Empty;

    /// <summary>
    /// Denormalized configurable visitor-category code captured on the event (Req 25.1, 25.3),
    /// resolved against the <c>Visitor_Category</c> list. Stored as the stable code, never an enum.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Optional captured vehicle number (Req 25.3 — "the vehicle number when captured").</summary>
    public string? VehicleNumber { get; set; }

    /// <summary>Entry timestamp (Req 25.3). Set when the event is created.</summary>
    public DateTime EntryTsUtc { get; set; }

    /// <summary>
    /// Exit timestamp (Req 25.3). Null while the visitor is on-site (the "open" event); stamped when
    /// the guard records the exit against this open event.
    /// </summary>
    public DateTime? ExitTsUtc { get; set; }

    /// <summary>Whether the visitor is still on-site (no exit recorded yet). Computed, not stored.</summary>
    public bool IsOpen => ExitTsUtc is null;
}
