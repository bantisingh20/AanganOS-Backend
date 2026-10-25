using CommunityOS.Application.Abstractions;
using CommunityOS.Application.Abstractions.Audit;
using CommunityOS.Application.Abstractions.Idempotency;
using CommunityOS.Application.Abstractions.Security;
using CommunityOS.Application.Audit;
using CommunityOS.Application.Auth;
using CommunityOS.Application.Community;
using CommunityOS.Application.Config;
using CommunityOS.Application.Files;
using CommunityOS.Application.Gate;
using CommunityOS.Application.Helpdesk;
using CommunityOS.Application.Notifications;
using CommunityOS.Application.Parcel;
using CommunityOS.Application.Querying;
using CommunityOS.Application.Identity;
using CommunityOS.Application.Maintenance;
using CommunityOS.Application.Residents;
using CommunityOS.Application.Amenities;
using CommunityOS.Application.Parking;
using CommunityOS.Infrastructure.Amenities;
using CommunityOS.Infrastructure.Parking;
using CommunityOS.Infrastructure.Audit;
using CommunityOS.Infrastructure.Auth;
using CommunityOS.Infrastructure.Community;
using CommunityOS.Infrastructure.Config;
using CommunityOS.Infrastructure.Files;
using CommunityOS.Infrastructure.Gate;
using CommunityOS.Infrastructure.Helpdesk;
using CommunityOS.Infrastructure.Idempotency;
using CommunityOS.Infrastructure.Identity;
using CommunityOS.Infrastructure.Maintenance;
using CommunityOS.Infrastructure.Notifications;
using CommunityOS.Infrastructure.Parcel;
using CommunityOS.Infrastructure.Persistence;
using CommunityOS.Infrastructure.Residents;
using CommunityOS.Infrastructure.Persistence.Interceptors;
using CommunityOS.Infrastructure.Querying;
using CommunityOS.Infrastructure.Security;
using CommunityOS.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CommunityOS.Infrastructure;

