using UrbanNest.Domain.Common;

namespace UrbanNest.Domain.Parcel;

/// <summary>
/// A secure storage bin/location where a <see cref="Parcel"/> is held while in custody (Req 29.1,
/// glossary "Custody_Location"). A community-scoped master row — community infrastructure an operator
/// manages, like <c>ParkingSlot</c>/<c>StorageLocation</c> — identified by a human-readable
/// <see cref="Code"/> unique within the community.
///
/// <para><b>One active custody location → one parcel (Req 29.4).</b> This entity carries no "current
/// parcel" column; the active assignment lives on the <see cref="Parcel.CustodyLocationId"/> side,
/// which is set only while the parcel is in custody and cleared when it leaves. A filtered unique
/// index on <c>Parcel(CustodyLocationId) WHERE CustodyLocationId IS NOT NULL</c> (added in
/// <c>AppDbContext.OnModelCreating</c> on SQL Server) makes "a given custody location holds at most
/// one parcel at a time" a hard DB guarantee — two concurrent assignments to the same location race
/// to that index and exactly one wins. See the Parcel-side remarks.</para>
///
/// <para>Not <see cref="IUnitScoped"/>: a custody location is community infrastructure, not resident
/// self-view data pinned to one unit (same stance as <c>ParkingSlot</c>/<c>StorageLocation</c> and
/// the ownership/tenancy registers). Derives from <see cref="TenantOwnedEntity"/>: tenant +
/// soft-delete global filter, rowversion concurrency token, audit columns.</para>
/// </summary>
public class CustodyLocation : TenantOwnedEntity
{
    /// <summary>
    /// Human-readable location code/label (e.g. "Bin A-12", "Reception Shelf 3"). Required; unique
    /// within the community (Req 29.1).
    /// </summary>
    public string Code { get; set; } = string.Empty;
}
