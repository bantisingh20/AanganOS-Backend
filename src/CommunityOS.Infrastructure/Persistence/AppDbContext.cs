using System.Reflection;
using CommunityOS.Application.Abstractions;
using CommunityOS.Application.Exceptions;
using CommunityOS.Domain.Audit;
using CommunityOS.Domain.Common;
using CommunityOS.Domain.Community;
using CommunityOS.Domain.Config;
using CommunityOS.Domain.Files;
using CommunityOS.Domain.Gate;
using CommunityOS.Domain.Helpdesk;
using CommunityOS.Domain.Idempotency;
using CommunityOS.Domain.Identity;
using CommunityOS.Domain.Maintenance;
using CommunityOS.Domain.Notifications;
using CommunityOS.Domain.Residents;
using CommunityOS.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CommunityOS.Infrastructure.Persistence;

/// <summary>
/// Primary EF Core context. Enforces multi-tenant isolation at the data layer via global
/// query filters on every <see cref="TenantOwnedEntity"/> (Req 5.1–5.5), maps the
/// <see cref="TenantOwnedEntity.RowVersion"/>/<see cref="PlatformEntity.RowVersion"/>
/// optimistic-concurrency token (Req 22.4), and soft-deletes platform-scoped entities too.
///
/// Global filters reference the injected <see cref="ITenantContext"/> through <b>instance
/// members of this context</b> (not a captured constant). EF Core recognises DbContext
/// instance access inside a query filter and re-evaluates it per query against the live
/// per-request context, so the cached model stays correct even though every scoped request
/// gets a different <see cref="ITenantContext"/>. Baking the context in with
/// <c>Expression.Constant</c> would freeze the first request's tenant into the cached model
/// and leak data across tenants.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    private readonly ITenantContext _tenant = tenant;

    public DbSet<SampleTenantRecord> SampleTenantRecords => Set<SampleTenantRecord>();

    // Config/master-data store (Task 3.1). Both are platform-scoped entities (see the modeling
    // note on Configuration) so the tenant global filter never hides platform-default rows.
    public DbSet<Configuration> Configurations => Set<Configuration>();
    public DbSet<MasterDataItem> MasterDataItems => Set<MasterDataItem>();

    // Append-only audit trail (Task 4.1). Deliberately NOT a TenantOwnedEntity/PlatformEntity, so
    // the OnModelCreating loop below skips it: no global tenant filter (its CommunityId is nullable
    // and tenant scoping is enforced in the Audit_Service read path, Req 6.5) and no soft-delete /
    // rowversion (rows are immutable and never deleted, Req 6.3). The AuditSaveChangesInterceptor
    // rejects any Modified/Deleted state on this type to enforce append-only at the data layer.
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Identity / RBAC store (Task 5.1). User, Role and Permission are platform-scoped entities (a
    // user is not owned by one community — its communities come from UserRole scope); UserRole is
    // platform-scoped and soft-deletable so a revoke is traceable (Req 3.4, 3.7). RolePermission is
    // a plain composite-key join table (no base, so no global filter). The OnModelCreating loop
    // applies the soft-delete filter to the PlatformEntity-derived types automatically.
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();

    // Auth_Service store (Task 5.2). LoginSession and OtpToken are platform-scoped entities (they
    // belong to a user, not one community), so the OnModelCreating loop applies the soft-delete
    // filter and the concurrency token automatically — no tenant predicate.
    public DbSet<LoginSession> LoginSessions => Set<LoginSession>();
    public DbSet<OtpToken> OtpTokens => Set<OtpToken>();

    // File_Service metadata store (Task 6.1). StoredFile is a TenantOwnedEntity, so the
    // OnModelCreating loop applies the tenant + soft-delete global filter and the concurrency token
    // automatically — a direct id lookup can never return a foreign-tenant/deleted file (Req 7.2).
    // The file bytes live in the blob store (outside the webroot), not in this table (Req 7.4).
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();

    // Notification_Service store (Task 7.1). NotificationTemplate and Device are platform-scoped
    // entities (a template falls back platform→community like Configuration; a device belongs to a
    // user, not one community), so the OnModelCreating loop applies the soft-delete filter and
    // concurrency token automatically — no tenant predicate. NotificationOutboxMessage is a
    // TenantOwnedEntity: the transactional-outbox row is written inside the business transaction
    // (Req 11.5) and carries its originating CommunityId, so the tenant + soft-delete global filter
    // applies automatically. The Task 7.2 worker drains it under a cross-tenant (super-admin) context.
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationOutboxMessage> NotificationOutbox => Set<NotificationOutboxMessage>();
    public DbSet<Device> Devices => Set<Device>();

    // Idempotency-key store (Task 8.2). IdempotencyKey derives from no base, so the OnModelCreating
    // loop applies no tenant/soft-delete filter or rowversion to it — it is immutable at-most-once
    // bookkeeping. Its Unique(KeyValue, Endpoint) index (declared in the entity configuration) is
    // what makes the reserve-before-execute insert in IdempotencyService race-safe, giving
    // at-most-once execution for payments (26.x) / parcel handover (18.x) later (Req 30.5, 44.2, 63.3).
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    // Community / Property hierarchy store (Task 11.1). Organization is a PlatformEntity (gets only
    // the soft-delete filter). Community and HierarchyNode are TenantOwnedEntity, so the
    // OnModelCreating loop applies the tenant + soft-delete global filter and the concurrency token
    // automatically. A Community row sets its own CommunityId == Id on create, so the standard
    // tenant filter scopes it to the owning tenant with no bespoke predicate (Req 12.1, 12.5, 13.x).
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Domain.Community.Community> Communities => Set<Domain.Community.Community>();
    public DbSet<HierarchyNode> HierarchyNodes => Set<HierarchyNode>();

    // Community notice board (MVP notices slice). Announcement is a TenantOwnedEntity but NOT
    // IUnitScoped — a notice is community-wide (same stance as Amenity/Asset), so the OnModelCreating
    // loop applies the tenant + soft-delete global filter and the rowversion token automatically (no
    // resident self-scope): every resident in the community reads the same board, a soft-deleted
    // notice is hidden from ordinary reads. Its (CommunityId, PublishedAtUtc) index (in
    // AnnouncementEntityConfiguration) backs the newest-first board list as a single indexed read.
    public DbSet<Announcement> Announcements => Set<Announcement>();

    // Unit management (Task 11.2). Unit is a TenantOwnedEntity, so the OnModelCreating loop applies
    // the tenant + soft-delete global filter and the rowversion concurrency token automatically. Its
    // Unique(CommunityId, UnitNumber) index (declared in UnitEntityConfiguration) is the DB-level,
    // concurrency-safe source of truth for unit-number uniqueness (Req 14.4, 22.1); custom attributes
    // are persisted as a JSON column via a value converter (Req 14.1).
    public DbSet<Unit> Units => Set<Unit>();

    // Resident foundation (Task 12.1). Resident is a TenantOwnedEntity (tenant + soft-delete global
    // filter + rowversion applied by the OnModelCreating loop); it is NOT IUnitScoped — a person is
    // not pinned to one unit. HouseholdMember IS IUnitScoped (it carries a UnitId), so the loop wires
    // the resident self-scope filter onto it: a resident-self-scoped principal only sees their own
    // household rows (Req 5.4, 66.3). EmergencyContact is a tenant-owned one-to-many child of a
    // Resident (Req 15.3). All three get the (CommunityId, ...) indexes declared in their configs.
    public DbSet<Resident> Residents => Set<Resident>();
    public DbSet<HouseholdMember> HouseholdMembers => Set<HouseholdMember>();
    public DbSet<EmergencyContact> EmergencyContacts => Set<EmergencyContact>();

    // Resident communication preferences (Task 12.6). CommunicationPreference is a TenantOwnedEntity
    // (tenant + soft-delete global filter + rowversion applied by the OnModelCreating loop); it is
    // NOT IUnitScoped — a resident reaches their own preferences through their already-tenant-scoped
    // Resident row. Its Unique(ResidentId, Category) index (in the entity config) enforces one opt-in
    // row per category per resident (Req 20.1) and is the single indexed read the Notification_Service
    // preference check rides. The real ICommunicationPreferences implementation (Task 12.6) reads this
    // set to gate OPTIONAL categories; mandatory account/security notices bypass it entirely (Req 20.4).
    public DbSet<CommunicationPreference> CommunicationPreferences => Set<CommunicationPreference>();

    // Ownership / tenancy history (Task 12.2). Both are append-only TenantOwnedEntity registers of a
    // unit's associations over time: the OnModelCreating loop applies the tenant + soft-delete global
    // filter and rowversion automatically. They are NOT IUnitScoped — history is an admin register,
    // not resident self-view data. Assigning/transferring ownership closes the prior open owner row
    // and inserts the new one in one transaction, so a unit never has two open owners; on SQL Server
    // the filtered unique index added in OnModelCreating makes that a hard DB guarantee (Req 16.1-16.4).
    public DbSet<OwnershipHistory> OwnershipHistories => Set<OwnershipHistory>();
    public DbSet<TenancyHistory> TenancyHistories => Set<TenancyHistory>();

    // Resident assets (Task 12.5). Vehicle and Pet are TenantOwnedEntity AND IUnitScoped, so the
    // OnModelCreating loop wires the resident self-scope filter onto them: a resident principal only
    // sees their own unit's vehicles/pets (Req 19.5, 5.4). Vehicle's Unique(CommunityId,
    // RegistrationNumber) index (in its config) is the concurrency-safe source of truth for
    // registration uniqueness (Req 19.2). ParkingSlot and StorageLocation are tenant-owned master
    // rows (NOT IUnitScoped — community infrastructure, not resident self-view) carrying an optional
    // reassignable AssignedUnitId that the Assign operation sets (Req 19.4). The full parking module
    // (one active assignment per slot, violations, EV) is Phase 6 (Task 24).
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Pet> Pets => Set<Pet>();
    public DbSet<ParkingSlot> ParkingSlots => Set<ParkingSlot>();
    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();

    // Gate module — visit passes + walk-ins (Task 16.1). VisitPass and Visitor are TenantOwnedEntity
    // AND IUnitScoped (their HostUnitId backs the IUnitScoped UnitId), so the OnModelCreating loop
    // wires the resident self-scope filter onto them: a resident principal only sees passes/walk-ins
    // for their own host unit (Req 5.4, 23.5). VisitPass's Unique(CommunityId, QrToken) index (in its
    // config) is the opaque-token uniqueness guarantee and the indexed gate-verification lookup path
    // (Req 23.3); its inherited RowVersion makes the use-count increment concurrency-safe so a
    // single-use pass can't be admitted twice (Req 23.4). Visitor's RowVersion makes the resident
    // approve/reject decision record exactly once (Req 24.3); a pending walk-in is withheld from entry
    // (Req 24.5). Categories/recurring/entry-exit (Visitor_Category behaviour, EntryExitEvent) are
    // Task 16.2.
    public DbSet<VisitPass> VisitPasses => Set<VisitPass>();
    public DbSet<Visitor> Visitors => Set<Visitor>();

    // Gate module — categories / recurring visitors / entry-exit (Task 16.2). RecurringVisitor is a
    // TenantOwnedEntity AND IUnitScoped (its HostUnitId backs IUnitScoped UnitId), so the
    // OnModelCreating loop wires the resident self-scope filter onto it: a resident principal only
    // sees recurring visitors for their own host unit (the ones they created / may revoke, Req 25.2);
    // its RowVersion makes revoke concurrency-safe. EntryExitEvent is a TenantOwnedEntity but NOT
    // IUnitScoped — the gate entry/exit log is a security operations record scoped by community (the
    // tenant filter), not pinned to one unit for a resident. Its (CommunityId, EntryTsUtc) index
    // (in its config) is the scalable chronological log + the open-event exit-matching read path
    // (Req 25.3, 25.4); its RowVersion makes the exit stamp concurrency-safe so a double-exit can't
    // both win. Visitor_Category is the configurable master-data list both entities validate against.
    public DbSet<RecurringVisitor> RecurringVisitors => Set<RecurringVisitor>();
    public DbSet<EntryExitEvent> EntryExitEvents => Set<EntryExitEvent>();

    // Gate module — security operations (Task 16.3). WatchlistEntry, GuardShift, Incident, SosAlert
    // and AccessIntegrationEvent are TenantOwnedEntity but NOT IUnitScoped — community-wide security
    // operations records, so the OnModelCreating loop applies the tenant + soft-delete global filter
    // and the rowversion concurrency token automatically (no resident self-scope). WatchlistEntry's
    // (CommunityId, SubjectIdentifier) index (in its config) is the efficient gate-match lookup path
    // (Req 26.5 — flag a presented subject with a single indexed equality read, no scan). The
    // Incident/SosAlert RowVersion makes the acknowledge/resolve transitions record exactly once
    // under concurrency (Req 27.1, 27.4). AccessIntegrationEvent is an append-only record of each
    // triggered CCTV/boom-barrier/access-control hook (Req 27.5); the real hardware drivers are
    // Phase 12 (Task 40). GuardShift references the guard User (a Phase 1 Guard-role user), not a
    // duplicated profile (Req 26.2/26.4). The HTTP controllers + list endpoints are Task 16.4.
    public DbSet<WatchlistEntry> WatchlistEntries => Set<WatchlistEntry>();
    public DbSet<GuardShift> GuardShifts => Set<GuardShift>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<SosAlert> SosAlerts => Set<SosAlert>();
    public DbSet<AccessIntegrationEvent> AccessIntegrationEvents => Set<AccessIntegrationEvent>();

    // Parcel module — registration + status lifecycle (Task 18.1, Req 28.1-28.4, 29.3). Parcel is a
    // TenantOwnedEntity AND IUnitScoped (its RecipientUnitId backs the IUnitScoped UnitId), so the
    // OnModelCreating loop wires the resident self-scope filter onto it: a resident principal only
    // sees parcels addressed to their own unit (Req 5.4, 28.5). Its inherited RowVersion makes a
    // status transition record exactly once under concurrency (design "RowVersion on Parcel status").
    // Status/category/condition are stable codes validated against the configurable Parcel_Status /
    // Parcel_Category / Parcel_Condition master-data lists, and the ALLOWED transitions are themselves
    // editable master data (Parcel_Status_Transition) so an illegal transition is rejected without a
    // hardcoded table (Req 28.3). Custody location (18.2), handover (18.3), the append-only timeline
    // (18.4) and the controllers (18.5) extend this registration surface.
    public DbSet<Domain.Parcel.Parcel> Parcels => Set<Domain.Parcel.Parcel>();

    // Parcel module — custody locations (Task 18.2, Req 29.1). CustodyLocation is a TenantOwnedEntity
    // (tenant + soft-delete global filter + rowversion applied by the OnModelCreating loop); NOT
    // IUnitScoped — a secure bin is community infrastructure, not resident self-view data (same stance
    // as ParkingSlot/StorageLocation). Its Unique(CommunityId, Code) index (in its config) keeps codes
    // distinct within a community. The "one active custody location → one parcel" invariant (Req 29.4)
    // lives on the Parcel side as the filtered unique index on (CustodyLocationId) WHERE
    // CustodyLocationId IS NOT NULL added below on SQL Server — the same "let the DB decide the winner"
    // approach as the one-open-owner OwnershipHistory index.
    public DbSet<Domain.Parcel.CustodyLocation> CustodyLocations => Set<Domain.Parcel.CustodyLocation>();

    // Parcel module — authorized collector + verified handover (Task 18.3, Req 30.1-30.5).
    // ParcelAuthorization and ParcelHandover are TenantOwnedEntity AND IUnitScoped (their UnitId is
    // the authorized/handed-over parcel's recipient unit), so the OnModelCreating loop wires the
    // resident self-scope filter onto them: a resident principal only sees authorizations/handovers
    // for their own unit (Req 5.4). A resident creates a ParcelAuthorization for a parcel addressed
    // to their own unit, naming the authorized collector and carrying an opaque QR token + a salted
    // OTP hash (reused verbatim from VisitPass, plaintext returned once); its RowVersion makes
    // consuming it on handover record exactly once. ParcelHandover is written only on a SUCCESSFUL
    // verification; its Unique(ParcelId) index (in its config) makes "a parcel is handed over at most
    // once" a DB guarantee alongside the Idempotency-Key at-most-once wrapper (Req 30.4, 30.5). A
    // failed verification writes neither a handover row nor any status change (Req 30.3). The HTTP
    // controller wiring the Idempotency-Key header onto PerformHandover is Task 18.5.
    public DbSet<Domain.Parcel.ParcelAuthorization> ParcelAuthorizations => Set<Domain.Parcel.ParcelAuthorization>();
    public DbSet<Domain.Parcel.ParcelHandover> ParcelHandovers => Set<Domain.Parcel.ParcelHandover>();

    // Parcel module — append-only custody timeline + SLA/lost-damaged/returned (Task 18.4, Req
    // 31.1-31.5). CustodyTimelineEntry is a TenantOwnedEntity but NOT IUnitScoped — an audit-style
    // register read with the CustodyTimelineEntry View permission, so the OnModelCreating loop applies
    // the tenant + soft-delete global filter and the rowversion automatically (no resident self-scope,
    // same stance as OwnershipHistory/EntryExitEvent). It is append-only: every parcel event (register,
    // in-custody, custody-location assignment, handover, lost/damaged, returned, SLA escalation)
    // INSERTS a row and no application path updates/deletes one — the AuditSaveChangesInterceptor
    // rejects any Modified/Deleted CustodyTimelineEntry (the same guard it applies to AuditLog). Its
    // (CommunityId, ParcelId, OccurredAtUtc) index (in its config) is the chronological per-parcel read
    // path (Req 31.5). The HTTP controllers (GET /parcels/{id}/custody-timeline) are Task 18.5.
    public DbSet<Domain.Parcel.CustodyTimelineEntry> CustodyTimelineEntries => Set<Domain.Parcel.CustodyTimelineEntry>();

    // Helpdesk module — ticket creation + classification + attachments (Task 20.1, Req 32.1-32.4).
    // Ticket is a TenantOwnedEntity AND IUnitScoped (its HostUnitId backs the IUnitScoped UnitId), so
    // the OnModelCreating loop wires the resident self-scope filter onto it: a resident principal only
    // sees tickets for their own household/unit (Req 32.4, 5.4), while a staff user with View sees all
    // tickets in their authorized communities (the tenant filter). Its inherited RowVersion makes the
    // Task 20.2/20.3 assignment + status transitions record exactly once under concurrency.
    // Category/subcategory/priority/status are stable codes validated against the configurable
    // Ticket_Category / Ticket_Subcategory / Ticket_Priority / Ticket_Status master-data lists, not
    // frozen enums (Req 32.2). TicketAttachment is a TenantOwnedEntity AND IUnitScoped (its UnitId is
    // the parent ticket's host unit), so the loop wires the same resident self-scope filter onto it —
    // a resident only sees attachments on their own unit's tickets. Each attachment references a
    // File_Service StoredFile by FileId (the opaque-reference pattern); the bytes live in the blob
    // store and are streamed only through the authorized File_Service download path (Req 32.3, 7.2).
    // Assignment/SLA (20.2), lifecycle/feedback/analytics (20.3) and the controllers (21.4) extend
    // this same registration surface.
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketAttachment> TicketAttachments => Set<TicketAttachment>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();

    // Maintenance module — asset register (Task 21.1, Req 35.1-35.4). Asset is a TenantOwnedEntity
    // but NOT IUnitScoped — it is community infrastructure (a lift, pump, DG set, ...), so the
    // OnModelCreating loop applies the tenant + soft-delete global filter and the rowversion
    // concurrency token automatically (no resident self-scope, same stance as CustodyLocation /
    // ParkingSlot / StorageLocation), even when its optional UnitId ties it to a unit. Status/type/
    // category are stable codes validated against the configurable Asset_Status / Asset_Type /
    // Asset_Category master-data lists, not frozen enums (Req 35.1). Its Unique(CommunityId, QrToken)
    // index (in AssetEntityConfiguration) is the community-unique scannable-token guarantee + the
    // QR-scan lookup path (Req 35.2); its Unique(CommunityId, SerialNumber) index is the DB-level
    // serial-number uniqueness source of truth. A soft-deleted asset stays in the table and remains
    // traceable but is hidden from ordinary reads (Req 35.4). Work orders (21.2) and inspections/AMC/
    // meters (21.3) extend the Maintenance seam with their own entities.
    public DbSet<Asset> Assets => Set<Asset>();

    // Maintenance module — preventive/corrective work orders with parts/labour (Task 21.2, Req
    // 36.1-36.5). MaintenancePlan, WorkOrder, WorkOrderPart and WorkOrderLabour are TenantOwnedEntity
    // but NOT IUnitScoped — community infrastructure (same stance as Asset), so the OnModelCreating
    // loop applies the tenant + soft-delete global filter and the rowversion concurrency token
    // automatically (no resident self-scope). A MaintenancePlan is a preventive schedule for an asset
    // whose configurable frequency/interval drives NextDueAtUtc; the background preventive sweep
    // generates a WorkOrder when a plan is due and advances NextDueAtUtc under the plan RowVersion, so
    // a plan can never double-generate for the same occurrence under concurrency (a losing concurrent
    // sweep → CONFLICT, skipped). A WorkOrder is preventive (plan-generated) or corrective (raised
    // directly); type/priority/status are stable codes validated against the configurable
    // Work_Order_Type / Work_Order_Priority / Work_Order_Status lists, and the ALLOWED transitions are
    // themselves editable master data (Work_Order_Status_Transition) so an illegal transition is
    // rejected without a hardcoded table (mirrors Ticket/Parcel). Assignment records the assignee +
    // notifies the staff member (work-order.assigned push via the outbox); its RowVersion makes the
    // assignment / status transitions record exactly once. WorkOrderPart/WorkOrderLabour are the
    // parts/labour children carrying their parent's CommunityId, with decimal(18,2) cost precision.
    // EquipmentHistoryEntry is a TenantOwnedEntity but NOT IUnitScoped — an append-only audit-style
    // register (same stance as CustodyTimelineEntry): on a work order's completion the service INSERTS
    // one entry and no application path updates/deletes one — the AuditSaveChangesInterceptor rejects
    // any Modified/Deleted EquipmentHistoryEntry (the same guard it applies to AuditLog /
    // CustodyTimelineEntry). Its (CommunityId, AssetId, OccurredAtUtc) index is the chronological
    // per-asset read path (Req 36.5). Inspections/AMC/meters (21.3) and the HTTP controllers (21.4)
    // extend the Maintenance seam in later tasks.
    public DbSet<MaintenancePlan> MaintenancePlans => Set<MaintenancePlan>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderPart> WorkOrderParts => Set<WorkOrderPart>();
    public DbSet<WorkOrderLabour> WorkOrderLabour => Set<WorkOrderLabour>();
    public DbSet<EquipmentHistoryEntry> EquipmentHistoryEntries => Set<EquipmentHistoryEntry>();

    // Maintenance module — inspections / AMC contracts / meter history (Task 21.3, Req 37.1-37.5).
    // InspectionChecklist (+ its owned InspectionChecklistItem children), AmcContract and MeterReading
    // are TenantOwnedEntity but NOT IUnitScoped — community infrastructure (same stance as Asset /
    // WorkOrder), so the OnModelCreating loop applies the tenant + soft-delete global filter and the
    // rowversion concurrency token automatically (no resident self-scope). Completing an inspection
    // and recording a meter reading each append a single EquipmentHistoryEntry so inspections / meter
    // readings flow into the SAME append-only chronological equipment history as work orders (Req
    // 37.1, 37.4, 37.5). A MeterReading is an equipment counter — the service enforces a non-decreasing
    // value per (asset, meter type) (Req 37.4). An AmcContract carries a configurable expiry-reminder
    // window: a bounded background sweep (SendDueAmcExpiryRemindersAsync, mirroring the ticket SLA
    // sweep) notifies the responsible user once per expiry window via the transactional outbox, using
    // the ExpiryReminderSentForEndDateUtc at-most-once marker + RowVersion so a contract is never
    // reminded twice for the same window (a losing concurrent sweep → CONFLICT, skipped, Req 37.3).
    public DbSet<InspectionChecklist> InspectionChecklists => Set<InspectionChecklist>();
    public DbSet<InspectionChecklistItem> InspectionChecklistItems => Set<InspectionChecklistItem>();
    public DbSet<AmcContract> AmcContracts => Set<AmcContract>();
    public DbSet<MeterReading> MeterReadings => Set<MeterReading>();

    // Amenities module — amenity master + slots + rules + blackout dates (Task 23.1, Req 38.1-38.4).
    // Amenity, FacilitySlot, BookingRule and BlackoutDate are TenantOwnedEntity but NOT IUnitScoped —
    // community infrastructure (same stance as Asset / its children), so the OnModelCreating loop
    // applies the tenant + soft-delete global filter and the rowversion concurrency token
    // automatically (no resident self-scope). An Amenity is the bookable master whose type/status are
    // stable codes validated against the configurable Amenity_Type / Amenity_Status lists (not frozen
    // enums); its Unique(CommunityId, Code) index (in AmenityEntityConfiguration) is the community-
    // unique code guarantee, so a duplicate → CONFLICT via UniqueConstraintGuard, no read-then-write.
    // FacilitySlot is a child recurring daily window (minutes-of-day + weekday bitmask) carrying its
    // parent's community; its inherited RowVersion is the concurrency token the 23.2 booking lifecycle
    // books capacity under (23.1 only stores the window + capacity, no BookedCount yet). BookingRule
    // is a per-amenity SINGLETON child — its Unique(AmenityId) index makes "one rule-set per amenity"
    // a DB guarantee behind the service's race-safe upsert; every value (eligibility codes validated
    // against Resident_Type, numeric windows/limits with Config_Service defaults, decimal money
    // fields) is stored configuration, nothing hardcoded (Req 38.3). BlackoutDate is a half-open UTC
    // range child that makes the amenity unavailable (Req 38.4); overlapping ranges are allowed (no
    // uniqueness), and its (CommunityId, AmenityId, StartUtc) index backs the lifecycle's overlap
    // check. The booking lifecycle with capacity concurrency (23.2) and the HTTP controllers (24.3)
    // extend the Amenities seam in later tasks.
    public DbSet<Domain.Amenities.Amenity> Amenities => Set<Domain.Amenities.Amenity>();
    public DbSet<Domain.Amenities.FacilitySlot> FacilitySlots => Set<Domain.Amenities.FacilitySlot>();
    public DbSet<Domain.Amenities.BookingRule> BookingRules => Set<Domain.Amenities.BookingRule>();
    public DbSet<Domain.Amenities.BlackoutDate> BlackoutDates => Set<Domain.Amenities.BlackoutDate>();

    // Booking lifecycle + capacity concurrency (Task 23.2, Req 39.1-39.5). Booking is a resident's
    // reservation of a FacilitySlot for a date: TenantOwnedEntity AND IUnitScoped (keyed on the
    // resident's host UnitId) so the resident self-scope filter restricts a resident to their OWN
    // bookings (book/cancel only your own), while a Facility-Manager with View/Approve sees all
    // bookings in their communities via the tenant filter. Its status is a stable Booking_Status code
    // and the ALLOWED transitions are editable master data (Booking_Status_Transition) so an illegal
    // move is rejected without a hardcoded table (mirrors Ticket/Parcel/WorkOrder). SlotBookingCount
    // is the per-(slot, date) seat ledger: its Unique(FacilitySlotId, BookingDate) index makes one
    // counter per slot per date a DB guarantee, and the lifecycle claims a seat with an ATOMIC guarded
    // UPDATE (SET BookedCount = BookedCount + 1 WHERE BookedCount < Capacity, via ExecuteUpdateAsync)
    // so two requests racing the last seat can never both succeed — the capacity concurrency guarantee
    // (Req 39.5) is a DB-level invariant, not a read-then-write check. NOT IUnitScoped (shared
    // capacity infrastructure). The HTTP controllers (24.3) extend this seam later.
    public DbSet<Domain.Amenities.Booking> Bookings => Set<Domain.Amenities.Booking>();
    public DbSet<Domain.Amenities.SlotBookingCount> SlotBookingCounts => Set<Domain.Amenities.SlotBookingCount>();

    // Resident events + registrations (Task 24.2, Req 41.4, 41.5). ResidentEvent is a resident-
    // published event (schedule + capacity): TenantOwnedEntity AND IUnitScoped (keyed on the
    // organiser's host UnitId) so the resident self-scope filter restricts a resident to managing
    // their OWN unit's events; its status is a stable Resident_Event_Status code. Capacity lives
    // inline — the registration flow claims a seat with the SAME atomic guarded UPDATE as
    // SlotBookingCount (SET RegisteredCount = RegisteredCount + 1 WHERE RegisteredCount < Capacity, via
    // ExecuteUpdateAsync) so two residents racing the last seat can never both register (reject-at-
    // capacity is a DB-level invariant, Req 41.5, not a read-then-write check). ResidentEventRegistration
    // is the per-resident seat record: TenantOwnedEntity AND IUnitScoped (a resident sees only their own
    // registrations); its Unique(ResidentEventId, ResidentId) index makes one-registration-per-resident
    // a DB guarantee, and its IsCancelled flag + RowVersion release a seat exactly once. The HTTP
    // controllers (24.3) extend this seam later.
    public DbSet<Domain.Amenities.ResidentEvent> ResidentEvents => Set<Domain.Amenities.ResidentEvent>();
    public DbSet<Domain.Amenities.ResidentEventRegistration> ResidentEventRegistrations => Set<Domain.Amenities.ResidentEventRegistration>();

    // Parking module — layout + assignment + violations (Task 24.1, Req 40.1-40.5). ParkingZone and
    // ParkingSlot are the layout master; ParkingAssignment is the append-only assignment register;
    // ParkingViolation is the operations record. All are TenantOwnedEntity but NOT IUnitScoped —
    // community infrastructure / operations records (same stance as Asset / CustodyLocation), so the
    // OnModelCreating loop applies the tenant + soft-delete global filter and the rowversion token
    // automatically (no resident self-scope). ParkingZone/ParkingSlot carry Unique(CommunityId, Code);
    // ParkingSlot is EVOLVED from the Phase 2 row with zone/level/number/type/active layout columns
    // and its (CommunityId, ZoneId, Level) index backs the DB-side occupancy projection (Req 40.5).
    // The one-active-assignment-per-slot invariant (Req 40.3) lives on ParkingAssignment as the
    // filtered unique index on (ParkingSlotId) WHERE EndedAtUtc IS NULL added below on SQL Server —
    // the same "let the DB decide the winner" approach as the one-open-owner OwnershipHistory index;
    // the service closes the prior active row + inserts the new one in one transaction so a loser
    // maps to CONFLICT, never a read-then-write. Guest parking / EV reservations (24.2) and the HTTP
    // controllers (24.3) extend this cohesive seam.
    public DbSet<Domain.Parking.ParkingZone> ParkingZones => Set<Domain.Parking.ParkingZone>();
    public DbSet<Domain.Parking.ParkingAssignment> ParkingAssignments => Set<Domain.Parking.ParkingAssignment>();
    public DbSet<Domain.Parking.ParkingViolation> ParkingViolations => Set<Domain.Parking.ParkingViolation>();

    // Parking module — EV-charging reservations (Task 24.2, Req 41.2, 41.3). EvReservation is the
    // append-only reservation register for EV-charging slots (SlotType "ev"): TenantOwnedEntity but
    // NOT IUnitScoped — an operations record of a shared charger's bookings read across the community
    // (same stance as ParkingAssignment), so the OnModelCreating loop applies the tenant + soft-delete
    // global filter and the rowversion token automatically. The one-active-reservation-per-EV-slot
    // invariant (Req 41.3) lives here as the filtered unique index on (ParkingSlotId) WHERE EndedAtUtc
    // IS NULL added below on SQL Server — the SAME "let the DB decide the winner" approach as the
    // one-active-assignment ParkingAssignment / one-open-owner OwnershipHistory indexes; two concurrent
    // reservations of the SAME EV slot race to it and exactly one wins (the loser → CONFLICT), never a
    // read-then-write. Guest parking (Req 41.1) does NOT get its own entity — it is a visitor-type
    // ParkingAssignment on the existing one-active-per-slot register. The HTTP controllers (24.3)
    // extend this seam later.
    public DbSet<Domain.Parking.EvReservation> EvReservations => Set<Domain.Parking.EvReservation>();

    // Members below are referenced *by name* inside the strongly-typed filter lambdas so EF
    // Core treats them as live per-request values. Kept internal so only the filter plumbing
    // (and tests, via InternalsVisibleTo if ever needed) touch them.
    private bool IsSuperAdmin => _tenant.IsPlatformSuperAdmin;
    private bool IsResidentSelfScoped => _tenant.IsResidentSelfScoped;
    private Guid? ResidentUnitId => _tenant.ResidentUnitId;

    private static readonly MethodInfo TenantFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo ResidentScopedFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyResidentScopedTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo PlatformFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyPlatformFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Per-entity mappings (unique indexes, column constraints, seed data) live in
        // IEntityTypeConfiguration classes in this assembly. Applied before the filter/rowversion
        // loop below so the entities are present in the model when the loop walks them.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // At-most-one-open-owner-per-unit as a hard DB guarantee on SQL Server (Req 16.3 invariant,
        // Task 12.2): a filtered unique index on (UnitId) WHERE EndDate IS NULL means two concurrent
        // ownership assignments race to the index and exactly one open owner survives — the same
        // "let the DB decide the winner" approach as the Unit-number unique index. Applied here (not
        // in the entity configuration) because the provider is only known on the live context: the
        // bracketed [EndDate] filter is SQL Server syntax, so it must be guarded by IsSqlServer() to
        // keep the provider-agnostic SQLite test path intact. On SQLite the invariant rests on the
        // service's single-transaction close+insert instead (ponytail: no filtered-index equivalent
        // wired for the test provider; see OwnershipHistoryEntityConfiguration).
        if (Database.IsSqlServer())
        {
            modelBuilder.Entity<OwnershipHistory>()
                .HasIndex(o => o.UnitId)
                .IsUnique()
                .HasFilter("[EndDate] IS NULL")
                .HasDatabaseName("UX_OwnershipHistory_UnitId_Open");

            // One-active-custody-location-per-parcel as a hard DB guarantee on SQL Server (Req 29.4,
            // Task 18.2): a filtered unique index on Parcel(CustodyLocationId) WHERE CustodyLocationId
            // IS NOT NULL means two concurrent assignments of the SAME custody location race to the
            // index and exactly one survives — the same "let the DB decide the winner" approach as the
            // one-open-owner index above (CustodyLocationId is nulled when a parcel leaves custody, so
            // a non-null value is the active assignment). Applied here (not in the entity config)
            // because the bracketed [CustodyLocationId] filter is SQL Server syntax, guarded by
            // IsSqlServer() to keep the provider-agnostic SQLite test path intact. On SQLite the
            // invariant rests on the service's single-transaction assignment instead (ponytail: no
            // filtered-index equivalent wired for the test provider; see ParcelEntityConfiguration).
            modelBuilder.Entity<Domain.Parcel.Parcel>()
                .HasIndex(p => p.CustodyLocationId)
                .IsUnique()
                .HasFilter("[CustodyLocationId] IS NOT NULL")
                .HasDatabaseName("UX_Parcel_CustodyLocationId_Active");

            // One-active-assignment-per-slot as a hard DB guarantee on SQL Server (Req 40.3, Task
            // 24.1 — the headline parking invariant): a filtered unique index on
            // ParkingAssignment(ParkingSlotId) WHERE EndedAtUtc IS NULL means two concurrent
            // assignments of the SAME slot race to the index and exactly one active assignment
            // survives — the same "let the DB decide the winner" approach as the one-open-owner /
            // one-active-custody-location indexes above (EndedAtUtc is stamped when an assignment is
            // superseded or the slot is released, so a null value is the active assignment). Applied
            // here (not in the entity config) because the bracketed [EndedAtUtc] filter is SQL Server
            // syntax, guarded by IsSqlServer() to keep the provider-agnostic SQLite test path intact.
            // On SQLite the invariant rests on the service's single-transaction close+insert instead
            // (ponytail: no filtered-index equivalent wired for the test provider; see
            // ParkingAssignmentEntityConfiguration).
            modelBuilder.Entity<Domain.Parking.ParkingAssignment>()
                .HasIndex(a => a.ParkingSlotId)
                .IsUnique()
                .HasFilter("[EndedAtUtc] IS NULL")
                .HasDatabaseName("UX_ParkingAssignment_ParkingSlotId_Active");

            // One-active-reservation-per-EV-slot as a hard DB guarantee on SQL Server (Req 41.3, Task
            // 24.2 — the EV concurrency headline): a filtered unique index on
            // EvReservation(ParkingSlotId) WHERE EndedAtUtc IS NULL means two concurrent reservations
            // of the SAME EV slot race to the index and exactly one active reservation survives — the
            // SAME "let the DB decide the winner" approach as the one-active-assignment ParkingAssignment
            // index above (EndedAtUtc is stamped when a reservation is released/completed, so a null
            // value is the active reservation). Applied here (not in the entity config) because the
            // bracketed [EndedAtUtc] filter is SQL Server syntax, guarded by IsSqlServer() to keep the
            // provider-agnostic SQLite test path intact. On SQLite the invariant rests on the service's
            // single-transaction insert instead (ponytail: no filtered-index equivalent wired for the
            // test provider; see EvReservationEntityConfiguration).
            modelBuilder.Entity<Domain.Parking.EvReservation>()
                .HasIndex(r => r.ParkingSlotId)
                .IsUnique()
                .HasFilter("[EndedAtUtc] IS NULL")
                .HasDatabaseName("UX_EvReservation_ParkingSlotId_Active");
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;

            // Optimistic concurrency token on both base types (Req 22.4).
            if (typeof(TenantOwnedEntity).IsAssignableFrom(clrType) ||
                typeof(PlatformEntity).IsAssignableFrom(clrType))
            {
                var rowVersion = modelBuilder.Entity(clrType)
                    .Property(nameof(TenantOwnedEntity.RowVersion));

                if (Database.IsSqlServer())
                {
                    // SQL Server generates & bumps an 8-byte rowversion automatically.
                    rowVersion.IsRowVersion();
                }
                else
                {
                    // Providers without a native rowversion (SQLite/LocalDB test fixtures): keep an
                    // app-managed concurrency token so inserts don't trip a NOT NULL on an
                    // unpopulated store-generated column, and concurrency checks still run.
                    rowVersion.IsConcurrencyToken();
                }
            }

            if (typeof(TenantOwnedEntity).IsAssignableFrom(clrType))
            {
                // Entities marked IUnitScoped also get resident self-scoping (Req 5.4);
                // all other tenant-owned entities get tenant + soft-delete filtering only.
                var hasUnitScope = typeof(IUnitScoped).IsAssignableFrom(clrType);

                var method = hasUnitScope ? ResidentScopedFilterMethod : TenantFilterMethod;
                method.MakeGenericMethod(clrType).Invoke(this, [modelBuilder]);
            }
            else if (typeof(PlatformEntity).IsAssignableFrom(clrType))
            {
                PlatformFilterMethod.MakeGenericMethod(clrType).Invoke(this, [modelBuilder]);
            }
        }
    }

    /// <summary>
    /// Central optimistic-concurrency mapping (Req 22.4): a <see cref="DbUpdateConcurrencyException"/>
    /// (RowVersion mismatch — another request updated or deleted the row first) is translated once,
    /// here at the single data-layer save chokepoint, into the application's
    /// <see cref="ConflictException"/> (CONFLICT / 409). This keeps the mapping in Infrastructure
    /// (EF never leaks into the API's exception middleware) and means every service gets correct 409
    /// behaviour on a lost update without each catching the raw EF exception. A unique-constraint
    /// violation is a <b>different</b> subclass (<c>DbUpdateException</c>, not the concurrency one)
    /// and is still translated at the call site by <see cref="UniqueConstraintGuard"/>, which can
    /// attach the entity-specific "duplicate X" message.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(
                "The record was modified by another request. Reload and try again.");
        }
    }

    /// <summary>
    /// <c>!IsDeleted AND (IsSuperAdmin OR AuthorizedCommunityIds.Contains(CommunityId))</c>
    /// — the baseline tenant-isolation + soft-delete filter (Req 5.2, 5.3, 5.5).
    /// </summary>
    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : TenantOwnedEntity =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            !e.IsDeleted &&
            (IsSuperAdmin || _tenant.AuthorizedCommunityIds.Contains(e.CommunityId)));

    /// <summary>
    /// Baseline tenant filter plus the resident self-scope hook (Req 5.4): when the request is
    /// resident-self-scoped, the row's <c>UnitId</c> must match the resident's own unit.
    /// Applied to tenant-owned entities that expose a nullable <c>Guid UnitId</c>.
    /// </summary>
    private void ApplyResidentScopedTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : TenantOwnedEntity, IUnitScoped =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(e =>
            !e.IsDeleted &&
            (IsSuperAdmin || _tenant.AuthorizedCommunityIds.Contains(e.CommunityId)) &&
            (!IsResidentSelfScoped || e.UnitId == ResidentUnitId));

    /// <summary>Platform-scoped entities: soft-delete exclusion only, no tenant predicate.</summary>
    private void ApplyPlatformFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : PlatformEntity =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(e => !e.IsDeleted);
}
