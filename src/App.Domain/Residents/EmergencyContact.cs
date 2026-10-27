using App.Domain.Common;

namespace App.Domain.Residents;

/// <summary>
/// An emergency contact for a <see cref="Resident"/> (Req 15.3). A resident may have one or more,
/// so this is a one-to-many child keyed by <see cref="ResidentId"/>.
///
/// <para>Derives from <see cref="TenantOwnedEntity"/> so it carries the owning
/// <see cref="TenantOwnedEntity.CommunityId"/> (same community as its resident, set by the service)
/// and inherits the tenant + soft-delete global filter, rowversion concurrency token and audit
/// columns. It is intentionally NOT <see cref="IUnitScoped"/>: a resident reaches their own contacts
/// through their <see cref="Resident"/>/<see cref="HouseholdMember"/> rows, which carry the
/// unit-scope, rather than this child pinning itself to a unit.</para>
/// </summary>
public class EmergencyContact : TenantOwnedEntity
{
    /// <summary>The resident this contact belongs to (Req 15.3). Required.</summary>
    public Guid ResidentId { get; set; }

    /// <summary>Navigation to the owning resident.</summary>
    public Resident? Resident { get; set; }

    /// <summary>Contact's name. Required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Contact's phone number. Required.</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>Relationship of the contact to the resident (e.g. "spouse", "parent"). Required.</summary>
    public string Relationship { get; set; } = string.Empty;
}
