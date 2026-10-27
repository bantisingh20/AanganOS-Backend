using App.Application.Abstractions;
using App.Application.Abstractions.Security;
using App.Application.Audit;
using App.Application.Config;
using App.Application.Exceptions;
using App.Application.Notifications;
using App.Application.Querying;
using App.Application.Residents;
using App.Contracts.Envelope;
using App.Domain.Community;
using App.Domain.Identity;
using App.Domain.Residents;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Residents;

/// <summary>
/// EF-backed <see cref="IResidentService"/> (Req 15.1–15.3, 15.5).
///
/// <para><b>Configurable type (Req 15.1).</b> <c>ResidentType</c> is validated at runtime against
/// the editable <c>Resident_Type</c> master-data list resolved for the resident's own community (so
/// a per-community override applies) — not a frozen enum. Verification status is never caller-set:
/// it starts at <c>pending</c> (the Task 12.3 workflow owns transitions).</para>
///
/// <para><b>Same-community association (Req 15.2).</b> When associating a resident with a unit, the
/// unit's community is loaded (tenant-scoped read) and a cross-community link is rejected with a
/// validation error — the same convention UnitService/HierarchyService use for same-community
/// checks.</para>
///
/// <para><b>Resident self-view (Req 5.4, 66.3).</b> <see cref="HouseholdMember"/> is
/// <c>IUnitScoped</c>, so the DbContext resident self-scope filter restricts a resident principal to
/// their own household rows automatically — this service adds no bespoke predicate for that.</para>
///
/// <para><b>Audit + tenant scope.</b> Create/update/delete and the association/contact mutations each
/// append an <see cref="AuditEntry"/> (Req 6.1); reads are tenant-scoped by the global filter
/// (Req 5.2) and use <c>AsNoTracking</c>; delete is a soft delete that stays traceable (Req 15.5).
/// The optimistic-concurrency token mismatch on edit is mapped to CONFLICT centrally in
/// <c>AppDbContext.SaveChangesAsync</c> (Req 22.4), so there is no per-service catch here.</para>
///
/// <para><b>Verification/approval workflow (Task 12.3, Req 17.x, 22.3).</b> <see cref="ApproveAsync"/>,
/// <see cref="RejectAsync"/> and <see cref="ResubmitForVerificationAsync"/> drive the
/// <c>VerificationStatus</c> state machine (pending → verified/rejected; rejected → pending). Each is
/// gated on the <c>Approve</c> permission + authorized community via <see cref="IAuthorizationGuard"/>
/// (defense-in-depth on top of the Task 12.7 endpoint gate, Req 17.5), validates the transition (an
/// invalid one → CONFLICT), appends the audit record (approver/timestamp/reason, Req 17.2/17.3), and
/// enqueues the resident notification through <see cref="INotificationService"/> as a transactional
/// outbox row — all committed by a single <c>SaveChangesAsync</c> so the status change and the
/// notification are atomic (Req 17.4): a rollback leaves no notification. Approve-once under
/// concurrency (Req 22.3) rests on the resident's <c>RowVersion</c>: the first save bumps the token
/// and the concurrent loser's save trips the row-version mismatch mapped centrally to CONFLICT — a
/// DB-level guard, never a read-then-write pre-check.</para>
/// </summary>
public sealed class ResidentService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config,
    IClock clock,
    IPageSizePolicy pageSizePolicy,
    IAsyncQueryExecutor executor,
    IAuthorizationGuard authorizationGuard,
    INotificationService notifications,
    ITenantContext tenantContext,
    App.Application.Auth.IPasswordHasher passwordHasher)
    : IResidentService
{
    /// <summary>Default role assigned to a newly-created resident login when none is specified.</summary>
    private const string DefaultResidentRoleName = "Resident";
    // Allow-list for the resident list endpoint (Req 15.4). verificationStatus and residentType are
    // filterable+sortable so the "pending residents in this community" verification queue hits the
    // (CommunityId, VerificationStatus) index at scale. No arbitrary client fields reach the query
    // (steering: no hardcoding, injection-safe).
    private static readonly ListQueryConfiguration<Resident> QueryConfig =
        new ListQueryConfiguration<Resident>()
            .WithDefaultSort(r => r.Name)
            .Sortable("name", r => r.Name)
            .Sortable("residentType", r => r.ResidentType)
            .Sortable("verificationStatus", r => r.VerificationStatus)
            .Filterable("residentType", r => r.ResidentType)
            .Filterable("verificationStatus", r => r.VerificationStatus)
            .Filterable("userId", r => r.UserId)
            .Searchable(r => r.Name)
            .Searchable(r => r.Email)
            .Searchable(r => r.Phone);

    public async Task<ResidentDto> CreateAsync(CreateResidentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireName(request.Name);
        await RequireValidResidentTypeAsync(request.ResidentType, request.CommunityId, cancellationToken);

        // A caller who can approve residents (admin / PowerUser creating from the portal) produces a
        // pre-verified resident — no separate approval step. A self-registering resident (no Approve
        // grant) still starts pending and goes through the verification queue (Req 15.1, Task 12.3).
        var createdByApprover = await authorizationGuard.HasPermissionAsync(PermissionAction.Approve, "Resident", cancellationToken);

        var resident = new Resident
        {
            CommunityId = request.CommunityId,
            UserId = request.UserId,
            Name = request.Name.Trim(),
            Email = request.Email?.Trim(),
            Phone = request.Phone?.Trim(),
            ResidentType = request.ResidentType.Trim(),
            PhotoFileId = string.IsNullOrWhiteSpace(request.PhotoFileId) ? null : request.PhotoFileId.Trim(),
            VerificationStatus = createdByApprover
                ? ResidentVerificationStatuses.Verified
                : ResidentVerificationStatuses.Pending,
        };

        db.Residents.Add(resident);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Create", "Resident", resident.Id.ToString(), resident.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(resident);
    }

    public async Task<ResidentDto> UpdateAsync(Guid residentId, UpdateResidentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var resident = await db.Residents.FirstOrDefaultAsync(r => r.Id == residentId, cancellationToken)
            ?? throw new NotFoundException("Resident not found.");

        if (request.Name is not null)
        {
            RequireName(request.Name);
            resident.Name = request.Name.Trim();
        }

        if (request.ResidentType is not null)
        {
            await RequireValidResidentTypeAsync(request.ResidentType, resident.CommunityId, cancellationToken);
            resident.ResidentType = request.ResidentType.Trim();
        }

        if (request.UpdateEmail)
        {
            resident.Email = request.Email?.Trim();
        }

        if (request.UpdatePhone)
        {
            resident.Phone = request.Phone?.Trim();
        }

        if (request.UpdateUserId)
        {
            resident.UserId = request.UserId; // null => unlink
        }

        if (request.UpdatePhotoFileId)
        {
            resident.PhotoFileId = string.IsNullOrWhiteSpace(request.PhotoFileId) ? null : request.PhotoFileId.Trim();
        }

        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Update", "Resident", resident.Id.ToString(), resident.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(resident);
    }

    public async Task DeleteAsync(Guid residentId, CancellationToken cancellationToken = default)
    {
        var resident = await db.Residents.FirstOrDefaultAsync(r => r.Id == residentId, cancellationToken)
            ?? throw new NotFoundException("Resident not found.");

        // Remove is converted to a soft delete by the audit interceptor; the archived resident stays
        // traceable (Req 15.5).
        db.Residents.Remove(resident);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Delete", "Resident", resident.Id.ToString(), resident.CommunityId, Detail: null),
            cancellationToken);
    }

    public async Task<ResidentDto?> GetAsync(Guid residentId, bool includeArchived = false, CancellationToken cancellationToken = default)
    {
        IQueryable<Resident> q = db.Residents.AsNoTracking();
        if (includeArchived)
        {
            q = q.IgnoreQueryFilters(); // retrieve soft-deleted for audit (Req 15.5)
        }

        var resident = await q.FirstOrDefaultAsync(r => r.Id == residentId, cancellationToken);
        return resident is null ? null : ToDto(resident);
    }

    public Task<PagedData<ResidentDto>> ListAsync(ListQueryParameters parameters, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        // Tenant-scoped by the global query filter (Req 15.4): the caller only sees residents in
        // their authorized communities. The soft-delete filter excludes archived rows; a
        // verificationStatus filter rides the (CommunityId, VerificationStatus) index.
        return db.Residents.AsNoTracking().ToPagedResultAsync(
            parameters, QueryConfig, ToDto, pageSizePolicy, executor, cancellationToken);
    }

    public async Task<HouseholdMemberDto> AssociateHouseholdAsync(Guid residentId, AssociateHouseholdRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireRelationship(request.Relationship);

        var resident = await db.Residents.AsNoTracking()
            .Where(r => r.Id == residentId)
            .Select(r => new { r.Id, r.CommunityId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Resident not found.");

        await RequireUnitInSameCommunityAsync(request.UnitId, resident.CommunityId, cancellationToken);

        var member = new HouseholdMember
        {
            CommunityId = resident.CommunityId,
            ResidentId = resident.Id,
            UnitId = request.UnitId,
            Relationship = request.Relationship.Trim(),
            StartDate = request.StartDate ?? clock.UtcNow,
        };

        db.HouseholdMembers.Add(member);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Create", "HouseholdMember", member.Id.ToString(), member.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(member);
    }

    public async Task<EmergencyContactDto> AddEmergencyContactAsync(Guid residentId, AddEmergencyContactRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireContactFields(request);

        var resident = await db.Residents.AsNoTracking()
            .Where(r => r.Id == residentId)
            .Select(r => new { r.Id, r.CommunityId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Resident not found.");

        var contact = new EmergencyContact
        {
            CommunityId = resident.CommunityId,
            ResidentId = resident.Id,
            Name = request.Name.Trim(),
            Phone = request.Phone.Trim(),
            Relationship = request.Relationship.Trim(),
        };

        db.EmergencyContacts.Add(contact);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Create", "EmergencyContact", contact.Id.ToString(), contact.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(contact);
    }

    public async Task<IReadOnlyList<EmergencyContactDto>> ListEmergencyContactsAsync(Guid residentId, CancellationToken cancellationToken = default)
    {
        // Confirm the resident is visible to the caller (tenant-scoped) before returning children,
        // so a cross-tenant/unknown id gets a not-found rather than an empty list.
        var exists = await db.Residents.AsNoTracking().AnyAsync(r => r.Id == residentId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("Resident not found.");
        }

        var contacts = await db.EmergencyContacts.AsNoTracking()
            .Where(c => c.ResidentId == residentId)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        return contacts.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<UnitHouseholdMemberDto>> GetUnitHouseholdAsync(Guid unitId, CancellationToken cancellationToken = default)
    {
        // Active household members of the unit joined to their resident (name + type). Tenant-scoped
        // by the global filter on both sets; a resident-self-scoped principal only ever sees their own
        // unit's rows. Projected AsNoTracking — no over-fetch. Owners first, then by join date.
        var members = await (
            from h in db.HouseholdMembers.AsNoTracking()
            where h.UnitId == unitId && h.EndDate == null
            join r in db.Residents.AsNoTracking() on h.ResidentId equals r.Id
            orderby h.StartDate
            select new UnitHouseholdMemberDto(
                r.Id, r.Name, r.ResidentType, h.Relationship, h.StartDate))
            .ToListAsync(cancellationToken);

        return members;
    }

    public async Task<IReadOnlyList<ResidentHouseholdUnitDto>> GetResidentHouseholdAsync(Guid residentId, CancellationToken cancellationToken = default)
    {
        // Confirm the resident is visible to the caller before returning children (unknown/cross-tenant
        // → not-found, not an empty list) — same convention as ListEmergencyContactsAsync.
        var exists = await db.Residents.AsNoTracking().AnyAsync(r => r.Id == residentId, cancellationToken);
        if (!exists)
        {
            throw new NotFoundException("Resident not found.");
        }

        // The resident's OWN active unit memberships joined to the unit (number/type/status/floor).
        // HouseholdMember has no Unit navigation, so join db.Units on UnitId (mirrors GetUnitHousehold).
        // Tenant/self-scoped by the global filter on both sets; AsNoTracking — no over-fetch.
        var units = await (
            from h in db.HouseholdMembers.AsNoTracking()
            where h.ResidentId == residentId && h.EndDate == null
            join u in db.Units.AsNoTracking() on h.UnitId equals u.Id
            orderby h.StartDate
            select new ResidentHouseholdUnitDto(
                u.Id, u.UnitNumber, u.UnitType, u.UnitStatus, u.Floor, u.CommunityId, h.Relationship, h.StartDate))
            .ToListAsync(cancellationToken);

        return units;
    }

    public async Task<ResidentMeDto> GetMeAsync(CancellationToken cancellationToken = default)
    {
        // "Who am I": the resident row linked to the current token's user (Resident.UserId == sub) —
        // a single self-targeted lookup, never a list-and-filter. No authenticated user id, or an
        // unparseable one, or no linked resident row (e.g. a platform superadmin) → an empty result
        // (resident null, no units), NOT a 404, so the header falls back to the login identifier.
        if (!Guid.TryParse(tenantContext.UserId, out var userId))
        {
            return new ResidentMeDto(null, []);
        }

        // Match on UserId (not the first tenant row): admins/guards can see several residents, so the
        // own row must be the one whose login == this user. Tenant/self-scoped by the global filter.
        var resident = await db.Residents.AsNoTracking()
            .FirstOrDefaultAsync(r => r.UserId == userId, cancellationToken);
        if (resident is null)
        {
            return new ResidentMeDto(null, []);
        }

        // The signed-in resident's OWN active unit memberships (+ unit details) — same join as
        // GetResidentHouseholdAsync, so one /me call feeds both the header name and the unit pickers.
        var units = await (
            from h in db.HouseholdMembers.AsNoTracking()
            where h.ResidentId == resident.Id && h.EndDate == null
            join u in db.Units.AsNoTracking() on h.UnitId equals u.Id
            orderby h.StartDate
            select new ResidentHouseholdUnitDto(
                u.Id, u.UnitNumber, u.UnitType, u.UnitStatus, u.Floor, u.CommunityId, h.Relationship, h.StartDate))
            .ToListAsync(cancellationToken);

        return new ResidentMeDto(ToDto(resident), units);
    }

    public async Task<ResidentLoginResult> CreateLoginAsync(Guid residentId, CreateResidentLoginRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var identifier = request.Identifier?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ValidationException("identifier", "An email or phone is required for the login.");
        }
        if (string.IsNullOrEmpty(request.Password))
        {
            throw new ValidationException("password", "A password is required for the login.");
        }

        // Resolve the resident tenant-scoped (unknown/foreign → not found). Loaded tracked so we can
        // link the new user id in the same transaction.
        var resident = await db.Residents
            .FirstOrDefaultAsync(r => r.Id == residentId, cancellationToken)
            ?? throw new NotFoundException("Resident not found.");

        // Defense-in-depth: creating a login is a User-create action in the resident's community.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "User", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to create a login.");
        }
        authorizationGuard.EnsureCommunityAuthorized(resident.CommunityId);

        if (resident.UserId is not null)
        {
            throw new ConflictException("This resident already has a login.");
        }

        // An identifier is an email when it contains '@', else a phone. The DB unique indexes on
        // User.Email / User.Phone are the source of truth for uniqueness — a duplicate maps to
        // CONFLICT centrally rather than a read-then-write pre-check.
        var isEmail = identifier.Contains('@', StringComparison.Ordinal);
        var user = new User
        {
            Email = isEmail ? identifier : null,
            Phone = isEmail ? null : identifier,
            PasswordHash = passwordHasher.Hash(request.Password),
            Status = UserStatus.Active,
        };
        db.Users.Add(user);

        // Link the resident to the new login.
        resident.UserId = user.Id;

        // Assign the named role (default "Resident") scoped to the resident's community so the login
        // can actually use resident features. If no such role exists we still create the login + link
        // (RoleAssigned = null) rather than fail — the admin can grant a role later.
        var roleName = string.IsNullOrWhiteSpace(request.RoleName) ? DefaultResidentRoleName : request.RoleName.Trim();
        var role = await db.Roles
            .FirstOrDefaultAsync(r => r.Name == roleName, cancellationToken);
        string? roleAssigned = null;
        if (role is not null)
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = role.Id,
                CommunityId = resident.CommunityId,
            });
            roleAssigned = role.Name;
        }

        // A duplicate identifier races the filtered unique index on User.Email/Phone; translate the
        // violation into a clean CONFLICT (409) rather than a raw 500, instead of a read-then-write
        // pre-check (concurrency-safe, steering).
        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken),
            "That email or phone is already in use by another login.");

        await audit.AppendAsync(
            new AuditEntry("CreateLogin", "User", user.Id.ToString(), resident.CommunityId,
                Detail: $"Login created for resident {resident.Id}."),
            cancellationToken);

        return new ResidentLoginResult(user.Id, identifier, roleAssigned);
    }

    public Task<ResidentDto> ApproveAsync(Guid residentId, CancellationToken cancellationToken = default) =>
        TransitionAsync(
            residentId,
            fromStatus: ResidentVerificationStatuses.Pending,
            toStatus: ResidentVerificationStatuses.Verified,
            auditAction: "Approve",
            rejectionReason: null,
            templateKey: NotificationTemplateKeys.ResidentVerificationApproved,
            cancellationToken);

    public Task<ResidentDto> RejectAsync(Guid residentId, RejectResidentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            throw new ValidationException("reason", "A rejection reason is required.");
        }

        return TransitionAsync(
            residentId,
            fromStatus: ResidentVerificationStatuses.Pending,
            toStatus: ResidentVerificationStatuses.Rejected,
            auditAction: "Reject",
            rejectionReason: request.Reason.Trim(),
            templateKey: NotificationTemplateKeys.ResidentVerificationRejected,
            cancellationToken);
    }

    public Task<ResidentDto> ResubmitForVerificationAsync(Guid residentId, CancellationToken cancellationToken = default) =>
        // Re-submit a rejected resident after a correction (Req 17.1): rejected → pending clears the
        // prior reason and puts the resident back in the verification queue. The approved template is
        // reused as the generic account-status notice; a dedicated "resubmitted" template can be
        // seeded later without touching this code.
        TransitionAsync(
            residentId,
            fromStatus: ResidentVerificationStatuses.Rejected,
            toStatus: ResidentVerificationStatuses.Pending,
            auditAction: "ResubmitForVerification",
            rejectionReason: null,
            templateKey: NotificationTemplateKeys.ResidentApprovalApproved,
            cancellationToken);

    public async Task<HouseholdMemberDto> MoveInAsync(Guid residentId, MoveInRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth authorization (Req 18.1): a move-in is a Create verb on Resident. The Task
        // 12.7 endpoint gate declares it too, but the invariant must hold regardless of caller — no
        // permission → FORBIDDEN with nothing mutated.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Create, "Resident", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to record a move-in.");
        }

        // Resolve the resident (tenant-scoped) to get its community for the same-community check.
        var resident = await db.Residents.AsNoTracking()
            .Where(r => r.Id == residentId)
            .Select(r => new { r.Id, r.CommunityId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Resident not found.");

        authorizationGuard.EnsureCommunityAuthorized(resident.CommunityId);

        // The unit must belong to the resident's community (same convention as AssociateHouseholdAsync).
        await RequireUnitInSameCommunityAsync(request.UnitId, resident.CommunityId, cancellationToken);

        // Tracked load of the unit: we flip its status, so EF must carry the RowVersion into the
        // UPDATE (Req 22.4 → concurrency maps to CONFLICT centrally).
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == request.UnitId, cancellationToken)
            ?? throw new NotFoundException("Unit not found.");

        var moveInDate = request.MoveInDate ?? clock.UtcNow;

        // Reuse the resident's existing OPEN association with this unit if present; otherwise create
        // one (Req 18.1: "creating the association if needed or using the existing one"). Tracked load
        // because we stamp StartDate on the existing row.
        var member = await db.HouseholdMembers
            .FirstOrDefaultAsync(
                h => h.UnitId == request.UnitId && h.ResidentId == resident.Id && h.EndDate == null,
                cancellationToken);

        if (member is null)
        {
            if (string.IsNullOrWhiteSpace(request.Relationship))
            {
                throw new ValidationException("relationship",
                    "A household relationship is required to create the move-in association.");
            }

            member = new HouseholdMember
            {
                CommunityId = resident.CommunityId,
                ResidentId = resident.Id,
                UnitId = request.UnitId,
                Relationship = request.Relationship.Trim(),
                StartDate = moveInDate,
            };
            db.HouseholdMembers.Add(member);
        }
        else
        {
            // Confirm the move-in date on the existing open association.
            member.StartDate = moveInDate;
        }

        // Set the unit occupied (Req 18.1). The code is the seeded Unit_Status "occupied" — a known
        // master-data code referenced via a constant, not a scattered literal.
        unit.UnitStatus = UnitStatuses.Occupied;

        await audit.AppendAsync(
            new AuditEntry("MoveIn", "HouseholdMember", resident.Id.ToString(), resident.CommunityId,
                Detail: request.UnitId.ToString()),
            cancellationToken);

        // One SaveChanges = one transaction (Req 18.4): the household start, the unit status flip and
        // the audit row commit together. The unit's RowVersion guards the status flip — a concurrent
        // update losing the row-version check → CONFLICT (central mapping in AppDbContext).
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(member);
    }

    public async Task<HouseholdMemberDto> MoveOutAsync(Guid residentId, MoveOutRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Defense-in-depth authorization (Req 18.2): a move-out is an Edit verb on Resident.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Edit, "Resident", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to record a move-out.");
        }

        // Target this resident's OPEN association for the unit (tracked — we stamp EndDate). The
        // global tenant filter scopes it to the caller, so an unknown/foreign-tenant resident+unit
        // yields no open row → not found.
        var member = await db.HouseholdMembers
            .Where(h => h.UnitId == request.UnitId && h.ResidentId == residentId && h.EndDate == null)
            .OrderBy(h => h.StartDate)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("No active household association found for this resident and unit.");

        authorizationGuard.EnsureCommunityAuthorized(member.CommunityId);

        var moveOutDate = request.MoveOutDate ?? clock.UtcNow;
        // An end before the start is nonsensical; clamp to the start so the row stays coherent (same
        // convention as EndTenancyAsync).
        member.EndDate = moveOutDate < member.StartDate ? member.StartDate : moveOutDate;

        // Last-occupant → Vacant, concurrency-safe (Req 18.3). Re-count the OPEN household members for
        // the unit INSIDE this transaction, EXCLUDING the row we are ending (it is still EndDate==null
        // in the DB until SaveChanges, so exclude it by id). The decision is made on freshly counted
        // state — no read-then-write gap on a stale count. We only load+flip the unit when it actually
        // becomes empty, so a normal (non-last) move-out never touches the unit row (no needless
        // RowVersion contention).
        var remainingOpen = await db.HouseholdMembers
            .CountAsync(
                h => h.UnitId == request.UnitId && h.EndDate == null && h.Id != member.Id,
                cancellationToken);

        if (remainingOpen == 0)
        {
            // Tracked load so the unit's RowVersion guards the status flip. If a concurrent move-out
            // already flipped/stamped the unit, this save trips a row-version mismatch → CONFLICT
            // (central mapping); the caller retries and the re-count still leaves the unit vacant
            // exactly when the last occupant has left.
            var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == request.UnitId, cancellationToken);
            if (unit is not null)
            {
                unit.UnitStatus = UnitStatuses.Vacant;
            }
        }

        await audit.AppendAsync(
            new AuditEntry("MoveOut", "HouseholdMember", member.Id.ToString(), member.CommunityId,
                Detail: request.UnitId.ToString()),
            cancellationToken);

        // One SaveChanges = one transaction (Req 18.4): the household end, the possible unit status
        // flip and the audit row commit together. A concurrent last move-out losing the unit's
        // RowVersion check → CONFLICT (central mapping in AppDbContext).
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(member);
    }

    /// <summary>
    /// Shared verification-status transition (Task 12.3). Enforces the Approve permission + authorized
    /// community (Req 17.5), the valid <paramref name="fromStatus"/> → <paramref name="toStatus"/>
    /// transition (invalid → CONFLICT), stamps the status (and the rejection reason), audits the
    /// transition (approver/timestamp from the tenant context + clock, Req 17.2/17.3), enqueues the
    /// resident notification (Req 17.4), then commits the status change + outbox row in one
    /// <c>SaveChangesAsync</c>. The resident's <c>RowVersion</c> makes the transition record exactly
    /// once under concurrency: a losing concurrent save trips a row-version mismatch mapped centrally
    /// to CONFLICT (Req 22.3) — no read-then-write gap.
    /// </summary>
    private async Task<ResidentDto> TransitionAsync(
        Guid residentId,
        string fromStatus,
        string toStatus,
        string auditAction,
        string? rejectionReason,
        string templateKey,
        CancellationToken cancellationToken)
    {
        // Defense-in-depth authorization (Req 17.5): the Task 12.7 endpoint declares
        // [RequirePermission(Approve, "Resident")], but the invariant must hold regardless of caller,
        // so the service re-checks the Approve permission before any state change. No permission →
        // FORBIDDEN with nothing mutated.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Approve, "Resident", cancellationToken))
        {
            throw new ForbiddenException("You do not have permission to approve or reject residents.");
        }

        // Tracked load (not AsNoTracking): we mutate the row and need EF to carry the RowVersion
        // concurrency token into the UPDATE. The global tenant filter scopes it to the caller, so an
        // unknown/foreign-tenant id is a not-found.
        var resident = await db.Residents.FirstOrDefaultAsync(r => r.Id == residentId, cancellationToken)
            ?? throw new NotFoundException("Resident not found.");

        // API-layer tenant check for a direct id access (Req 5.3) — belt-and-braces with the filter.
        authorizationGuard.EnsureCommunityAuthorized(resident.CommunityId);

        // Valid-transition guard. An approve/reject/resubmit against the wrong current status is a
        // conflict with the resource's current state (CONFLICT), not a validation error — and it is
        // also what the concurrency loser effectively observes after the winner moved the row.
        if (!string.Equals(resident.VerificationStatus, fromStatus, StringComparison.Ordinal))
        {
            throw new ConflictException(
                $"The resident is '{resident.VerificationStatus}', not '{fromStatus}'; the operation is not valid for its current status.");
        }

        resident.VerificationStatus = toStatus;
        // Set the reason on reject; clear it on approve/resubmit so a stale reason never lingers.
        resident.RejectionReason = rejectionReason;

        // On REJECT, free the resources this (rejected) resident was holding so the unit becomes
        // available again and their phone/login can be reused:
        //  - end their OPEN household associations (so the unit's active household no longer counts
        //    them as the owner → the unit shows up again in "available units"),
        //  - soft-delete their linked login User + its role assignments (the soft-delete-aware unique
        //    index on User.Phone then frees that phone/email for a new login),
        //  - unlink Resident.UserId.
        // A later resubmit (rejected → pending) does NOT auto-restore these; the admin re-adds the
        // household/login via the normal flow. (ponytail: resubmit-restore not needed for the MVP.)
        if (string.Equals(toStatus, ResidentVerificationStatuses.Rejected, StringComparison.Ordinal))
        {
            var now = clock.UtcNow;

            var openMemberships = await db.HouseholdMembers
                .Where(h => h.ResidentId == resident.Id && h.EndDate == null)
                .ToListAsync(cancellationToken);
            foreach (var m in openMemberships)
            {
                m.EndDate = now;
            }

            if (resident.UserId is { } linkedUserId)
            {
                var loginUser = await db.Users.FirstOrDefaultAsync(u => u.Id == linkedUserId, cancellationToken);
                if (loginUser is not null)
                {
                    loginUser.IsDeleted = true;
                    loginUser.DeletedAtUtc = now;
                }

                var assignments = await db.UserRoles
                    .Where(ur => ur.UserId == linkedUserId && !ur.IsDeleted)
                    .ToListAsync(cancellationToken);
                foreach (var ur in assignments)
                {
                    ur.IsDeleted = true;
                    ur.DeletedAtUtc = now;
                }

                resident.UserId = null;
            }
        }

        // Audit the transition: the acting user and timestamp are stamped by the Audit_Service from
        // the tenant context + clock (Req 17.2/17.3), so the approver/time cannot be spoofed from a
        // DTO. The rejection reason is the only extra detail, and it is user-supplied free text (no
        // secret), safe to record.
        await audit.AppendAsync(
            new AuditEntry(auditAction, "Resident", resident.Id.ToString(), resident.CommunityId, Detail: rejectionReason),
            cancellationToken);

        // Notify the resident of the new status (Req 17.4). EnqueueAsync only Adds a Pending outbox
        // row to THIS DbContext (it does not save), so the SaveChangesAsync below commits the status
        // change and the notification atomically — a rollback queues nothing (transactional outbox).
        // Confidentiality + preference rules are enforced inside Notification_Service; it never throws
        // for an opted-out resident/unknown template, so a notification decision can't break the
        // transition. The notice targets the resident's OWN linked login user only. A resident with no
        // linked user has no push recipient, so we skip the enqueue (nothing to target) while still
        // recording the status change + audit.
        if (resident.UserId is { } recipientUserId)
        {
            await notifications.EnqueueAsync(
                new NotificationRequest(
                    templateKey,
                    recipientUserId,
                    resident.CommunityId,
                    Placeholders: BuildPlaceholders(resident, rejectionReason)),
                cancellationToken);
        }

        // One save = one transaction: status change + audit row + outbox row commit together. The
        // RowVersion token makes concurrent approvals record exactly once — the loser's save trips a
        // DbUpdateConcurrencyException translated to CONFLICT in AppDbContext.SaveChangesAsync (Req 22.3).
        await db.SaveChangesAsync(cancellationToken);

        return ToDto(resident);
    }

    private static IReadOnlyDictionary<string, string> BuildPlaceholders(Resident resident, string? rejectionReason)
    {
        // Fills the seeded template {tokens}: {residentName} for all notices, {reason} for rejection.
        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["residentName"] = resident.Name,
        };
        if (rejectionReason is not null)
        {
            placeholders["reason"] = rejectionReason;
        }

        return placeholders;
    }

    private static ResidentDto ToDto(Resident r) =>
        new(r.Id, r.CommunityId, r.UserId, r.Name, r.Email, r.Phone, r.ResidentType,
            r.VerificationStatus, r.RejectionReason, r.IsDeleted, r.PhotoFileId);

    private static HouseholdMemberDto ToDto(HouseholdMember h) =>
        // UnitId is non-null in practice (set on associate); default to Guid.Empty defensively.
        new(h.Id, h.CommunityId, h.UnitId ?? Guid.Empty, h.ResidentId, h.Relationship, h.StartDate, h.EndDate);

    private static EmergencyContactDto ToDto(EmergencyContact c) =>
        new(c.Id, c.ResidentId, c.Name, c.Phone, c.Relationship);

    private static void RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("name", "A resident name is required.");
        }
    }

    private static void RequireRelationship(string? relationship)
    {
        if (string.IsNullOrWhiteSpace(relationship))
        {
            throw new ValidationException("relationship", "A household relationship is required.");
        }
    }

    private static void RequireContactFields(AddEmergencyContactRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("name", "An emergency contact name is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Phone))
        {
            throw new ValidationException("phone", "An emergency contact phone is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Relationship))
        {
            throw new ValidationException("relationship", "An emergency contact relationship is required.");
        }
    }

    private async Task RequireValidResidentTypeAsync(string? residentType, Guid communityId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(residentType))
        {
            throw new ValidationException("residentType", "A resident type is required.");
        }

        // Validate against the configurable Resident_Type list resolved for this community (Req 15.1)
        // — not a frozen enum, so types stay editable per community.
        var allowed = await config.GetMasterDataAsync(MasterDataKeys.ResidentType, communityId, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, residentType.Trim(), StringComparison.Ordinal)))
        {
            throw new ValidationException("residentType", $"'{residentType}' is not a valid resident type.");
        }
    }

    private async Task RequireUnitInSameCommunityAsync(Guid unitId, Guid communityId, CancellationToken cancellationToken)
    {
        // Tenant-scoped read: the global filter already hides foreign-tenant units. Load the unit's
        // CommunityId and reject a cross-community association with a validation error (same
        // convention as UnitService's same-community node check).
        var unitCommunityId = await db.Units.AsNoTracking()
            .Where(u => u.Id == unitId)
            .Select(u => (Guid?)u.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        if (unitCommunityId is null || unitCommunityId.Value != communityId)
        {
            throw new ValidationException("unitId", "The unit must belong to the same community as the resident.");
        }
    }
}
