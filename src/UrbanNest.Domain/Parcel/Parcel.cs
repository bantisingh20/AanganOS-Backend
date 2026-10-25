using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Parcel;

/// <summary>
/// A delivery received and held on behalf of a resident/unit (Req 28.1). A guard registers it at the
/// gate/reception with its tracking number, delivery provider, recipient <see cref="RecipientUnitId"/>,
/// configurable <see cref="Category"/>, received <see cref="Condition"/>, received timestamp and
/// optional photo/condition evidence (a File_Service reference, Req 28.4). Carries its owning
/// <see cref="TenantOwnedEntity.CommunityId"/>.
///
/// <para><b>Resident self-view (Req 5.4).</b> Implements <see cref="IUnitScoped"/> via its non-null
/// <see cref="RecipientUnitId"/> surfaced as <c>UnitId</c>, so the DbContext resident self-scope
/// filter restricts a resident principal to parcels addressed to their own unit — no bespoke
/// predicate in the service. The custody-location assignment + handover flows (Tasks 18.2–18.3)
/// extend this entity; this task defines the registration + status-lifecycle surface.</para>
///
/// <para><b>Configurable status, category, condition (steering: no hardcoding).</b>
/// <see cref="Status"/> resolves against the editable <c>Parcel_Status</c> list
/// (expected / received / owner_not_available / in_custody / ready_for_pickup / handed_over /
/// returned / lost_damaged, Req 28.2); <see cref="Category"/> against <c>Parcel_Category</c>
/// (standard / medicine / perishable / urgent / document, Req 28.1); <see cref="Condition"/> against
/// <c>Parcel_Condition</c>. All stored as stable codes, never frozen enums. The allowed status
/// transitions are themselves editable master data (<c>Parcel_Status_Transition</c>), so an illegal
/// transition is rejected without a hardcoded transition table (Req 28.3).</para>
///
/// <para><b>Priority flag (Req 29.3).</b> <see cref="IsPriority"/> is a stored flag set on
/// registration when the parcel's category is one of the configurable priority categories (medicine
/// / perishable / urgent). It drives the priority handling of the parcel-received notification
/// (Task 18.2); kept as a stored flag (not derived from the category) so the category list and the
/// priority-category set stay independently editable and a later re-classification does not silently
/// change the recorded priority.</para>
///
/// <para><b>Evidence (Req 28.4).</b> <see cref="PhotoFileId"/>/<see cref="ConditionEvidenceFileId"/>
/// reference File_Service <c>StoredFile</c> rows (captured at the gate), not inline bytes — the
/// opaque reference pattern. Each stored file carries the parcel's CommunityId so the authorized
/// streaming check stays tenant-safe (Req 28.4, 7.2).</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token (the status-transition-once guard, Req 22.4 / design "RowVersion on Parcel
/// status"), audit columns.</para>
/// </summary>
public class Parcel : TenantOwnedEntity, IUnitScoped
{
    /// <summary>
    /// The recipient unit the parcel is addressed to (Req 28.1). Non-null in practice; typed nullable
    /// to satisfy <see cref="IUnitScoped"/>, which the resident self-scope filter keys on. This IS the
    /// recipient unit — see <see cref="RecipientUnitId"/>.
    /// </summary>
    public Guid? UnitId { get; set; }

    /// <summary>The recipient unit the parcel is addressed to (Req 28.1). Alias over <see cref="UnitId"/>.</summary>
    public Guid RecipientUnitId
    {
        get => UnitId ?? Guid.Empty;
        set => UnitId = value;
    }

    /// <summary>The courier/delivery tracking or reference number (Req 28.1). Required.</summary>
    public string TrackingNumber { get; set; } = string.Empty;

    /// <summary>The delivery provider / courier (Req 28.1). Required.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Configurable parcel-category code resolved against the <c>Parcel_Category</c> master-data list
    /// (Req 28.1). Stored as the stable code, never a frozen enum.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Configurable status code resolved against the <c>Parcel_Status</c> master-data list (Req 28.2).
    /// Starts <c>received</c> on registration. Stored as the stable code, never a frozen enum; the
    /// Task 18.1 state machine drives transitions against the editable <c>Parcel_Status_Transition</c>
    /// list (Req 28.3).
    /// </summary>
    public string Status { get; set; } = ParcelStatuses.Received;

    /// <summary>
    /// Configurable recorded package condition resolved against the <c>Parcel_Condition</c> master-data
    /// list (Req 28.1). Stored as the stable code, never a frozen enum.
    /// </summary>
    public string Condition { get; set; } = string.Empty;

    /// <summary>The person who received/receipted the parcel at the gate/reception (Req 28.1). Optional free text.</summary>
    public string? Receiver { get; set; }

    /// <summary>The gate/reception location the parcel was received at (Req 28.1). Optional free text.</summary>
    public string? ReceivedLocation { get; set; }

