using App.Domain.Common;

namespace App.Domain.Residents;


public class Resident : TenantOwnedEntity
{
    
    public Guid? UserId { get; set; }

    /// <summary>Resident's full name. Required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional contact email.</summary>
    public string? Email { get; set; }

    /// <summary>Optional contact phone.</summary>
    public string? Phone { get; set; }

    
    public string? PhotoFileId { get; set; }

   
    public string ResidentType { get; set; } = string.Empty;

    
    public string VerificationStatus { get; set; } = ResidentVerificationStatuses.Pending;

    /// <summary>
    /// Reason recorded when a resident is rejected (Req 17.3). Null while pending/verified; set by
    /// the Task 12.3 workflow.
    /// </summary>
    public string? RejectionReason { get; set; }

    /// <summary>Optional date of birth captured at self-registration.</summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>Optional gender code (male / female / other) captured at self-registration.</summary>
    public string? Gender { get; set; }

    /// <summary>Optional move-in date captured at self-registration.</summary>
    public DateOnly? MoveInDate { get; set; }

    /// <summary>Optional government-ID type code (e.g. aadhaar / pan / passport) for verification.</summary>
    public string? GovtIdType { get; set; }

    /// <summary>Optional File_Service reference to the government-ID front image.</summary>
    public string? GovtIdFrontFileId { get; set; }

    /// <summary>Optional File_Service reference to the government-ID back image.</summary>
    public string? GovtIdBackFileId { get; set; }

    /// <summary>Household associations of this resident with units (Req 15.2).</summary>
    public ICollection<HouseholdMember> HouseholdMembers { get; set; } = new List<HouseholdMember>();

    /// <summary>Emergency contacts for this resident (Req 15.3). One-to-many.</summary>
    public ICollection<EmergencyContact> EmergencyContacts { get; set; } = new List<EmergencyContact>();
}


public static class ResidentVerificationStatuses
{
    /// <summary>Admin-created account awaiting the resident's own KYC submission (draft → pending).</summary>
    public const string Draft = "draft";

    /// <summary>Initial status on create (Req 15.1).</summary>
    public const string Pending = "pending";

    /// <summary>Set by the Task 12.3 approval workflow when a resident is verified.</summary>
    public const string Verified = "verified";

    /// <summary>Set by the Task 12.3 workflow when a resident is rejected, alongside a reason.</summary>
    public const string Rejected = "rejected";
}
