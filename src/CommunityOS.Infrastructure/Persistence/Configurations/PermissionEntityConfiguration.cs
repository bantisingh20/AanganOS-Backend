using CommunityOS.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunityOS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps <see cref="Permission"/> and seeds the foundation permission catalog as editable data
/// (Req 3.2). Unique(ResourceType, Action) prevents a duplicate catalog entry. The seed is the
/// known (ResourceType, Action) vocabulary used by the foundation modules so far — it is <b>data</b>
/// an admin can extend/retire, not a compiled gate; the Task 5.3 evaluator checks requirements
/// against these rows, and later modules add their own resource types simply by inserting rows.
/// </summary>
public sealed class PermissionEntityConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");

        builder.Property(p => p.ResourceType).IsRequired().HasMaxLength(128);
        builder.Property(p => p.Action).HasConversion<int>();

        // Exactly one catalog entry per (resource type, action) (Req 3.2).
        builder.HasIndex(p => new { p.ResourceType, p.Action })
            .IsUnique()
            .HasDatabaseName("UX_Permission_ResourceType_Action");

        builder.HasData(SeedCatalog());
    }

    /// <summary>
    /// Default platform permission catalog (Req 3.2): the foundation resource types with the actions
    /// that make sense for each. Deterministic ids so re-seeding (EnsureCreated across runs) is
    /// idempotent. Admins edit/add rows later — nothing here is a hardcoded authorization decision.
    /// </summary>
    private static IEnumerable<Permission> SeedCatalog()
    {
        // Which actions each foundation resource type supports. Read-only/config resources get a
        // narrower set than fully-managed ones (least-privilege vocabulary, Req 4.3).
        var catalog = new (string ResourceType, PermissionAction[] Actions)[]
        {
            ("User", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete, PermissionAction.Assign]),
            ("Role", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete, PermissionAction.Assign]),
            ("Permission", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete]),
            ("UserRole", [PermissionAction.View, PermissionAction.Assign, PermissionAction.Delete]),
            ("Configuration", [PermissionAction.View, PermissionAction.Edit]),
            ("MasterData", [PermissionAction.View, PermissionAction.Edit]),
            ("Audit", [PermissionAction.View, PermissionAction.Export]),
            // Community / Property module (Task 11.3) — the resource types the Organization/Community/
            // Hierarchy/Unit endpoints gate on, so roles can grant them (Req 12.x, 13.x, 14.x, 4.1).
            ("Organization", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete]),
            ("Community", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete]),
            ("HierarchyNode", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete]),
            ("Unit", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete]),
            // Resident module (Task 12.1). Resident gets Approve as well — the action the Task 12.3
            // verification/approval workflow gates on (Req 17.2, 17.5), seeded now so roles can grant
            // it ahead of that task. HouseholdMember and EmergencyContact are managed via the resident
            // service, so they carry the standard View/Create/Edit/Delete vocabulary.
            ("Resident", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete, PermissionAction.Approve]),
            ("HouseholdMember", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete]),
            ("EmergencyContact", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete]),
            // Ownership/tenancy history (Task 12.2) — the resource types the Task 12.7 history
            // endpoints gate on (GET /units/{id}/ownership-history | /tenancy-history, Req 16.4). As
            // append-only registers they carry View (read the chronological history) + Create (assign
            // ownership / begin a tenancy) + Edit (close a prior entry on transfer / end a tenancy);
            // no Delete — history is retained, never removed (Req 16.3).
            ("OwnershipHistory", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit]),
            ("TenancyHistory", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit]),
            // Resident assets (Task 12.5). Vehicle and Pet are managed via the resident-assets
            // service, so they carry the standard View/Create/Edit/Delete vocabulary (Req 19.1, 19.3,
            // 19.5). ParkingSlot and StorageLocation additionally get Assign — the action the
            // associate-slot/storage-to-a-unit operation gates on (Req 19.4) — seeded so roles can
            // grant it. The full parking module's extra actions are Phase 6 (Task 24).
            ("Vehicle", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete]),
            ("Pet", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete]),
            ("ParkingSlot", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete, PermissionAction.Assign]),
            ("StorageLocation", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete, PermissionAction.Assign]),
            // Gate module — visit passes + walk-ins (Task 16.1). VisitPass carries View/Create/Edit/
            // Delete — a resident creates a pass for their own unit (Create), lists their passes
            // (View), revokes (Edit) (Req 23.1-23.4). Visitor additionally gets Approve — the action
            // the resident approve/reject of a pending walk-in gates on (Req 24.3); a guard registers
            // a walk-in with Create (Req 24.1). Gate QR/OTP verification is a Create on EntryExitEvent
            // (Task 16.2), not gated here. Seeded as editable catalog data so roles can grant them.
            ("VisitPass", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete]),
            ("Visitor", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Approve, PermissionAction.Delete]),
            // Recurring visitors (Task 16, Req 25.2): a resident defines regular visitors (daily help /
            // staff) for their own unit (Create), lists + manages them (View), and revokes (modelled as
            // a Create grant on the revoke endpoint). This MUST be in the catalog or the
            // /recurring-visitors endpoints 403 for everyone but superadmin (the bug this fixes).
            ("RecurringVisitor", [PermissionAction.View, PermissionAction.Create]),
            // Entry/exit events (Task 16.2, Req 25.3-25.5). A guard records an ENTRY and a matching
            // EXIT at the gate — BOTH gate on (Create, EntryExitEvent) (the exit closes the open
            // entry, modelled as another create). View lists the gate log. This MUST be in the catalog
            // or the entry/exit endpoints 403 for everyone but superadmin — the earlier "verification
            // not gated here" note was wrong; the endpoints really do require this grant. Seeded as
            // editable catalog data so the Security role (and others) can hold it.
            ("EntryExitEvent", [PermissionAction.View, PermissionAction.Create]),
            // Gate security operations (Task 16.3). WatchlistEntry: View gates reads (Req 26.3),
            // Create adds/clears entries (Req 26.1, 26.5); the watchlist-match override at entry reuses
            // Visitor.Approve (above). GuardShift: View lists the roster (Req 26.5), Create schedules
            // (Req 26.4). Incident: View/Create (report, Req 27.1) + Edit (acknowledge/resolve) — Create
            // on Incident is also the guard-facing grant to trigger an access-control integration
            // (Req 27.5). SosAlert: View/Create (raise, Req 27.3) + Edit (acknowledge/resolve, Req 27.4).
            // Seeded as editable catalog data so roles can grant them.
            ("WatchlistEntry", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete]),
            ("GuardShift", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete]),
            ("Incident", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit]),
            ("SosAlert", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit]),
            // Parcel module — registration + status lifecycle (Task 18.1). A guard registers a
            // received parcel with Create (Req 28.1); a resident lists parcels addressed to their own
            // unit with View (Req 28.5, resident self-scope); a status change through the configurable
            // state machine is an Edit (Req 28.3). No Delete — a parcel's lifecycle ends by a terminal
            // status (handed_over / returned / lost_damaged), not a row removal; custody/handover/
            // timeline actions (Tasks 18.2-18.4) reuse these grants on the same resource. Seeded as
            // editable catalog data so roles can grant them.
            ("Parcel", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit]),
            // Parcel module — custody locations (Task 18.2). CustodyLocation is a community-managed
            // master row (the secure bins/shelves a parcel is held in, Req 29.1), so it carries the
            // standard View/Create/Edit/Delete vocabulary an operator uses to manage the location
            // catalog. Assigning a location to a parcel on the In Custody transition is gated on the
            // parcel's own Edit grant (above) — the assignment lives on the Parcel row — so no separate
            // Assign action is seeded here. Seeded as editable catalog data so roles can grant them.
            ("CustodyLocation", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete]),
            // Parcel module — authorized collector + verified handover (Task 18.3). A resident
            // authorizes a collector for a parcel addressed to their OWN unit with Create on
            // ParcelAuthorization (Req 30.1) and lists/revokes their authorizations with View/Edit; a
            // resident self-scope principal only ever sees their own unit's authorizations. The guard's
            // verified handover itself is gated on the parcel's own Edit grant (above) — the handover
            // transitions the Parcel row to handed_over (Req 30.4) — so no separate handover action is
            // seeded. No Delete here (an authorization is retired by expiry/consumption, not removal).
            // Seeded as editable catalog data so roles can grant them.
            ("ParcelAuthorization", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit]),
            // Parcel module — append-only custody timeline (Task 18.4, Req 31.1, 31.5). The timeline
            // is SYSTEM-WRITTEN (every parcel event appends an entry inside the parcel operation) and
            // append-only, so it carries View ONLY — there is no Create/Edit/Delete grant because no
            // caller writes or mutates a timeline entry directly, and history is never removed. The
            // lost/damaged, returned and SLA-escalation ACTIONS are an Edit on Parcel (they transition
            // the Parcel row); linking an Incident on lost/damaged reuses the Create-on-Incident grant
            // of SecurityOpsService — no separate action is seeded here. Seeded as editable catalog
            // data so a role can grant the timeline read (Req 31.5).
            ("CustodyTimelineEntry", [PermissionAction.View]),
            // Helpdesk module — ticket creation + classification + attachments (Task 20.1). A resident
            // raises a ticket for their OWN unit with Create (Req 32.1) and lists/reads tickets for
            // their own household/unit with View (Req 32.4, resident self-scope); staff with View see
            // all tickets in their authorized communities and manage them with Edit (the Task 20.3
            // lifecycle transitions reuse this Edit grant on the same resource). Assigning / re-
            // assigning a ticket to a maintenance-staff member carries the dedicated Assign action
            // (Task 20.2, Req 33.4 — "a user with the Assign permission reassigns a Ticket"), the same
            // Assign vocabulary User/Role/ParkingSlot/StorageLocation already use. No Delete — a
            // ticket's lifecycle ends by a terminal status (closed), not a row removal. Seeded as
            // editable catalog data so roles can grant them.
            ("Ticket", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Assign]),
            // Helpdesk module — ticket attachments (Task 20.1, Req 32.3). A resident attaches a photo/
            // video/document to a ticket they can access with Create and lists them with View; the
            // bytes are stored by File_Service and streamed only through its own authorized download
            // path (View on the file's owning Ticket). A resident self-scope principal only ever sees
            // attachments on their own unit's tickets. No Delete here (an attachment is retained with
            // its ticket's history). Seeded as editable catalog data so roles can grant them.
            ("TicketAttachment", [PermissionAction.View, PermissionAction.Create]),
            // Helpdesk ticket comments — the discussion thread. A resident or staff member reads the
            // thread (View) and posts a message (Create). No Edit/Delete — comments are retained with
            // the ticket's history. MUST be in the catalog or the comment endpoints 403 for everyone
            // but the PowerUser (the catalog-gap class of bug). Seeded as editable catalog data.
            ("TicketComment", [PermissionAction.View, PermissionAction.Create]),
            // Resident self-onboarding join requests. A community admin reads the pending queue
            // (View) and approves/rejects (Approve). Submission is anonymous (no grant). MUST be in
            // the catalog or the admin endpoints 403 for everyone but PowerUser (catalog-gap rule).
            ("JoinRequest", [PermissionAction.View, PermissionAction.Approve]),
            // Maintenance module — asset register (Task 21.1, Req 35.1-35.4). A Facility Manager
            // registers an asset with Create (Req 35.1), lists/reads the register scoped to their
            // authorized communities with View (Req 35.3), edits an asset / drives its lifecycle
            // status with Edit (Req 35.1), and archives an asset with Delete — a Soft_Delete that
            // keeps the row traceable (Req 35.4). Seeded as editable catalog data so roles can grant
            // them. Work orders (21.2) and inspections/AMC/meters (21.3) add their own resource types.
            ("Asset", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete]),
            // Maintenance module — preventive/corrective work orders (Task 21.2, Req 36.1-36.5). A
            // Facility Manager defines a preventive MaintenancePlan with Create and manages it with
            // View/Edit (pause/retire via Edit; no Delete — a plan is soft-deleted/retired, kept
            // traceable). A WorkOrder is raised (corrective) or generated (preventive) and carries
            // View/Create/Edit/Assign — Create raises a corrective order (Req 36.2); View lists/reads;
            // Edit drives the status state machine, records parts/labour and completes the order
            // (Req 36.4, 36.5); the dedicated Assign action gates assigning/re-assigning the order to
            // a maintenance-staff member (Req 36.3), the same Assign vocabulary Ticket/User/Role use.
            // Parts/labour are managed via the WorkOrder's own Edit grant (they are children recorded
            // against the order) and the equipment history is SYSTEM-WRITTEN + read with the
            // WorkOrder View grant, so no separate resource types are seeded. Seeded as editable
            // catalog data so roles can grant them. Inspections/AMC/meters (21.3) add their own types.
            ("MaintenancePlan", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit]),
            ("WorkOrder", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Assign]),
            // Maintenance module — inspections / AMC contracts / meter history (Task 21.3, Req
            // 37.1-37.5). A Facility Manager completes an InspectionChecklist with Create and
            // lists/reads them with View (Req 37.1); records an AmcContract with Create, manages it
            // with View/Edit (status/renewal via Edit; no Delete — a contract is soft-deleted/retired,
            // kept traceable, Req 37.2); and records a MeterReading with Create and lists/reads them
            // with View (Req 37.4). Meter readings and inspections are recorded facts (no Edit/Delete
            // — a correction is a new reading / inspection), and completion appends to the asset's
            // SYSTEM-WRITTEN equipment history read with the WorkOrder View grant (Req 37.5). Seeded as
            // editable catalog data so roles can grant them.
            ("InspectionChecklist", [PermissionAction.View, PermissionAction.Create]),
            ("AmcContract", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit]),
            ("MeterReading", [PermissionAction.View, PermissionAction.Create]),
            // Amenities module — amenity master + slots + rules + blackout dates (Task 23.1, Req
            // 38.1-38.4). A Facility Manager defines a bookable Amenity with Create, lists/reads the
            // register scoped to their authorized communities with View (Req 38.1), edits an amenity /
            // drives its lifecycle status AND configures its booking-rule set with Edit (the rule-set
            // is managed via the amenity's Edit grant — it is the amenity's booking policy, Req 38.3),
            // and archives an amenity with Delete — a Soft_Delete that keeps the row traceable.
            // FacilitySlot carries View/Create/Edit/Delete — a FM defines a slot's time window +
            // capacity (Create, Req 38.2), lists them (View), edits the window/capacity/active flag
            // (Edit) and retires a slot (Delete, a Soft_Delete). BlackoutDate carries View/Create/
            // Delete — a FM defines a blackout range (Create, Req 38.4), lists them (View) and lifts a
            // blackout (Delete, a Soft_Delete that makes the amenity bookable again); a blackout is a
            // fact that is added or removed, not edited, so no Edit action is seeded. The booking-rule
            // set has no own resource type — it is gated on Amenity.Edit (above). Seeded as editable
            // catalog data so roles can grant them. The booking lifecycle (23.2) and the HTTP
            // controllers (24.3) add their own Booking resource type / actions later.
            ("Amenity", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete]),
            ("FacilitySlot", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete]),
            ("BlackoutDate", [PermissionAction.View, PermissionAction.Create, PermissionAction.Delete]),
            // Booking lifecycle (Task 23.2, Req 39.1-39.5): a resident books a slot with Create and
            // cancels their own booking with Edit (cancel is a status move, not a hard delete — the
            // row stays traceable and its capacity seat is released); a Facility Manager lists/reads
            // bookings in their communities with View and approves a pending booking with Approve
            // (Req 39.3). No Delete action — a booking is cancelled (Edit), never hard-deleted, so its
            // reservation history is preserved. Seeded as editable catalog data so roles can grant
            // them; the HTTP controllers (24.3) consume these grants.
            ("Booking", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Approve]),
            // Parking module — layout + assignment + violations + occupancy (Task 24.1, Req 40.1-40.5).
            // ParkingZone is a layout-master row an operator manages → View/Create/Edit/Delete (Req
            // 40.1). ParkingSlot is EVOLVED from Phase 2: it keeps its Phase 2 View/Create/Edit/Delete/
            // Assign vocabulary — Create defines a slot in the layout (Req 40.1) and View backs the
            // occupancy dashboard (Req 40.5) — and the Phase 6 assignment uses the dedicated Assign
            // action, now realized on the ParkingAssignment register. ParkingAssignment carries
            // View/Assign — Assign creates/reassigns/releases the one active assignment per slot (Req
            // 40.2, 40.3), View lists a slot's assignment history; no Create/Edit/Delete because an
            // assignment is appended/closed through the Assign action (append-only, never edited or
            // hard-deleted). ParkingViolation carries View/Create/Edit — Create records a violation
            // (Req 40.4), View lists them, Edit drives its open→resolved/waived status; no Delete (a
            // violation is retained, waived not removed). Seeded as editable catalog data so roles can
            // grant them; the HTTP controllers (24.3) consume these grants.
            ("ParkingZone", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete]),
            ("ParkingAssignment", [PermissionAction.View, PermissionAction.Assign]),
            ("ParkingViolation", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit]),
            // Guest parking, EV charging + resident events (Task 24.2, Req 41.1-41.5). GUEST PARKING
            // reuses the existing ParkingAssignment Assign grant (a resident-requested guest slot is a
            // visitor-type ParkingAssignment on the SAME one-active-per-slot register, Req 41.1) — no
            // new resource is introduced for it. EvReservation carries View/Create/Edit — Create
            // reserves an EV slot (one active reservation per slot, Req 41.2, 41.3), View lists a
            // slot's/unit's reservations, Edit drives its reserved→charging→completed/cancelled
            // lifecycle (release); no Delete (a reservation is cancelled, not hard-deleted — the record
            // is retained). ResidentEvent carries View/Create/Edit — Create both PUBLISHES an event
            // (Req 41.4) and, for a registering resident, is the create grant for a registration (Req
            // 41.5); View lists/browses events; Edit updates/cancels an event; no Delete (an event is
            // cancelled, not hard-deleted). Seeded as editable catalog data so roles can grant them;
            // the HTTP controllers (24.3) consume these grants.
            ("EvReservation", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit]),
            ("ResidentEvent", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit]),
            // Community notice board (MVP notices slice). A resident reads the board with View (the
            // only grant the MVP read endpoints gate on); Create/Edit/Delete are seeded for the
            // forthcoming admin publish/manage task so a role can grant them ahead of that work (an
            // announcement is published, edited and retired — a Soft_Delete that keeps it traceable).
            // Seeded as editable catalog data so roles can grant them.
            ("Announcement", [PermissionAction.View, PermissionAction.Create, PermissionAction.Edit, PermissionAction.Delete]),
            // File_Service (Task 6.1) — multipart upload + authorized streaming download. Create
            // gates the upload endpoint, View gates the download endpoint (on top of the View check
            // the service runs against the file's OWNING resource type). Seeded so non-superadmin
            // roles (e.g. Resident capturing a visitor/guard photo or a ticket attachment) can be
            // granted file access; without this entry the grant is dropped and only superadmin, who
            // bypasses the gate, could ever upload/download — so photos saved but never displayed.
            ("File", [PermissionAction.View, PermissionAction.Create]),
        };

        foreach (var (resourceType, actions) in catalog)
        {
            foreach (var action in actions)
            {
                yield return new Permission
                {
                    Id = DeterministicGuid($"{resourceType}|{action}"),
                    ResourceType = resourceType,
                    Action = action,
                    CreatedAtUtc = SeedTimestamp,
                    CreatedBy = "seed",
                    RowVersion = [],
                };
            }
        }
    }

    private static readonly DateTime SeedTimestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Guid DeterministicGuid(string input)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return new Guid(bytes);
    }
}
