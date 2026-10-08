using CommunityOS.Domain.Common;

namespace CommunityOS.Domain.Parcel;

/// <summary>
/// A resident's authorization naming an <b>authorized collector</b> permitted to pick up a specific
/// <see cref="Parcel"/> addressed to the resident's OWN unit (Req 30.1). A resident creates it so a
/// nominated person (a family member, domestic staff, a courier agent) can collect the parcel on the
/// resident's behalf, and the guard verifies that person against this record at handover (Req 30.2).
/// Carries its owning <see cref="TenantOwnedEntity.CommunityId"/> and the <see cref="UnitId"/> the
/// authorized parcel is addressed to.
///
/// <para><b>Resident self-view (Req 5.4, 30.1).</b> Implements <see cref="IUnitScoped"/> via its
/// non-null <see cref="UnitId"/> (the authorized parcel's recipient unit), so the DbContext resident
/// self-scope filter restricts a resident principal to authorizations for their own unit — the ones
/// they created. No bespoke predicate in the service.</para>
///
/// <para><b>Opaque QR + hashed OTP (steering: security first) — reused verbatim from
/// <c>VisitPass</c>.</b> <see cref="QrToken"/> is an opaque, high-entropy, community-unique code the
/// guard scans; it reveals nothing about the authorization. <see cref="OtpHash"/> stores only a
/// salted hash of the short OTP shared with the collector (the plaintext is returned once on create
/// and never persisted or logged, same convention as the visit-pass OTP). Verification compares in
/// constant time. Either/both may be required depending on the configured verification method.</para>
///
/// <para><b>Configurable verification method (steering: no hardcoding).</b>
/// <see cref="VerificationMethod"/> is a stable code resolved against the editable
/// <c>Parcel_Verification_Method</c> master-data list (otp / qr / id / combination, Req 30.2) — not a
/// frozen enum. It records how the collector must be verified at handover: the handover enforces the
/// method named here. The <see cref="ParcelVerificationMethods"/> constants name the seeded codes so
/// the service avoids scattered literals.</para>
///
/// <para><b>Expected collector + ID (Req 30.4).</b> <see cref="CollectorName"/> and
/// <see cref="CollectorRelationship"/> are the resident's declaration of who may collect;
/// <see cref="ExpectedIdReference"/> is the expected government/photo-ID reference the <c>id</c> /
/// <c>combination</c> methods check the presented reference against. These seed the handover record's
/// recorded collector details (Req 30.4).</para>
///
/// <para>Derives from <see cref="TenantOwnedEntity"/>: tenant + soft-delete global filter, rowversion
/// concurrency token, audit columns.</para>
/// </summary>
public class ParcelAuthorization : TenantOwnedEntity, IUnitScoped
{
    /// <summary>
    /// The recipient unit the authorized parcel is addressed to (Req 30.1). Non-null in practice;
    /// typed nullable to satisfy <see cref="IUnitScoped"/>, which the resident self-scope filter keys
    /// on. Mirrors the authorized parcel's <see cref="Parcel.RecipientUnitId"/>.
    /// </summary>
    public Guid? UnitId { get; set; }

    /// <summary>The parcel this authorization permits a collector to pick up (Req 30.1). Required.</summary>
    public Guid ParcelId { get; set; }

    /// <summary>Optional authorizing resident (Req 30.1): the resident who created the authorization for their own unit.</summary>
    public Guid? AuthorizingResidentId { get; set; }

    /// <summary>The authorized collector's name (Req 30.1, 30.4). Required.</summary>
    public string CollectorName { get; set; } = string.Empty;

    /// <summary>
    /// The authorized collector's relationship to the resident (e.g. family member, domestic staff,
    /// courier agent, Req 30.4). Optional free text, recorded on the handover.
    /// </summary>
    public string? CollectorRelationship { get; set; }

    /// <summary>
    /// Expected government/photo-ID reference the <c>id</c> / <c>combination</c> verification methods
    /// check the presented reference against (Req 30.2, 30.4). Optional; required by the service when
    /// the chosen method includes ID verification.
    /// </summary>
    public string? ExpectedIdReference { get; set; }

    /// <summary>
    /// Opaque, community-unique QR code the guard scans at handover (Req 30.2). High-entropy random
    /// string, not derived from authorization data — it leaks nothing and is the indexed lookup key.
    /// </summary>
    public string QrToken { get; set; } = string.Empty;

    /// <summary>
    /// Salted hash of the short OTP shared with the collector (Req 30.2). Only the hash is stored;
    /// the plaintext OTP is returned once on create and never persisted or logged (steering: secrets).
    /// </summary>
    public string OtpHash { get; set; } = string.Empty;

    /// <summary>
    /// Configurable verification-method code resolved against the <c>Parcel_Verification_Method</c>
    /// master-data list (otp / qr / id / combination, Req 30.2). Stored as the stable code, never a
    /// frozen enum. The handover enforces the method named here.
    /// </summary>
    public string VerificationMethod { get; set; } = ParcelVerificationMethods.Otp;

    /// <summary>End of the authorization's validity window (Req 30.1). The authorization is not usable after this time.</summary>
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>
    /// Whether this authorization has already been consumed by a completed handover (Req 30.4). Set
    /// true when the handover completes, concurrency-safe under the inherited <c>RowVersion</c>, so a
    /// single authorization cannot drive two handovers.
    /// </summary>
    public bool IsUsed { get; set; }
}

/// <summary>
/// Well-known default <c>Parcel_Verification_Method</c> codes (Req 30.2). Seeded master-data codes
/// kept as constants so the service sets/compares the method without a magic string. <b>Data</b>, not
/// a behavioural enum — the method list is editable master data; this is only a convenience for the
/// known codes.
/// </summary>
public static class ParcelVerificationMethods
{
    /// <summary>Verify the collector by the short OTP shared with them (hashed at rest, Req 30.2).</summary>
    public const string Otp = "otp";

    /// <summary>Verify the collector by scanning the opaque QR token (Req 30.2).</summary>
    public const string Qr = "qr";

    /// <summary>Verify the collector by checking a presented government/photo-ID reference (Req 30.2).</summary>
    public const string Id = "id";

    /// <summary>Verify the collector by a configured combination of OTP + QR + ID (all required, Req 30.2).</summary>
    public const string Combination = "combination";
}
