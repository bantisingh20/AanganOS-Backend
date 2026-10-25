using UrbanNest.Application.Notifications;
using UrbanNest.Domain.Config;
using UrbanNest.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace UrbanNest.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="NotificationTemplate"/> and seeds the DEFAULT platform templates so the resident
/// verification/approval notices work out of the box (Req 11.1, 11.2). Seeded rows are plain data
/// an admin can edit/retire or override per community — not compiled strings (steering: no
/// hardcoding). Uniqueness is Unique(Key, Scope, CommunityId), split into filtered platform/
/// community indexes for the nullable <c>CommunityId</c> — the same technique the Configuration /
/// MasterData maps use.
/// </summary>
public sealed class NotificationTemplateEntityConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        builder.ToTable("NotificationTemplates");

        builder.Property(t => t.Key).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Category).IsRequired().HasMaxLength(100);
        builder.Property(t => t.TitleTemplate).IsRequired().HasMaxLength(400);
        builder.Property(t => t.BodyTemplate).IsRequired().HasMaxLength(2000);
        builder.Property(t => t.Scope).HasConversion<int>();
        builder.Property(t => t.CategoryKind).HasConversion<int>();
        builder.Property(t => t.Channels).HasConversion<int>();

        // One platform default per key.
        builder.HasIndex(t => t.Key)
            .IsUnique()
            .HasFilter("[CommunityId] IS NULL")
            .HasDatabaseName("UX_NotificationTemplate_Platform_Key");

        // One community override per (key, community).
        builder.HasIndex(t => new { t.Key, t.CommunityId })
            .IsUnique()
            .HasFilter("[CommunityId] IS NOT NULL")
            .HasDatabaseName("UX_NotificationTemplate_Community_Key");

        builder.HasData(SeedDefaults());
    }

    /// <summary>
    /// Default platform-wide resident verification/approval templates (Req 11.2). Classified
    /// <see cref="NotificationCategoryKind.Mandatory"/> (account/security) so they are always sent
    /// regardless of optional preferences (Req 20.4). Fixed, deterministic ids so the seed is
    /// idempotent across EnsureCreated/migrations. The wording lives here as editable data; later
    /// phases add the rest of the Req 64 matrix the same way.
    /// </summary>
    private static IEnumerable<NotificationTemplate> SeedDefaults()
    {
        // Signed out elsewhere → the single-active-session notice to the OLD device being logged out
        // when the user signs in on a new device. MANDATORY "account" security notice — always
        // delivered regardless of opt-out (Req 20.4). Delivered to the specific old device via the
        // outbox TargetPlayerId. No placeholders.
        yield return Platform(
            NotificationTemplateKeys.SignedOutElsewhere,
            "Signed out",
            "You've been signed out because your account was used to sign in on another device. If this wasn't you, reset your password.");

        yield return Platform(
            NotificationTemplateKeys.ResidentVerificationApproved,
            "Verification approved",
            "Hi {residentName}, your resident verification has been approved. Your account is now active.");

        yield return Platform(
            NotificationTemplateKeys.ResidentVerificationRejected,
            "Verification update",
            "Hi {residentName}, your resident verification could not be approved. Reason: {reason}.");

        yield return Platform(
            NotificationTemplateKeys.ResidentApprovalApproved,
            "Account approved",
            "Hi {residentName}, your account for {communityName} has been approved.");

        // Resident self-onboarding — notify the community admins a request needs review.
        yield return Platform(
            NotificationTemplateKeys.JoinRequestSubmitted,
            "New resident request",
            "{name} has requested to join unit {unitNumber}. Review and approve or reject the request.");

        // Resident self-onboarding — tell the requester the outcome.
        yield return Platform(
            NotificationTemplateKeys.JoinRequestApproved,
            "Request approved",
            "Your request to join {communityName} has been approved. You can now sign in.");

        yield return Platform(
            NotificationTemplateKeys.JoinRequestRejected,
            "Request update",
            "Your request to join {communityName} could not be approved. {reason}");

        // Walk-in visitor request → the host unit's residents approve/reject (Req 24.2, Task 16.1).
        // OPTIONAL "visitor" category (a resident can opt out, Req 20.3) — unlike the mandatory
        // account notices above.
        yield return Platform(
            NotificationTemplateKeys.VisitorRequest,
            "Visitor at the gate",
            "A {category} visitor, {visitorName}, is requesting to visit your unit. Please approve or reject.",
            NotificationCategories.Visitor,
            NotificationCategoryKind.Optional);

        // Walk-in APPROVED → tells the GUARD who registered it to admit the visitor (Task 20261008).
        // Optional "visitor" category; fills {visitorName}, {unitNumber}.
        yield return Platform(
            NotificationTemplateKeys.VisitorApproved,
            "Walk-in approved",
            "The resident approved {visitorName} for unit {unitNumber}. You can admit the visitor.",
            NotificationCategories.Visitor,
            NotificationCategoryKind.Optional);

        // Walk-in REJECTED → tells the GUARD who registered it not to admit (Task 20261008).
        yield return Platform(
            NotificationTemplateKeys.VisitorRejected,
            "Walk-in rejected",
            "The resident rejected {visitorName} for unit {unitNumber}. Do not admit the visitor. {reason}",
            NotificationCategories.Visitor,
            NotificationCategoryKind.Optional);

        // Incident escalated to responders (Req 27.2, Task 16.3). MANDATORY "responder" category —
        // security staff must always receive an escalation regardless of opt-out (Req 20.4).
        yield return Platform(
            NotificationTemplateKeys.IncidentEscalated,
            "Security incident escalated",
            "A {severity} {category} incident has been escalated at {location}. Please respond.",
            NotificationCategories.Responder,
            NotificationCategoryKind.Mandatory);

        // SOS raised (Req 27.3, Task 16.3). MANDATORY "emergency" category — always delivered to the
        // configured audience regardless of opt-out (Req 20.4).
        yield return Platform(
            NotificationTemplateKeys.SosRaised,
            "Emergency SOS raised",
            "An emergency SOS has been raised at {location}. Immediate response required.",
            NotificationCategories.Emergency,
            NotificationCategoryKind.Mandatory);

        // Parcel received / taken into custody → notify the recipient unit's residents (Req 29.2,
        // Task 18.2). OPTIONAL "parcel" category (a resident can opt out, Req 20.3). Req 64.2 says a
        // parcel-received notice targets push + SMS + email, so the template declares all three
        // channels (the Phase 1 worker dispatches push today; SMS/email light up in later phases
        // without a schema or seed change — NotificationDispatcher remarks).
        yield return Platform(
            NotificationTemplateKeys.ParcelReceived,
            "Parcel received",
            "A parcel ({provider}, {trackingNumber}) has been received for your unit and is held at {custodyLocation}.",
            NotificationCategories.Parcel,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push | NotificationChannel.Sms | NotificationChannel.Email);

        // Priority variant for a priority-flagged parcel — medicine / perishable / urgent (Req 29.3,
        // 64.2). Same optional "parcel" category and the full push + SMS + email channel set, with
        // priority wording so a time-sensitive delivery is surfaced prominently. The service selects
        // this template (over the standard one) when the parcel's IsPriority flag is set.
        yield return Platform(
            NotificationTemplateKeys.ParcelReceivedPriority,
            "Priority parcel received",
            "A PRIORITY parcel ({provider}, {trackingNumber}) has been received for your unit and is held at {custodyLocation}. Please collect it promptly.",
            NotificationCategories.Parcel,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push | NotificationChannel.Sms | NotificationChannel.Email);

        // Parcel custody-SLA breach → escalation notice to the recipient residents AND the community
        // admin (Req 31.2, Task 18.4). OPTIONAL "parcel" category (same opt-out rule as the other
        // parcel notices, Req 20.3). Req 64.2 says the SLA-exceeded notice targets push + email, so
        // the template declares exactly those two channels. Fills {trackingNumber}, {provider},
        // {custodyLocation}, {hoursInCustody}.
        yield return Platform(
            NotificationTemplateKeys.ParcelSlaExceeded,
            "Parcel awaiting collection",
            "A parcel ({provider}, {trackingNumber}) has been held at {custodyLocation} for {hoursInCustody} hours and has exceeded its collection SLA. Please arrange collection.",
            NotificationCategories.Parcel,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push | NotificationChannel.Email);

        // Ticket assigned → complaint-assigned push to the maintenance-staff assignee (Req 33.5, 64.3,
        // Task 20.2). OPTIONAL "complaint" category (a staff member can opt out, Req 20.3). Req 64.3
        // says a ticket-assigned notice targets the PUSH channel, so this template declares push only.
        // Fills {ticketTitle}, {category}, {priority}.
        yield return Platform(
            NotificationTemplateKeys.TicketAssigned,
            "Ticket assigned to you",
            "A {priority} {category} ticket has been assigned to you: {ticketTitle}. Please action it within its SLA.",
            NotificationCategories.Complaint,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        // Ticket SLA breached → escalation notice to the next responder (Req 33.3, Task 20.2). OPTIONAL
        // "complaint" category (same opt-out rule as the assigned notice, Req 20.3). The template
        // declares push + email so an overdue ticket reaches the escalation audience through more than
        // the in-app channel. Fills {ticketTitle}, {category}, {priority}, {hoursOverdue}.
        yield return Platform(
            NotificationTemplateKeys.TicketEscalated,
            "Ticket overdue — escalated",
            "A {priority} {category} ticket has breached its SLA by {hoursOverdue} hours and has been escalated to you: {ticketTitle}. Please respond.",
            NotificationCategories.Complaint,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push | NotificationChannel.Email);

        // Ticket resolved → complaint-resolved notice to the requesting resident (Req 34.3, Task 20.3).
        // OPTIONAL "complaint" category (same opt-out rule as the other ticket notices, Req 20.3). The
        // template declares push + email so the resident learns of the resolution (and can give
        // feedback / reopen) through more than the in-app channel. Fills {ticketTitle}, {category}.
        yield return Platform(
            NotificationTemplateKeys.TicketResolved,
            "Your ticket has been resolved",
            "Your {category} ticket has been resolved: {ticketTitle}. If the issue persists you can reopen it, and you're welcome to rate the service.",
            NotificationCategories.Complaint,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push | NotificationChannel.Email);

        // Work order assigned → work-order-assigned push to the maintenance-staff assignee (Req 36.3,
        // Task 21.2). OPTIONAL "complaint" category (a staff member can opt out, Req 20.3), following
        // the ticket-assigned notice; its template declares the PUSH channel. Fills {workOrderTitle},
        // {workOrderType}, {priority}.
        yield return Platform(
            NotificationTemplateKeys.WorkOrderAssigned,
            "Work order assigned to you",
            "A {priority} {workOrderType} work order has been assigned to you: {workOrderTitle}. Please action it.",
            NotificationCategories.Complaint,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        // AMC contract approaching expiry → expiry reminder to the responsible user (Req 37.3, Task
        // 21.3). OPTIONAL "complaint" category (the responsible user can opt out, Req 20.3), following
        // the other maintenance notices; its template declares push + email so the approaching expiry
        // reaches the responsible user through more than the in-app channel. Fills {assetName},
        // {vendorName}, {endDate}, {daysToExpiry}.
        yield return Platform(
            NotificationTemplateKeys.AmcExpiryReminder,
            "AMC contract expiring soon",
            "The AMC for {assetName} with {vendorName} expires on {endDate} ({daysToExpiry} days). Please arrange renewal.",
            NotificationCategories.Complaint,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push | NotificationChannel.Email);

        // --- Facility bookings (Task 23.2, Req 39.4) ----------------------------------------------

        // A facility booking was created → booking notice to the booking resident. The {status}
        // placeholder distinguishes the auto-confirmed case from "awaiting approval". OPTIONAL
        // "facility_booking" category (a resident can opt out, Req 20.3); push channel. Fills
        // {amenityName}, {bookingDate}, {status}.
        yield return Platform(
            NotificationTemplateKeys.BookingCreated,
            "Facility booking {status}",
            "Your booking of {amenityName} for {bookingDate} is {status}.",
            NotificationCategories.FacilityBooking,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        // A pending booking needs approval → approval-needed notice to the amenity's approvers (Req
        // 39.3). OPTIONAL "facility_booking" category; push channel. Fills {amenityName}, {bookingDate}.
        yield return Platform(
            NotificationTemplateKeys.BookingApprovalNeeded,
            "Booking awaiting your approval",
            "A booking of {amenityName} for {bookingDate} is awaiting approval.",
            NotificationCategories.FacilityBooking,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        // A booking was confirmed/approved → confirmation notice to the booking resident (Req 39.4).
        // OPTIONAL "facility_booking" category; push channel. Fills {amenityName}, {bookingDate}.
        yield return Platform(
            NotificationTemplateKeys.BookingConfirmed,
            "Facility booking confirmed",
            "Your booking of {amenityName} for {bookingDate} has been confirmed.",
            NotificationCategories.FacilityBooking,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        // A booking was cancelled → cancellation notice to the booking resident (Req 39.4). OPTIONAL
        // "facility_booking" category; push channel. Fills {amenityName}, {bookingDate}.
        yield return Platform(
            NotificationTemplateKeys.BookingCancelled,
            "Facility booking cancelled",
            "Your booking of {amenityName} for {bookingDate} has been cancelled.",
            NotificationCategories.FacilityBooking,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        // --- Parking (Task 24.1, Req 40.4) --------------------------------------------------------

        // A parking violation was recorded against a slot assigned to the resident's unit → notice to
        // that unit's residents (Req 40.4). OPTIONAL "parking" category (a resident can opt out, Req
        // 20.3); push channel. Fills {slotCode}, {violationType}, {description}.
        yield return Platform(
            NotificationTemplateKeys.ParkingViolationRecorded,
            "Parking violation recorded",
            "A parking violation ({violationType}) was recorded against slot {slotCode}: {description}.",
            NotificationCategories.Parking,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        // --- Guest parking, EV charging & resident events (Task 24.2, Req 41.1, 41.2, 41.5) -------

        // Guest parking assigned for a resident's approved visitor (Req 41.1) → confirmation to the
        // requesting resident. OPTIONAL "parking" category (Req 20.3); push channel. Fills {slotCode},
        // {visitorName}, {validUntil}.
        yield return Platform(
            NotificationTemplateKeys.GuestParkingAssigned,
            "Guest parking assigned",
            "Guest parking slot {slotCode} has been assigned for your visitor {visitorName} until {validUntil}.",
            NotificationCategories.Parking,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        // EV-charging slot reserved for a resident (Req 41.2) → confirmation to the reserving resident.
        // OPTIONAL "parking" category (Req 20.3); push channel. Fills {slotCode}, {reservedFrom},
        // {reservedUntil}.
        yield return Platform(
            NotificationTemplateKeys.EvReservationConfirmed,
            "EV charger reserved",
            "EV charging slot {slotCode} has been reserved for you from {reservedFrom} to {reservedUntil}.",
            NotificationCategories.Parking,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);

        // Resident registered for an event with available capacity (Req 41.5) → confirmation to the
        // registering resident. OPTIONAL "event" category (Req 20.3); push channel. Fills {eventTitle},
        // {startsAt}.
        yield return Platform(
            NotificationTemplateKeys.ResidentEventRegistered,
            "Event registration confirmed",
            "You're registered for {eventTitle}, starting {startsAt}.",
            NotificationCategories.Event,
            NotificationCategoryKind.Optional,
            NotificationChannel.Push);
    }

    private static NotificationTemplate Platform(
        string key,
        string title,
        string body,
        string category = NotificationCategories.Account,
        NotificationCategoryKind categoryKind = NotificationCategoryKind.Mandatory,
        NotificationChannel channels = NotificationChannel.Push) => new()
    {
        // Deterministic id derived from the key so re-seeding is stable and never duplicates.
        Id = DeterministicGuid($"notif-template|{key}"),
        Key = key,
        Scope = ConfigScope.Platform,
        CommunityId = null,
        // Verification/approval are account notices → mandatory (Req 20.4); the visitor-request
        // notice is an optional "visitor" category (Req 20.3) passed in by the caller.
        Category = category,
        CategoryKind = categoryKind,
        TitleTemplate = title,
        BodyTemplate = body,
        Channels = channels,
        IsActive = true,
        // Audit columns are non-nullable; give the seed a fixed, deterministic stamp.
        CreatedAtUtc = SeedTimestamp,
        CreatedBy = "seed",
        RowVersion = [],
    };

    private static readonly DateTime SeedTimestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Guid DeterministicGuid(string input)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(bytes);
    }
}
