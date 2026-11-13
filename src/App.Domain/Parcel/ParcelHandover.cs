using App.Domain.Common;

namespace App.Domain.Parcel;

/// <summary>
/// The immutable record of a completed, verified parcel handover (Req 30.4). It is written only when
/// a guard's collector verification SUCCEEDS; the parcel is transitioned to <c>handed_over</c> in the
/// same transaction. A FAILED verification writes no handover row and leaves the parcel's custody
/// status unchanged (Req 30.3), so the mere existence of this row is proof the parcel reached
/// <c>handed_over</c> through a successful verification. Carries its owning
/// <see cref="TenantOwnedEntity.CommunityId"/> and the <see cref="UnitId"/> the parcel was addressed to.
///
/// <para><b>Resident self-view (Req 5.4).</b> Implements <see cref="IUnitScoped"/> via its non-null
/// <see cref="UnitId"/> (the parcel's recipient unit), so the DbContext resident self-scope filter
/// restricts a resident principal to handovers for their own unit's parcels. No bespoke predicate.</para>
///
/// <para><b>Recorded on success (Req 30.4).</b> It captures the collector name + relationship, the
/// verification method actually used, the captured handover <see cref="ProofFileId"/> (a File_Service
/// reference, the opaque-reference pattern — not inline bytes), the handling <see cref="GuardUserId"/>
/// and the <see cref="HandedOverAtUtc"/> timestamp. One handover per parcel: a unique index on
/// <c>ParcelId</c> makes "a parcel is handed over at most once" a DB guarantee alongside the
/// idempotency-key at-most-once wrapper on the operation (Req 30.4, 30.5).</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns.</para>
/// </summary>
public class ParcelHandover : TenantOwnedEntity, IUnitScoped
{
    /// <summary>
    /// The recipient unit the handed-over parcel was addressed to (Req 30.4). Non-null in practice;
    /// typed nullable to satisfy <see cref="IUnitScoped"/>, which the resident self-scope filter keys on.
    /// </summary>
    public Guid? UnitId { get; set; }

    /// <summary>The parcel that was handed over (Req 30.4). Required; unique — one handover per parcel.</summary>
    public Guid ParcelId { get; set; }

    /// <summary>The authorization the handover was verified against (Req 30.1, 30.4), when one was used.</summary>
    public Guid? ParcelAuthorizationId { get; set; }

    /// <summary>The collector's name as recorded at handover (Req 30.4). Required.</summary>
    public string CollectorName { get; set; } = string.Empty;

    /// <summary>The collector's relationship to the resident as recorded at handover (Req 30.4). Optional.</summary>
    public string? CollectorRelationship { get; set; }

    /// <summary>
    /// The verification method actually used to clear the collector (otp / qr / id / combination, a
    /// <c>Parcel_Verification_Method</c> code, Req 30.4). Stored as the stable code, never a frozen enum.
    /// </summary>
    public string VerificationMethod { get; set; } = string.Empty;

    /// <summary>
    /// File_Service reference to the captured handover proof (e.g. a photo / signed slip, Req 30.4).
    /// The opaque reference pattern — the file bytes live in the blob store, not inline here. Optional.
    /// </summary>
    public Guid? ProofFileId { get; set; }

    /// <summary>
    /// The handling guard — the acting user who performed the verified handover (Req 30.4). Taken from
    /// the request tenant context so it cannot be spoofed from a DTO.
    /// </summary>
    public string? GuardUserId { get; set; }

    /// <summary>When the handover completed (Req 30.4). Set from the service clock on success.</summary>
    public DateTime HandedOverAtUtc { get; set; }
}
