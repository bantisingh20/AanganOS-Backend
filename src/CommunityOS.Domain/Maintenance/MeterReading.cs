using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Maintenance;

/// <summary>
/// A recorded reading of an equipment meter / counter on an <see cref="Asset"/> (Req 37.4): a
/// Facility Manager / technician records the counter value (e.g. a DG set's running hours, a pump's
/// energy counter) at a point in time. Recording a reading appends a single append-only
/// <see cref="EquipmentHistoryEntry"/> to the asset's history
/// (<see cref="EquipmentHistoryEventTypes.MeterReadingRecorded"/>) so meter readings flow into the
/// same chronological equipment history as work orders and inspections (Req 37.5).
///
/// <para><b>Equipment counter — non-decreasing (Req 37.4).</b> A physical equipment counter never
/// runs backward, so a reading must be &gt;= the latest recorded reading for the same asset + meter;
/// a lower value is a data-entry error rejected with a VALIDATION_ERROR (the service enforces it).
/// This is the maintenance equipment-meter history — distinct from the Phase 11 utility-billing meter
/// module.</para>
///
/// <para><b>Community infrastructure, NOT resident self-view data.</b> Like <see cref="Asset"/>, a
/// meter reading belongs to the community, so it is a <see cref="TenantOwnedEntity"/> deliberately
/// NOT <see cref="IUnitScoped"/> — a Facility-Manager record bounded by the community (the tenant
/// global filter).</para>
///
/// <para><b>Configurable meter type / unit (steering: no hardcoding).</b> <see cref="MeterType"/>
/// resolves against the editable <c>Meter_Type</c> master-data list (running_hours / energy_kwh /
/// flow / ...) and <see cref="Unit"/> against <c>Meter_Unit</c> (hours / kwh / litres / ...), stored
/// as stable codes never frozen enums. The reading is keyed by (asset, meter type) so one asset can
/// carry several independent counters.</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token (Req 22.4), audit columns.</para>
/// </summary>
public class MeterReading : TenantOwnedEntity
{
    /// <summary>The asset whose meter this reading is for (Req 37.4). Required.</summary>
    public Guid AssetId { get; set; }

    /// <summary>
    /// Configurable meter-type code resolved against the <c>Meter_Type</c> master-data list
    /// (Req 37.4). Identifies WHICH counter on the asset this reading is for, so one asset can have
    /// several independent monotonic counters. Stored as the stable code, never a frozen enum.
    /// </summary>
    public string MeterType { get; set; } = string.Empty;

    /// <summary>
    /// Configurable measurement-unit code resolved against the <c>Meter_Unit</c> master-data list
    /// (Req 37.4). Stored as the stable code, never a frozen enum.
    /// </summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// The counter value read (Req 37.4). Must be &gt;= the latest recorded value for the same asset +
    /// meter type (equipment counters are non-decreasing — the service rejects a lower value).
    /// </summary>
    public decimal Value { get; set; }

    /// <summary>When the reading was taken, in UTC, from the injected clock (Req 37.4).</summary>
    public DateTime ReadingAtUtc { get; set; }

    /// <summary>The user who recorded the reading (Req 37.4), from the request tenant context, or null for a system actor.</summary>
    public string? RecordedByUserId { get; set; }

    /// <summary>Optional free-text note about the reading (Req 37.4).</summary>
    public string? Notes { get; set; }
}

/// <summary>
/// Well-known default <c>Meter_Type</c> codes (Req 37.4). Seeded master-data codes kept as constants.
/// <b>Data</b>, not a behavioural enum — the list is editable master data.
/// </summary>
public static class MeterTypes
{
    /// <summary>Equipment running hours (e.g. a DG set / pump).</summary>
    public const string RunningHours = "running_hours";

    /// <summary>Cumulative energy consumption in kWh.</summary>
    public const string EnergyKwh = "energy_kwh";

    /// <summary>Cumulative flow (e.g. litres pumped).</summary>
    public const string Flow = "flow";

    /// <summary>A generic cycle / operation counter.</summary>
    public const string Cycles = "cycles";
}
