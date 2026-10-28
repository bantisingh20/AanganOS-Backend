namespace App.Application.Notifications;


public static class NotificationTemplateKeys
{
    /// <summary>
    /// The user signed in on a new device, so this device's session was ended (single-active-session).
    /// Sent to the OLD device being signed out. Category <c>account</c> (MANDATORY — a security
    /// notice, always delivered, Req 20.4). No placeholders.
    /// </summary>
    public const string SignedOutElsewhere = "account.signed-out-elsewhere";

    /// <summary>Resident verification approved → account becomes active (Req 11.2).</summary>
    public const string ResidentVerificationApproved = "resident.verification.approved";

    /// <summary>Resident verification rejected (Req 11.2).</summary>
    public const string ResidentVerificationRejected = "resident.verification.rejected";

    /// <summary>Resident account/registration approved (Req 11.2).</summary>
    public const string ResidentApprovalApproved = "resident.approval.approved";

    /// <summary>A new resident join request was submitted → notify community admins. Fills {name}, {unitNumber}.</summary>
    public const string JoinRequestSubmitted = "joinrequest.submitted";

    /// <summary>A resident join request was approved → the requester can now log in. Fills {communityName}.</summary>
    public const string JoinRequestApproved = "joinrequest.approved";

    /// <summary>A resident join request was rejected. Fills {communityName}, {reason}.</summary>
    public const string JoinRequestRejected = "joinrequest.rejected";

    /// <summary>
    /// Walk-in visitor registered at the gate → notify the host unit's residents to approve/reject
    /// (Req 24.2). Category <c>visitor</c> (optional); fills {visitorName}, {category}.
    /// </summary>
    public const string VisitorRequest = "visitor.request";

    /// <summary>
    /// A resident APPROVED a walk-in → notify the GUARD who registered it so they can admit the
    /// visitor (Task 20261008). Category <c>visitor</c> (optional); fills {visitorName}, {unitNumber}.
    /// </summary>
    public const string VisitorApproved = "visitor.approved";

    /// <summary>
    /// A resident REJECTED a walk-in → notify the GUARD who registered it (Task 20261008). Category
    /// <c>visitor</c> (optional); fills {visitorName}, {unitNumber}, {reason}.
    /// </summary>
    public const string VisitorRejected = "visitor.rejected";

    /// <summary>
    /// A pending walk-in auto-expired (not approved within the configured window) → notify the GUARD
    /// who registered it. Category <c>visitor</c> (optional); fills {visitorName}, {unitNumber}.
    /// </summary>
    public const string VisitorExpired = "visitor.expired";

    /// <summary>
    /// A high-severity incident met the configured escalation threshold → notify the responders
    /// (Req 27.2). Category <c>responder</c> (mandatory — security staff must always receive it);
    /// fills {category}, {severity}, {location}.
    /// </summary>
    public const string IncidentEscalated = "incident.escalated";

    /// <summary>
    /// A parcel was received / taken into custody for a recipient unit → notify the unit's residents
    /// (Req 29.2, 64.2). Category <c>parcel</c> (optional — a resident can opt out, Req 20.3); fills
    /// {trackingNumber}, {provider}, {custodyLocation}. The priority variant for medicine/perishable/
    /// urgent parcels is <see cref="ParcelReceivedPriority"/> (Req 29.3).
    /// </summary>
    public const string ParcelReceived = "parcel.received";

    /// <summary>
    /// Priority variant of <see cref="ParcelReceived"/> for a priority-flagged parcel (medicine /
    /// perishable / urgent, Req 29.3): same optional <c>parcel</c> category but its template declares
    /// the full push + SMS + email channel set (Req 64.2) and priority wording, so a time-sensitive
    /// delivery reaches the recipient through more channels. Fills the same placeholders.
    /// </summary>
    public const string ParcelReceivedPriority = "parcel.received.priority";

    /// <summary>
    /// A parcel sat in custody past its configured Parcel_SLA → escalation notice to the recipient
    /// residents AND the community admin (Req 31.2, 64.2). Category <c>parcel</c>; its template
    /// declares the push + email channel set per Req 64.2. Fills {trackingNumber}, {provider},
    /// {custodyLocation}, {hoursInCustody}.
    /// </summary>
    public const string ParcelSlaExceeded = "parcel.sla.exceeded";