    /// <summary>When the parcel was received (Req 28.1). Set on registration to the service clock's now.</summary>
    public DateTime ReceivedAtUtc { get; set; }

    /// <summary>Optional File_Service reference to a captured parcel photo (Req 28.4).</summary>
    public Guid? PhotoFileId { get; set; }

    /// <summary>Optional File_Service reference to captured package-condition evidence (Req 28.4).</summary>
    public Guid? ConditionEvidenceFileId { get; set; }

    /// <summary>
    /// The <see cref="CustodyLocation"/> the parcel is currently held in, or null when the parcel is
    /// not in custody (Req 29.1). Set when the parcel moves to <c>in_custody</c> and cleared when it
    /// leaves custody, so a non-null value marks the <b>active</b> assignment.
    ///
    /// <para><b>One active custody location → one parcel (Req 29.4).</b> A filtered unique index on
    /// <c>(CustodyLocationId) WHERE CustodyLocationId IS NOT NULL</c> (added in
    /// <c>AppDbContext.OnModelCreating</c> on SQL Server) makes "a given custody location holds at
    /// most one parcel at a time" a hard DB guarantee: two concurrent assignments of the same
    /// location race to the index and exactly one wins — no read-then-write pre-check. The race
    /// loser's unique-violation is mapped to CONFLICT via <c>UniqueConstraintGuard</c>.</para>
    /// </summary>
    public Guid? CustodyLocationId { get; set; }

    /// <summary>
    /// Whether the parcel is flagged priority (Req 29.3): set on registration when its category is a
    /// configurable priority category (medicine / perishable / urgent). Drives priority handling of
    /// the parcel-received notification (Task 18.2). Stored, not derived — see the type remarks.
    /// </summary>
    public bool IsPriority { get; set; }

    /// <summary>
    /// When the parcel's custody-SLA breach was escalated, or null while it has not been escalated
    /// (Req 31.2, Task 18.4). The background SLA sweep considers a parcel overdue only while this is
    /// null, stamps it when it escalates, and so escalates a given parcel AT MOST ONCE — a stored
    /// idempotency marker, not a per-pass re-notification. Cleared implicitly by the parcel leaving
    /// custody (a terminal/returned/handed-over status is no longer swept). This is distinct from the
    /// timeline entry (the audit record of the escalation): the stamp is the sweep's dedupe guard.
    /// </summary>
    public DateTime? CustodySlaEscalatedAtUtc { get; set; }
}

/// <summary>
/// Well-known default <c>Parcel_Status</c> codes (Req 28.2). Seeded master-data codes kept as
/// constants so the service sets/compares status without a magic string. <b>Data</b>, not a
/// behavioural enum — the status list AND the allowed transitions are editable master data; this is
/// only a convenience for the known codes.
/// </summary>
public static class ParcelStatuses
{
    /// <summary>Pre-arrival: a delivery is expected but not yet received (Req 28.2).</summary>
    public const string Expected = "expected";

    /// <summary>Initial status on registration (Req 28.1, 28.2): the parcel has been received at the gate.</summary>
    public const string Received = "received";

    /// <summary>The recipient was not available to collect at delivery (Req 28.2).</summary>
    public const string OwnerNotAvailable = "owner_not_available";

    /// <summary>Held in a secure custody location (Req 28.2, 29.1). The Task 18.2 flow assigns the location.</summary>
    public const string InCustody = "in_custody";

    /// <summary>Ready for the resident/authorized collector to pick up (Req 28.2).</summary>
    public const string ReadyForPickup = "ready_for_pickup";

    /// <summary>Handed over to a verified collector (Req 28.2, 30.4). The Task 18.3 handover flow sets this.</summary>
    public const string HandedOver = "handed_over";

    /// <summary>Returned to the sender/courier (Req 28.2).</summary>
    public const string Returned = "returned";

    /// <summary>Recorded as lost or damaged (Req 28.2, 31.4).</summary>
    public const string LostDamaged = "lost_damaged";
}

/// <summary>
/// Well-known default <c>Parcel_Category</c> codes (Req 28.1). Seeded master-data codes kept as
/// constants. <b>Data</b>, not a behavioural enum — the list is editable, and which categories are
/// treated as priority is itself configurable (<c>parcel.priorityCategories</c>, Req 29.3).
/// </summary>
public static class ParcelCategories
{
    /// <summary>An ordinary parcel (Req 28.1).</summary>
    public const string Standard = "standard";

    /// <summary>Medicine — a default priority category (Req 29.3).</summary>
    public const string Medicine = "medicine";

    /// <summary>Perishable goods — a default priority category (Req 29.3).</summary>
    public const string Perishable = "perishable";

    /// <summary>Urgent delivery — a default priority category (Req 29.3).</summary>
    public const string Urgent = "urgent";

    /// <summary>A document delivery (Req 28.1).</summary>
    public const string Document = "document";
}
