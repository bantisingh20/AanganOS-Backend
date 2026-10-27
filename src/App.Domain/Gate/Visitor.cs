using App.Domain.Common;

namespace App.Domain.Gate;


public class Visitor : TenantOwnedEntity, IUnitScoped
{
    public Guid? UnitId { get; set; }

    /// <summary>The host unit being visited (Req 24.1). Alias over <see cref="UnitId"/> for intent.</summary>
    public Guid HostUnitId
    {
        get => UnitId ?? Guid.Empty;
        set => UnitId = value;
    }

    /// <summary>The visitor's name (Req 24.1). Required.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional visitor phone (Req 24.1).</summary>
    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? QrToken { get; set; }

    public string? OtpHash { get; set; }

    public string Category { get; set; } = string.Empty;

    /// <summary>Optional File_Service reference to the captured gate photo (Req 24.1).</summary>
    public Guid? PhotoFileId { get; set; }

    /// <summary>Optional captured vehicle number (Req 24.1).</summary>
    public string? VehicleNumber { get; set; }

    /// <summary>Optional captured gate location/identifier (Req 24.1).</summary>
    public string? GateLocation { get; set; }

   
    public string? IdProofType { get; set; }

    /// <summary>Optional captured ID-proof number (e.g. last digits of an Aadhaar / license).</summary>
    public string? IdProofNumber { get; set; }

    /// <summary>Optional free-text purpose of the visit (e.g. "deliver parcel", "AC repair").</summary>
    public string? Purpose { get; set; }

    /// <summary>Optional free-text person the visitor is here to meet (beyond the host unit link).</summary>
    public string? WhomToMeet { get; set; }

    /// <summary>Optional expected visit duration in minutes, captured at the gate for reference.</summary>
    public int? ExpectedDurationMinutes { get; set; }


    public string Status { get; set; } = VisitorStatuses.Pending;

    /// <summary>The resident who approved/rejected the walk-in (Req 24.3). Null while pending.</summary>
    public Guid? ApprovedByResidentId { get; set; }

    /// <summary>When the approval/rejection decision was recorded (Req 24.3). Null while pending.</summary>
    public DateTime? ApprovalDecisionAtUtc { get; set; }


    public bool IsAdmittable =>
        string.Equals(Status, VisitorStatuses.Approved, StringComparison.Ordinal);
}

public static class VisitorStatuses
{
    /// <summary>Initial status on walk-in registration (Req 24.1): entry withheld until approval (Req 24.5).</summary>
    public const string Pending = "pending";

    /// <summary>A resident approved the walk-in (Req 24.3): admittable.</summary>
    public const string Approved = "approved";

    /// <summary>A resident rejected the walk-in (Req 24.4): not admittable; no entry event permitted.</summary>
    public const string Rejected = "rejected";

    /// <summary>The visitor has entered (set by the Task 16.2 entry workflow).</summary>
    public const string Entered = "entered";

    /// <summary>The visitor has exited (set by the Task 16.2 exit workflow).</summary>
    public const string Exited = "exited";
}