    /// <summary>
    /// An SOS alert was raised → emergency notification to the configured community audience
    /// (Req 27.3). Category <c>emergency</c> (MANDATORY — always delivered regardless of opt-out,
    /// Req 20.4); fills {location}.
    /// </summary>
    public const string SosRaised = "sos.raised";

    /// <summary>
    /// A helpdesk ticket was assigned (or re-assigned) to a maintenance-staff member → complaint-
    /// assigned push to that staff member (Req 33.5, 64.3). Category <c>complaint</c> (optional —
    /// Req 20.3); Req 64.3 says the ticket-assigned notice targets the PUSH channel, so its template
    /// declares push only. Fills {ticketTitle}, {category}, {priority}.
    /// </summary>
    public const string TicketAssigned = "ticket.assigned";

    /// <summary>
    /// A helpdesk ticket breached its SLA while unresolved → escalation notice to the NEXT responder
    /// (Req 33.3). Category <c>complaint</c>; its template declares push + email so an overdue ticket
    /// reaches the escalation audience through more than the in-app channel. Fills {ticketTitle},
    /// {category}, {priority}, {hoursOverdue}.
    /// </summary>
    public const string TicketEscalated = "ticket.escalated";

    /// <summary>
    /// A helpdesk ticket was resolved → complaint-resolved notice to the requesting resident
    /// (Req 34.3). Category <c>complaint</c> (optional — Req 20.3); its template declares push + email
    /// so the resident learns of the resolution (and can give feedback / reopen) through more than the
    /// in-app channel. Fills {ticketTitle}, {category}.
    /// </summary>
    public const string TicketResolved = "ticket.resolved";

    /// <summary>
    /// A maintenance work order was assigned (or re-assigned) to a maintenance-staff member →
    /// work-order-assigned push to that staff member (Req 36.3). Category <c>complaint</c> (optional —
    /// Req 20.3), following <see cref="TicketAssigned"/>; its template declares the PUSH channel. Fills
    /// {workOrderTitle}, {workOrderType}, {priority}.
    /// </summary>
    public const string WorkOrderAssigned = "work-order.assigned";

    /// <summary>
    /// An AMC contract has entered its configured expiry-reminder window → expiry reminder to the
    /// responsible user (Req 37.3). Category <c>complaint</c> (optional — Req 20.3), following the
    /// other maintenance notices; its template declares push + email so the responsible user learns of
    /// the approaching expiry through more than the in-app channel. Fills {assetName}, {vendorName},
    /// {endDate}, {daysToExpiry}.
    /// </summary>
    public const string AmcExpiryReminder = "amc.expiry-reminder";

    /// <summary>
    /// A facility booking was created (Req 39.4, Task 23.2) → booking notice to the booking resident.
    /// Covers both the auto-confirmed case and the "awaiting approval" case; the wording distinguishes
    /// them via the {status} placeholder. Category <c>facility_booking</c> (optional — a resident can
    /// opt out, Req 20.3); its template declares push + in-app. Fills {amenityName}, {bookingDate},
    /// {status}.
    /// </summary>
    public const string BookingCreated = "booking.created";

    /// <summary>
    /// A pending facility booking requires approval (Req 39.3, 39.4) → approval-needed notice to the
    /// amenity's approvers. Category <c>facility_booking</c> (optional — Req 20.3); its template
    /// declares push + in-app so an approver is prompted to action it. Fills {amenityName},
    /// {bookingDate}.
    /// </summary>
    public const string BookingApprovalNeeded = "booking.approval-needed";

    /// <summary>
    /// A facility booking was confirmed/approved (Req 39.4) → confirmation notice to the booking
    /// resident. Category <c>facility_booking</c> (optional — Req 20.3); its template declares push +
    /// in-app. Fills {amenityName}, {bookingDate}.
    /// </summary>
    public const string BookingConfirmed = "booking.confirmed";

    /// <summary>
    /// A facility booking was cancelled (Req 39.4) → cancellation notice to the booking resident.
    /// Category <c>facility_booking</c> (optional — Req 20.3); its template declares push + in-app.
    /// Fills {amenityName}, {bookingDate}.
    /// </summary>
    public const string BookingCancelled = "booking.cancelled";