/// <summary>
/// Infrastructure composition root. Hosts (API, Worker, tests) call <see cref="AddInfrastructure"/>
/// to register the data layer. The connection string and command timeout come from configuration
/// (options pattern — nothing hardcoded); secrets live in appsettings.Development.json / user-secrets
/// / environment variables, never in source.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers EF Core (SQL Server provider), the audit interceptor and the system clock.
    /// <paramref name="configureProvider"/> lets tests swap in a relational provider (SQLite /
    /// LocalDB) without touching production wiring; when null the configured SQL Server connection
    /// string is used.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<DbContextOptionsBuilder>? configureProvider = null)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<AuditSaveChangesInterceptor>();

        // Pagination/filter/sort/search infrastructure (Task 2.3). Page-size bounds come from
        // configuration (no hardcoding, Req 9.4); the executor runs COUNT + page as async SQL.
        services.AddOptions<PaginationOptions>()
            .Bind(configuration.GetSection(PaginationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IAsyncQueryExecutor, EfAsyncQueryExecutor>();

        // Config / master-data framework (Task 3.1). In-memory cache for safe reference/config data (Req 65.2).
        services.AddMemoryCache();
        services.AddScoped<IConfigService, ConfigService>();

        // Audit foundation (Task 4.1): the append-only Audit_Service, and the REAL IConfigAuditSink
        // adapter over it — so Config_Service edits now write genuine AuditLog entries (Req 8.2).
        // Replaces the NoOpConfigAuditSink seam. Add (not TryAdd) so this real sink wins over the
        // no-op default regardless of registration order.
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IConfigAuditSink, AuditConfigAuditSink>();

        // Page-size policy is now config-driven: per-community override via Config_Service, falling
        // back to the platform PaginationOptions default (Req 9.4, 65.2). Replaces OptionsPageSizePolicy
        // while keeping the IPageSizePolicy seam the pagination helper depends on unchanged (Task 2.3).
        services.AddScoped<IPageSizePolicy, ConfigPageSizePolicy>();

        // Identity / RBAC management services (Task 5.1). User/role/permission CRUD, scoped
        // role assignment, and the effective-permissions query the Task 5.3 evaluator consumes.
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IUserRoleService, UserRoleService>();
        services.AddScoped<IPermissionCatalog, PermissionCatalog>();
        services.AddScoped<IEffectivePermissionQuery, EffectivePermissionQuery>();

        // Community / Property module (Task 11.1). Organization + Community create/edit/soft-delete/get
        // (community create assigns CommunityId == its own id; community-type validated against the
        // configurable Community_Type master-data list) and the configurable, optional-level
        // HierarchyNode (same-community parent validation, Req 13.3; tenant-scoped hierarchy reads,
        // Req 13.4). All mutations audited via IAuditService; delete is a soft delete (Req 12.5/13.5).
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<ICommunityService, CommunityService>();
        services.AddScoped<IHierarchyService, HierarchyService>();

        // Unit management (Task 11.2). Create/edit/soft-delete/get a unit with its number, type,
        // area, floor, bedrooms, status and free-form custom attributes. Unit_Type/Unit_Status are
        // validated against the configurable master-data lists (not enums); unit-number uniqueness
        // is DB-enforced (Unique(CommunityId, UnitNumber)) and concurrency-safe — a duplicate maps to
        // CONFLICT via UniqueConstraintGuard rather than a read-then-write pre-check (Req 14.4, 22.1).
        // RowVersion gives optimistic concurrency on edit (Req 22.4); mutations are audited.
        services.AddScoped<IUnitService, UnitService>();

        // Bulk Unit import/export (Task 13.1). CSV import validates each row independently and
        // creates valid rows, skipping invalid ones with a per-row reason (partial success, Req
        // 21.1/21.2); the configurable Unit_Type/Unit_Status lists are resolved ONCE per community
        // (no N+1) and existing numbers loaded once so a duplicate is a per-row skip. The DB unique
        // index stays the authority under concurrency (racing duplicate isolated per row). Export is
        // Export⊆View (EnsureCanExportAsync) + tenant-filtered + AsNoTracking projection, so it never
        // exceeds the caller's viewable scope (Req 21.3/66.6). Both honour the configured row cap
        // (BulkOptions, Req 21.4) and append an Audit_Log entry with counts (Req 21.5).
        services.AddOptions<BulkOptions>()
            .Bind(configuration.GetSection(BulkOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IUnitBulkService, UnitBulkService>();

        // Resident foundation (Task 12.1). Create a resident with a configurable type + CommunityId
        // (VerificationStatus defaults to pending, Req 15.1); associate a resident with a unit via a
        // HouseholdMember (same-community check, Req 15.2); add/list emergency contacts (Req 15.3);
        // edit; soft-delete (Req 15.5); get; and a paginated/filter(verificationStatus,type)/sort/
        // search ListAsync (Req 15.4) over the shared pagination infra + an allow-listed query config.
        // ResidentType is validated against the configurable Resident_Type master-data list (not an
        // enum); mutations are audited via IAuditService. HouseholdMember is IUnitScoped so the
        // resident self-scope filter bounds a resident principal to their own household (Req 5.4/66.3).
        // Task 12.3 adds the verification/approval workflow to the same service: Approve/Reject/
        // Resubmit gated on the Approve permission via IAuthorizationGuard (defense-in-depth, Req 17.5),
        // approve-once under concurrency via the resident RowVersion → CONFLICT (Req 22.3), and a
        // resident notification enqueued through INotificationService in the same transaction (Req 17.4).
        services.AddScoped<IResidentService, ResidentService>();

        // Ownership / tenancy history (Task 12.2). Assign/transfer a unit's owner, begin/end a
        // tenancy, and retrieve a unit's chronological ownership + tenancy history. Append-only:
        // mutations append a new row or close a prior one by stamping EndDate — a prior row is never
        // removed, so the full history stays traceable (Req 16.1-16.4). Assigning a new owner closes
        // the prior open owner and inserts the new one in ONE transaction, so a unit never has two
        // open owners under concurrency (on SQL Server also backed by a filtered unique index). Owner/
        // tenant and unit are validated same-community; reads are tenant-scoped + AsNoTracking;
        // mutations are audited via IAuditService.
        services.AddScoped<IUnitHistoryService, UnitHistoryService>();

        // Resident assets (Task 12.5). Register vehicles/pets against a unit, create parking slots /
        // storage locations and assign them to a unit, and read a unit's vehicles/pets/parking/
        // storage (Req 19.1-19.5). Vehicle/Pet types validate against the configurable Vehicle_Type/
        // Pet_Type master-data lists (not enums). Vehicle registration uniqueness is DB-enforced
        // (Unique(CommunityId, RegistrationNumber)) and concurrency-safe — a duplicate maps to
        // CONFLICT via UniqueConstraintGuard, never a read-then-write (Req 19.2). Parking/storage
        // assignment is gated on the Assign permission via IAuthorizationGuard with a same-community
        // check (Req 19.4); Vehicle/Pet are IUnitScoped so the resident self-scope filter bounds a
        // resident to their own unit's records (Req 19.5, 5.4). Mutations are audited. The full
        // parking module (one active assignment per slot, violations, EV) is Phase 6 (Task 24).
        services.AddScoped<IResidentAssetsService, ResidentAssetsService>();

        // Resident communication preferences (Task 12.6, Req 20.1-20.4). The management service
        // stores/reads a resident's per-category opt-in (categories validated against the
        // configurable Notification_Category master-data list; mutations audited). The REAL
        // ICommunicationPreferences implementation replaces the DefaultCommunicationPreferences seam:
        // it is registered here with Add (not TryAdd) and BEFORE the Notification_Service block's
        // TryAddScoped default below, so the real per-resident store already occupies the seam and the
        // default is skipped. The Notification_Service consults it for OPTIONAL categories only
        // (Req 20.3) — mandatory account/security notices bypass preferences entirely in the service
        // (Req 20.4). It maps the notification's recipient userId → Resident (via Resident.UserId) →
        // the resident's preference row, falling back to the configured OptionalCategoriesAllowedByDefault
        // when there is no linked resident or no stored row (no hardcoding).
        services.AddScoped<ICommunicationPreferenceService, CommunicationPreferenceService>();
        services.AddScoped<ICommunicationPreferences, ResidentCommunicationPreferences>();

        // Gate module — visit passes + walk-ins (Task 16.1, Req 23.1-23.4, 24.1-24.5). A resident
        // creates a pre-approved pass for their OWN unit (self-scope + Create on VisitPass): the
        // service generates an opaque, community-unique QR token (DB Unique(CommunityId, QrToken) is
        // the source of truth) and a short OTP whose salted hash alone is stored (IPasswordHasher;
        // plaintext returned once). Gate verification by QR/OTP accepts a valid in-window pass with
        // uses remaining — incrementing UseCount under the pass RowVersion so two concurrent scans
        // can't over-use a single-use pass (Req 23.3) — and rejects expired/used/revoked/invalid with
        // the reason (Req 23.4). A guard registers a walk-in Visitor (Create on Visitor): category
        // validated against the configurable Visitor_Category list, status starts pending (entry
        // withheld, Req 24.5), and the host unit's residents are notified via INotificationService in
        // the SAME transaction (transactional outbox, Req 24.2). A resident with Approve approves/
        // rejects a pending walk-in for their own unit, recording approver + timestamp exactly once
        // under the visitor RowVersion (Req 24.3, 24.4). Validity window / max-uses default from
        // Config_Service (no hardcoding).
        //
        // Task 16.2 extends the SAME IGateService (one cohesive Gate seam) with
        // categories/recurring/entry-exit (Req 25.1-25.4): the configurable Visitor_Category list is
        // reused consistently by walk-ins AND recurring visitors; a resident with Create on
        // RecurringVisitor defines/revokes a recurring visitor for their OWN unit, whose stored
        // schedule (days-of-week bitmask + date range + optional daily window) authorizes entry on
        // matching dates without a new approval (RecurringVisitor.IsAuthorizedOn, Req 25.2); a guard
        // with Create on EntryExitEvent records an ENTRY — enforcing the 16.1 admittability rule so a
        // pending/rejected walk-in, an expired/used/revoked pass or an out-of-schedule recurring
        // visitor yields NO entry event (Req 24.4, 25.3) — and an EXIT matched to the OPEN event
        // (ExitTsUtc == null) via a single indexed query, stamped concurrency-safely under the event
        // RowVersion so a double-exit can't both win (Req 25.3). EntryExitEvent carries the scalable
        // (CommunityId, EntryTsUtc) index (Req 25.4). Watchlist/shifts/incidents/SOS and the HTTP
        // controllers (list endpoints) are Tasks 16.3-16.4.
        services.AddScoped<IGateService, GateService>();

        // Gate module — security operations (Task 16.3, Req 26.1-26.5, 27.1-27.5). The focused
        // ISecurityOpsService (kept separate from IGateService — a distinct security-ops concern with
        // its own permission resources) adds the blacklist/watchlist, guard-shift roster, incident
        // reporting + escalation, SOS workflow and the access-control integration hooks. Staff with
        // Create on WatchlistEntry add/clear entries (reads are View-gated, Req 26.3); the gate flow
        // (GateService) consults the efficient indexed MatchWatchlistAsync on every walk-in/entry and
        // requires the Approve override to admit a flagged subject (Req 26.5). Create on GuardShift
        // schedules a guard (a Phase 1 Guard-role User) to a gate + period (Req 26.4). Create on
        // Incident reports an incident; when its severity rank (the Incident_Severity list's SortOrder)
        // meets the configurable gate.incident.escalationThreshold the incident auto-escalates and the
        // responders (users whose roles grant View on Incident in the community — RBAC data, no
        // hardcoded recipients) are notified via Notification_Service in the SAME transaction
        // (Req 27.2); Edit acknowledges/resolves concurrency-safely under the incident RowVersion.
        // Create on SosAlert raises an SOS and notifies the configured audience with the MANDATORY
        // emergency template (always delivered regardless of opt-out, Req 20.4); it stays active until
        // an Edit user acknowledges + resolves it (Req 27.4). All mutations audited, tenant-scoped,
        // notifications transactional.
        services.AddScoped<ISecurityOpsService, SecurityOpsService>();

        // Parcel module — registration + status state machine (Task 18.1, Req 28.1-28.4, 29.3). A
        // guard with Create on Parcel registers a received parcel for a recipient unit in their
        // community: the recipient unit is resolved tenant-scoped (same-community by construction),
        // the category + condition validate against the configurable Parcel_Category / Parcel_Condition
        // lists, the status starts received, the optional photo/condition-evidence File_Service
        // references are recorded (Req 28.4) and the parcel is flagged priority when its category is
        // one of the configurable priority categories (parcel.priorityCategories, Req 29.3). A status
        // change (Edit on Parcel) is permitted only when the from->to pair is present in the editable
        // Parcel_Status_Transition allow-list — an illegal transition → VALIDATION_ERROR with nothing
        // mutated (Req 28.3), no hardcoded transition table — and records exactly once under the parcel
        // RowVersion (loser → CONFLICT centrally). Parcel is IUnitScoped so the resident self-scope
        // filter bounds a resident to parcels addressed to their own unit (Req 5.4, 28.5). All
        // mutations are audited. Task 18.2 adds custody-location assignment: a CustodyLocation master
        // row (Create gated on CustodyLocation) and moving a parcel to In Custody (Edit on Parcel)
        // records the assignment on the parcel and notifies the recipient unit's residents via
        // Notification_Service in the SAME transaction (EnqueueAsync only Adds the outbox row; the
        // single SaveChanges commits atomically) — with priority handling for priority-flagged parcels
        // (Req 29.2, 29.3). "One active custody location → one parcel" (Req 29.4) is a DB guarantee via
        // the filtered unique index UX_Parcel_CustodyLocationId_Active, so a racing duplicate
        // assignment → CONFLICT. Handover (18.3), the append-only timeline (18.4) and the HTTP
        // controllers (18.5) extend this same seam.
        services.AddScoped<IParcelService, ParcelService>();

        // Helpdesk module — ticket creation + classification + attachments (Task 20.1, Req 32.1-32.4).
        // A resident with Create on Ticket raises a ticket for their OWN unit: the host unit is
        // resolved tenant + resident-self scoped (so a non-own/cross-tenant unit → validation error,
        // same-community by construction, Req 32.1), the category/subcategory/priority validate against
        // the configurable Ticket_Category / Ticket_Subcategory / Ticket_Priority lists (an omitted
        // priority falls back to the configured community default), the status starts new, and each
        // supplied File_Service reference is stored as a TicketAttachment associated with the ticket
        // and its community (Create on TicketAttachment, Req 32.3) — the ticket + attachments + audit
        // commit in one transaction. Ticket and TicketAttachment are IUnitScoped so the resident
        // self-scope filter restricts a resident to tickets (and attachments) for their own household/
        // unit, while a staff View principal sees all tickets in their authorized communities
        // (Req 32.4, 5.4). All mutations are audited. Assignment/SLA (20.2), lifecycle/feedback/
        // analytics (20.3) and the HTTP controllers (21.4) extend this same seam.
        services.AddScoped<IHelpdeskService, HelpdeskService>();

        // Maintenance module — asset register with QR + lifecycle (Task 21.1, Req 35.1-35.4). A
        // Facility Manager with Create on Asset registers an asset for an authorized community: the
        // type/category validate against the configurable Asset_Type / Asset_Category lists (no frozen
        // enums), the serial number is unique within the community (DB Unique(CommunityId,
        // SerialNumber) is the source of truth — a duplicate → CONFLICT via UniqueConstraintGuard, no
        // read-then-write), the lifecycle status starts active, and an opaque, community-unique QR
        // token identifying the asset is generated (DB Unique(CommunityId, QrToken), regenerate-on-
        // collision — the same approach as the visit-pass / parcel QR token, Req 35.2). An Edit on
        // Asset updates the editable details and/or drives the lifecycle status (validated as an
        // active Asset_Status code; no transition allow-list for the asset lifecycle in 21.1),
        // recording exactly once under the asset RowVersion (a loser → CONFLICT centrally). Asset is a
        // TenantOwnedEntity but NOT IUnitScoped — community infrastructure, so reads are bounded to the
        // caller's authorized communities by the global tenant filter and the paginated/filter/sort/
        // search list (View on Asset) rides the shared allow-listed query config + AsNoTracking
        // (Req 35.3). A Delete on Asset is a Soft_Delete (Remove → IsDeleted via the audit
        // interceptor), so the archived asset stays traceable but is hidden from ordinary reads
        // (Req 35.4). All mutations are audited. Work orders (21.2), inspections/AMC/meters (21.3) and
        // the HTTP controllers (21.4) extend this same cohesive Maintenance seam.
        services.AddScoped<IMaintenanceService, MaintenanceService>();

        // Amenities module — amenity master + slots + rules + blackout dates (Task 23.1, Req
        // 38.1-38.4). The CONFIGURATION side of the Amenities area. A Facility Manager with Create on
        // Amenity defines a bookable amenity for an authorized community: the type/status validate
        // against the configurable Amenity_Type / Amenity_Status lists (no frozen enums), the code is
        // unique within the community (DB Unique(CommunityId, Code) is the source of truth — a
        // duplicate → CONFLICT via UniqueConstraintGuard, no read-then-write), and the status starts
        // active. Edit drives the editable details / lifecycle status (validated as an active status
        // code; no transition allow-list in 23.1); reads are tenant-scoped (Amenity is a
        // TenantOwnedEntity but NOT IUnitScoped — community infrastructure) and the paginated/filter/
        // sort/search list (View) rides the shared allow-listed query config + AsNoTracking; Delete is
        // a Soft_Delete. FacilitySlot (Create/Edit/View/Delete) stores a recurring daily window
        // (minutes-of-day + weekday bitmask) + capacity + the RowVersion the 23.2 booking lifecycle
        // books under. The per-amenity singleton BookingRule set is upserted via Amenity.Edit
        // (SetBookingRuleAsync, Unique(AmenityId) makes it race-safe): eligibility codes validate
        // against Resident_Type, numeric advance-window / per-resident-cap defaults resolve via
        // Config_Service, money fields are decimals — nothing hardcoded (Req 38.3). BlackoutDate
        // (Create/View/Delete) stores a half-open UTC range that makes the amenity unavailable;
        // overlapping ranges are allowed (Req 38.4). All mutations are audited; RowVersion gives
        // concurrency safety on every edit/delete. The booking lifecycle with capacity concurrency
        // (23.2) and the HTTP controllers (24.3) extend this same cohesive seam. Resident events
        // (Task 24.2, Req 41.4, 41.5) extend it too: PublishResidentEvent stores an event with its
        // schedule + capacity; RegisterForResidentEvent claims a seat with the SAME atomic guarded
        // counter as SlotBookingCount (SET RegisteredCount = RegisteredCount + 1 WHERE RegisteredCount
        // < Capacity) so registrations are rejected-at-capacity concurrency-safely, and the
        // Unique(ResidentEventId, ResidentId) index blocks a duplicate registration; cancellation
        // releases the seat exactly once (guarded decrement under RowVersion + IsCancelled). All
        // mutations are audited and notify via the transactional outbox.
        services.AddScoped<IAmenityService, AmenityService>();

        // Parking module — layout + assignment + violations + occupancy (Task 24.1, Req 40.1-40.5).
        // Built on the minimal Phase 2 ParkingSlot association (Task 12.5), which it EVOLVES (zone/
        // level/number/type/active layout columns + the authoritative ParkingAssignment register)
        // rather than duplicates. CreateZone/CreateSlot define the layout — types validate against
        // the configurable Parking_Zone_Type / Parking_Slot_Type lists (no frozen enums), codes are
        // unique within the community (DB Unique(CommunityId, Code) → CONFLICT via
        // UniqueConstraintGuard, no read-then-write). AssignSlot closes the slot's prior ACTIVE
        // ParkingAssignment and appends the new one (permanent/temporary/visitor) in ONE transaction;
        // the DB-level filtered unique index on (ParkingSlotId) WHERE EndedAtUtc IS NULL makes two
        // concurrent assignments of the SAME slot resolve to exactly one winner (the loser → CONFLICT)
        // — the one-active-per-slot headline invariant (Req 40.3), the same "let the DB decide the
        // winner" approach as OwnershipHistory / CustodyLocation. ReleaseSlot closes the active row.
        // RecordViolation stores a violation with its slot/vehicle/type/description + an optional
        // File_Service evidence reference, resolves the responsible unit from the slot's active
        // assignment, and notifies that unit's residents via the transactional outbox (Req 40.4).
        // GetOccupancy computes free/occupied counts (overall + per zone/level) in the DATABASE via a
        // grouped projection over active slots + active assignments — no in-memory scan — scoped to
        // the caller's authorized communities (Req 40.5). All mutations are audited; RowVersion gives
        // concurrency safety on every edit/close. Guest parking + EV reservations (Task 24.2, Req
        // 41.1-41.3) extend this cohesive seam: RequestGuestParking appends a visitor-type
        // ParkingAssignment for an APPROVED visitor on the SAME one-active-per-slot register (reusing
        // the Req 40.3 DB-level guarantee — no second uniqueness mechanism), time-bounded + per-unit-
        // capped via Config_Service. ReserveEvSlot creates an EvReservation with its OWN filtered
        // unique index on (ParkingSlotId) WHERE EndedAtUtc IS NULL, so two concurrent reservations of
        // the same EV slot resolve to exactly one winner (the loser → CONFLICT, Req 41.3) — the
        // identical "let the DB decide the winner" approach; CloseEvReservation closes the active row
        // (frees the slot) under RowVersion. Both notify the resident via the transactional outbox and
        // are audited. The HTTP controllers (24.3) extend this seam.
        services.AddScoped<IParkingService, ParkingService>();

        // Community notice board (MVP notices slice). The read-only Announcement_Service lists a
        // community's announcements (newest-first) and fetches one, gated on View on Announcement and
        // tenant-scoped by the global query filter — a resident only sees the board of a community
        // they belong to. Read-only in the MVP; the board is populated by the dev seed (and a future
        // admin publish task), not a resident write.
        services.AddScoped<IAnnouncementService, AnnouncementService>();

        // Access-control hardware seam (Req 27.5). The no-op/logging default records that an action
        // WOULD have been triggered and reports a non-fatal outcome, so the Gate_Service writes a
        // complete AccessIntegrationEvent audit row even without a device attached — exactly like the
        // logging OTP deliverer / OneSignal client seams. The REAL CCTV / boom-barrier / access-control
        // drivers are the Phase 12 integrations module (Task 40); swapping one in is a one-line DI
        // change and the Gate_Service does not change. TryAdd so a host registering a real driver
        // first always wins.
        services.TryAddScoped<IAccessControlIntegration, LoggingAccessControlIntegration>();

        // Authorization_Service (Task 5.3): the REAL API-level permission gate. A single
        // request-scoped instance backs both the endpoint gate (IPermissionEvaluator, consumed by
        // the pipeline middleware) and the reusable resource/export guard (IAuthorizationGuard,
        // consumed by module services), so the effective-permission set is loaded once per request
        // and memoized across every check. Registered with Add (not TryAdd) and ahead of the
        // AllowAuthenticated placeholder — the placeholder is TryAdd'd later in
        // AddRequestPipelineSecurity, so this real evaluator already occupies IPermissionEvaluator
        // and the placeholder is skipped (Req 4.1-4.4, 5.3).
        services.AddScoped<AuthorizationService>();
        services.AddScoped<IPermissionEvaluator>(sp => sp.GetRequiredService<AuthorizationService>());
        services.AddScoped<IAuthorizationGuard>(sp => sp.GetRequiredService<AuthorizationService>());

        // Auth_Service (Task 5.2): login/logout, OTP reset, lockout, hashing + token issuance.
        // Thresholds/expiry/lifetime come from the "Auth" section (options), overridable per-community
        // through Config_Service (no hardcoding, Req 1.6/2.2). Token issuance binds the SAME "Jwt"
        // section the API validates, so issuer/audience/key agree on both sides.
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<JwtIssuerOptions>()
            .Bind(configuration.GetSection(JwtIssuerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // File_Service (Task 6.1): secure non-public storage + authorized streaming. Max size,
        // content-type allow-list and the storage root all come from the "FileStorage" section
        // (options — no hardcoding, Req 7.3/7.4). The local blob store writes outside the webroot;
        // IFileBlobStore is the seam a cloud store (S3/Azure Blob) replaces later without touching
        // callers. FileService layers validation + View/tenant checks + upload audit on top.
        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IFileBlobStore, LocalFileBlobStore>();
        services.AddScoped<IFileService, FileService>();

        // Notification_Service (Task 7.1): named templates + transactional outbox WRITE. Tunables
        // (OneSignal app id/key, retry policy, default opt-in) bind from the "Notification" section
        // (options — no hardcoding; secrets come from configuration/user-secrets, never source).
        // EnqueueAsync resolves a template, applies the resident's preference, renders it and Adds a
        // Pending outbox row to the request DbContext WITHOUT saving, so it commits with the caller's
        // business transaction (Req 11.5). It does NOT call OneSignal — the Task 7.2 worker drains
        // the outbox via INotificationDispatcher/IOneSignalClient (defined here, implemented in 7.2).
        services.AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection(NotificationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<INotificationTemplateRenderer, PlaceholderTemplateRenderer>();
        services.AddScoped<INotificationRecipientResolver, DeviceRecipientResolver>();
        // ICommunicationPreferences fallback seam (allow optional by default / mandatory always on,
        // Req 20.3/20.4). TryAdd so Phase 2 Task 12.6's real per-resident store wins: that store is
        // registered with AddScoped in the Resident module block ABOVE, so by the time this line runs
        // the seam is already occupied and this default is skipped. The default remains only for hosts
        // that compose the data layer without the resident module.
        services.TryAddScoped<ICommunicationPreferences, DefaultCommunicationPreferences>();
        services.AddScoped<INotificationService, NotificationService>();
        // In-app notification inbox read side (Task 20261008): reads the recipient's own outbox rows
        // (written by the common EnqueueAsync helper) + flips their read flag. No per-feature save.
        services.AddScoped<CommunityOS.Application.Notifications.INotificationInboxService, Notifications.NotificationInboxService>();

        // Notification dispatch (Task 7.2): the OneSignal push client + the dispatcher the worker
        // calls. IOneSignalClient is a TYPED HttpClient (IHttpClientFactory) so connections are
        // pooled — never a per-request `new HttpClient()` (Req 65.4). BaseAddress + timeout are
        // config-driven defaults (no hardcoded per-call URL/timeout logic). The app id / API key are
        // read from NotificationOptions at send time, never baked into the handler. These are safe to
        // register in every host; only the worker actually drains the outbox and dispatches.
        services.AddOptions<NotificationWorkerOptions>()
            .Bind(configuration.GetSection(NotificationWorkerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHttpClient<IOneSignalClient, OneSignalHttpClient>((sp, client) =>
        {
            var notif = sp.GetRequiredService<IOptions<NotificationOptions>>().Value;
            client.BaseAddress = new Uri(notif.OneSignalApiBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(notif.HttpTimeoutSeconds);
        });
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        // Idempotency-key store (Task 8.2): the reusable at-most-once primitive payments (26.x) and
        // parcel handover (18.x) wrap their handlers in later (Req 30.5, 44.2, 63.3). Retention /
        // replay-wait tunables bind from the "Idempotency" section (options — no hardcoding). The
        // service reserves the key via a Unique(KeyValue, Endpoint)-guarded insert BEFORE executing,
        // so at-most-once is a DB guarantee, not an application read-then-write check. Scoped so it
        // uses the per-request DbContext.
        services.AddOptions<IdempotencyOptions>()
            .Bind(configuration.GetSection(IdempotencyOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<IIdempotencyService, IdempotencyService>();

        services.AddSingleton<IPasswordHasher, PasswordHasherAdapter>();
        services.AddSingleton<IAuthTokenIssuer, JwtAuthTokenIssuer>();
        services.AddScoped<ISessionRevocationStore, SessionRevocationStore>();
        services.TryAddScoped<IOtpDeliverer, LoggingOtpDeliverer>(); // Notification_Service (Task 7.1) replaces later.
        services.AddScoped<IAuthService, AuthService>();

        // Public self-service onboarding (create your own community + admin). Scoped: it uses the
        // request-scoped IAuthService to log the new admin in after provisioning.
        services.AddScoped<CommunityOS.Application.Onboarding.IOnboardingService, CommunityOS.Infrastructure.Onboarding.OnboardingService>();

        // Resident self-onboarding + admin approval (mobile login/registration flow).
        services.AddScoped<CommunityOS.Application.Onboarding.IResidentOnboardingService, CommunityOS.Infrastructure.Onboarding.ResidentOnboardingService>();

        // Fail-closed default; the request pipeline (Task 2.2) replaces this with a principal-derived
        // context. TryAdd so a host registering the real one first always wins.
        services.TryAddScoped<ITenantContext, NullTenantContext>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            if (configureProvider is not null)
            {
                configureProvider(options);
            }
            else
            {
                var dbOptions = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
                options.UseSqlServer(dbOptions.ConnectionString, sql =>
                    sql.CommandTimeout(dbOptions.CommandTimeoutSeconds));
            }

            options.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        return services;
    }
}
