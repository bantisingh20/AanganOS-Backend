namespace App.Domain.Files;

/// <summary>
/// Standard attachment / owning resource types managed by the common StoredFiles table.
/// Used to categorize documents, photos, and evidence uploads across all modules.
/// </summary>
public static class AttachmentTypes
{
    /// <summary>Resident profile photo / portrait.</summary>
    public const string Resident = "Resident";

    /// <summary>Resident KYC selfie photo.</summary>
    public const string ResidentSelfie = "ResidentSelfie";

    /// <summary>Resident government ID proof (front face).</summary>
    public const string ResidentGovtIdFront = "ResidentGovtIdFront";

    /// <summary>Resident government ID proof (back face).</summary>
    public const string ResidentGovtIdBack = "ResidentGovtIdBack";

    /// <summary>Gate visitor / walk-in captured photo.</summary>
    public const string Visitor = "Visitor";

    /// <summary>Pre-approved visitor pass badge / photo.</summary>
    public const string VisitPass = "VisitPass";

    /// <summary>Staff / frequent helper recurring visitor profile photo.</summary>
    public const string RecurringVisitor = "RecurringVisitor";

    /// <summary>Helpdesk complaint or ticket evidence photo / document.</summary>
    public const string Ticket = "Ticket";

    /// <summary>Delivered parcel arrival photo.</summary>
    public const string Parcel = "Parcel";

    /// <summary>Parcel damage / condition evidence photo.</summary>
    public const string ParcelEvidence = "ParcelEvidence";

    /// <summary>Society announcement / circular attachment.</summary>
    public const string Announcement = "Announcement";

    /// <summary>Billing / expense receipt or invoice proof.</summary>
    public const string MaintenanceExpense = "MaintenanceExpense";

    /// <summary>General community rule book, agreement, or shared document.</summary>
    public const string Document = "Document";
}