    /// <summary>
    /// A parking violation was recorded against a slot assigned to a resident's unit (Req 40.4, Task
    /// 24.1) → notice to that unit's residents. Category <c>parking</c> (optional — a resident can opt
    /// out, Req 20.3); its template declares push + in-app. Fills {slotCode}, {violationType},
    /// {description}.
    /// </summary>
    public const string ParkingViolationRecorded = "parking.violation.recorded";

    /// <summary>
    /// Guest parking was assigned for a resident's approved visitor (Req 41.1, Task 24.2) → confirmation
    /// notice to the requesting resident. Category <c>parking</c> (optional — Req 20.3); push channel.
    /// Fills {slotCode}, {visitorName}, {validUntil}.
    /// </summary>
    public const string GuestParkingAssigned = "parking.guest.assigned";

    /// <summary>
    /// An EV-charging slot was reserved for a resident (Req 41.2, Task 24.2) → confirmation notice to
    /// the reserving resident. Category <c>parking</c> (optional — Req 20.3); push channel. Fills
    /// {slotCode}, {reservedFrom}, {reservedUntil}.
    /// </summary>
    public const string EvReservationConfirmed = "parking.ev.reserved";

    /// <summary>
    /// A resident registered for a resident event with available capacity (Req 41.5, Task 24.2) →
    /// confirmation notice to the registering resident. Category <c>event</c> (optional — Req 20.3);
    /// push channel. Fills {eventTitle}, {startsAt}.
    /// </summary>
    public const string ResidentEventRegistered = "event.registered";
}

/// <summary>
/// Well-known notification category names used for the per-resident opt-in check (Req 20.3). Open
/// set, same as template keys — later modules introduce their own categories. The resident
/// verification/approval notices are classified mandatory (account/security) so they are always
/// delivered (Req 20.4); this constant names that category.
/// </summary>
public static class NotificationCategories
{
    /// <summary>Account/verification/security notices — mandatory, always delivered (Req 20.4).</summary>
    public const string Account = "account";

    // Optional categories a resident can opt out of (Req 20.1, 20.3). Open set, aligned with the
    // Req 64 notification matrix — later modules add their own categories simply by seeding a new
    // Notification_Category row; these constants name the ones relevant to Phase 2/3 so the seed and
    // any in-code reference avoid a scattered literal (steering: no hardcoding).

    /// <summary>Parcel/delivery notices (Phase 4). Optional.</summary>
    public const string Parcel = "parcel";

    /// <summary>Complaint/ticket updates. Optional.</summary>
    public const string Complaint = "complaint";

    /// <summary>Invoice / billing notices. Optional.</summary>
    public const string Invoice = "invoice";

    /// <summary>Facility / amenity booking notices. Optional.</summary>
    public const string FacilityBooking = "facility_booking";

    /// <summary>Document-expiry reminders. Optional.</summary>
    public const string DocumentExpiry = "document_expiry";

    /// <summary>Community announcements / general broadcasts. Optional.</summary>
    public const string Announcement = "announcement";

    /// <summary>Gate visitor requests (walk-in approval, Req 24.2). Optional.</summary>
    public const string Visitor = "visitor";

    /// <summary>Parking notices — parking-violation alerts, guest-parking + EV-reservation confirmations (Req 40.4, 41.1, 41.2). Optional.</summary>
    public const string Parking = "parking";

    /// <summary>Resident-event notices — event-registration confirmations (Req 41.5). Optional.</summary>
    public const string Event = "event";

    /// <summary>
    /// Emergency/safety alerts (SOS, Req 27.3). Classified mandatory (safety) like account notices,
    /// so it is always delivered regardless of opt-out (Req 20.4); seeded in the category list.
    /// </summary>
    public const string Emergency = "emergency";

    /// <summary>
    /// Security-responder notices — incident escalation to responders (Req 27.2). Classified
    /// mandatory (security staff must always receive an escalation), so it is always delivered
    /// regardless of opt-out (Req 20.4).
    /// </summary>
    public const string Responder = "responder";
}
