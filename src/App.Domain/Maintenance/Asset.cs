using App.Domain.Common;

namespace App.Domain.Maintenance;

/// <summary>
/// A community asset tracked in the maintenance register (Req 35.1): a piece of equipment,
/// fixture or installation (a lift, pump, DG set, fire panel, ...) a Facility Manager registers
/// with its serial number, physical location, warranty details and lifecycle state. Carries its
/// owning <see cref="TenantOwnedEntity.CommunityId"/>.
///
/// <para><b>Community infrastructure, NOT resident self-view data.</b> An asset belongs to the
/// community / a common area, so it is a <see cref="TenantOwnedEntity"/> that is deliberately NOT
/// <see cref="IUnitScoped"/> — the same stance as <c>CustodyLocation</c> / <c>ParkingSlot</c> /
/// <c>StorageLocation</c>. An asset MAY optionally be tied to a unit (e.g. a unit-specific water
/// meter) via the nullable <see cref="UnitId"/>, but that does not force resident self-scoping:
/// the register is a Facility-Manager view bounded by the community (the tenant global filter),
/// not pinned to one resident's unit.</para>
///
/// <para><b>Configurable lifecycle + classification (steering: no hardcoding, Req 35.1).</b>
/// <see cref="Status"/> resolves against the editable <c>Asset_Status</c> master-data list
/// (active / under_maintenance / retired / disposed, ...) and starts <see cref="AssetStatuses.Active"/>
/// on registration. <see cref="AssetType"/> resolves against the editable <c>Asset_Type</c> list and
/// <see cref="Category"/> against <c>Asset_Category</c>. All stored as stable codes an admin can add
/// to / retire without a code change, never frozen enums. 21.1 keeps lifecycle as a simple VALIDATED
/// status (any active <c>Asset_Status</c> code is accepted on edit) — a transition allow-list like
/// <c>Parcel_Status_Transition</c> / <c>Ticket_Status_Transition</c> is intentionally NOT introduced
/// here because the requirement needs only a tracked lifecycle state, not transition validation; the
/// seam is left clean for a later task to add one if a workflow demands it.</para>
///
/// <para><b>Scannable QR identity (Req 35.2) — same pattern as <c>VisitPass</c> /
/// <c>ParcelAuthorization</c>.</b> <see cref="QrToken"/> is an opaque, high-entropy, community-unique
/// code that identifies the asset when scanned; it reveals nothing about the asset. The DB
/// <c>Unique(CommunityId, QrToken)</c> index is the source of truth — the service generates a token
/// and, on the (vanishingly unlikely) collision, regenerates and retries rather than a read-then-write
/// pre-check (the same approach <c>GateService</c>/<c>ParcelService</c> use).</para>
///
/// <para><b>Serial-number uniqueness (Req 35.1).</b> <see cref="SerialNumber"/> is unique within the
/// community (<c>Unique(CommunityId, SerialNumber)</c>), enforced DB-side via <c>UniqueConstraintGuard</c>
/// — a duplicate maps to CONFLICT, never a read-then-write gap.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter (so a
/// soft-deleted/archived asset stays in the table and remains traceable but is hidden from ordinary
/// reads, Req 35.4), rowversion concurrency token (Req 22.4), audit columns.</para>
/// </summary>
public class Asset : TenantOwnedEntity
{
    /// <summary>Human-readable asset name/label (e.g. "Lift A - Tower 1", "Fire Pump 2"). Required (Req 35.1).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The asset's serial / tag number (Req 35.1). Required and unique within the community
    /// (<c>Unique(CommunityId, SerialNumber)</c>), the DB-enforced identity of a physical asset.
    /// </summary>
    public string SerialNumber { get; set; } = string.Empty;

    /// <summary>The asset's physical location within the community (e.g. "Basement 1 Pump Room"). Required (Req 35.1).</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Configurable asset-type code resolved against the <c>Asset_Type</c> master-data list
    /// (Req 35.1). Stored as the stable code, never a frozen enum.
    /// </summary>
    public string AssetType { get; set; } = string.Empty;

    /// <summary>
    /// Configurable asset-category code resolved against the <c>Asset_Category</c> master-data list
    /// (Req 35.1). Optional (not every asset needs a category). Stored as the stable code, never a
    /// frozen enum.
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// Configurable lifecycle status resolved against the <c>Asset_Status</c> master-data list
    /// (Req 35.1). Starts <see cref="AssetStatuses.Active"/> on registration. Stored as the stable
    /// code, never a frozen enum.
    /// </summary>
    public string Status { get; set; } = AssetStatuses.Active;

    /// <summary>
    /// Opaque, community-unique QR code that identifies the asset when scanned (Req 35.2).
    /// High-entropy random string, not derived from asset data — it leaks nothing and is the indexed
    /// lookup key. Unique within the community (<c>Unique(CommunityId, QrToken)</c>).
    /// </summary>
    public string QrToken { get; set; } = string.Empty;

    /// <summary>
    /// Optional unit the asset is tied to (Req 35.1), e.g. a unit-specific meter, or null for a
    /// common-area / community-wide asset. Nullable so an asset can be community infrastructure; this
    /// does NOT make the asset resident-self-scoped (see the type remarks).
    /// </summary>
    public Guid? UnitId { get; set; }

    /// <summary>The warranty provider / vendor (Req 35.1). Optional free text.</summary>
    public string? WarrantyProvider { get; set; }

    /// <summary>The warranty reference / policy number (Req 35.1). Optional free text.</summary>
    public string? WarrantyReference { get; set; }

    /// <summary>When the asset's warranty began (Req 35.1), or null if not recorded.</summary>
    public DateTime? WarrantyStartUtc { get; set; }

    /// <summary>When the asset's warranty expires (Req 35.1), or null if not recorded.</summary>
    public DateTime? WarrantyEndUtc { get; set; }

    /// <summary>When the asset was installed / commissioned (Req 35.1), or null if not recorded.</summary>
    public DateTime? InstalledOnUtc { get; set; }

    /// <summary>Optional free-text notes about the asset (Req 35.1).</summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Well-known default <c>Asset_Status</c> codes (Req 35.1). Seeded master-data codes kept as
/// constants so the service sets/compares status without a magic string. <b>Data</b>, not a
/// behavioural enum — the status list is editable master data; this is only a convenience for the
/// known codes.
/// </summary>
public static class AssetStatuses
{
    /// <summary>Initial status on registration (Req 35.1): the asset is in active service.</summary>
    public const string Active = "active";

    /// <summary>The asset is temporarily out of service for maintenance (Req 35.1).</summary>
    public const string UnderMaintenance = "under_maintenance";

    /// <summary>The asset is inactive / standby but still tracked (Req 35.1).</summary>
    public const string Inactive = "inactive";

    /// <summary>The asset has reached end-of-life and been retired from service (Req 35.1).</summary>
    public const string Retired = "retired";

    /// <summary>The asset has been disposed of (Req 35.1).</summary>
    public const string Disposed = "disposed";
}
