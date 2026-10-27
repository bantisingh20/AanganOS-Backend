using App.Application.Abstractions;
using App.Application.Abstractions.Security;
using App.Application.Auth;
using App.Application.Config;
using App.Application.Exceptions;
using App.Application.Files;
using App.Application.Notifications;
using App.Application.Onboarding;
using App.Domain.Identity;
using App.Domain.Residents;
using App.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Onboarding;

/// <summary>
/// Resident self-onboarding + admin approval (mobile login/registration flow). The anonymous reads +
/// submit use the request-scoped <see cref="AppDbContext"/> with <c>IgnoreQueryFilters()</c> (the
/// caller has no principal, so the tenant filter would otherwise hide everything) — soft-delete is
/// re-applied explicitly in each query. Admin list/approve/reject run normally, permission-gated on
/// <c>JoinRequest</c> and community-scoped by the authenticated admin's authorized communities.
/// </summary>
public sealed class ResidentOnboardingService(
    IPasswordHasher passwordHasher,
    AppDbContext db,
    ITenantContext tenantContext,
    IAuthorizationGuard authorizationGuard,
    INotificationService notifications,
    IConfigService config,
    IFileService files,
    IAuthService authService) : IResidentOnboardingService
{
    private const int MaxSearchResults = 25;
    private const int MaxUnitResults = 100;
    private const string JoinRequestResource = "JoinRequest";
    private const string SelfRegistrationEnabledKey = "onboarding.selfRegistration.enabled";

    // --- Anonymous reads + submit (isolated super-admin scope) ------------------------------------

    public async Task<bool> IdentifierExistsAsync(string identifier, CancellationToken cancellationToken = default)
    {
        var id = (identifier ?? string.Empty).Trim();
        if (id.Length == 0) return false;
        // User is platform-scoped; IgnoreQueryFilters reaches it anonymously (same as OnboardingService).
        // Return only the boolean — never which field matched (no enumeration beyond existence).
        return await db.Users.IgnoreQueryFilters()
            .AnyAsync(u => u.Email == id || u.Phone == id, cancellationToken);
    }

    public async Task<IReadOnlyList<PublicCommunityDto>> SearchCommunitiesAsync(string? search, CancellationToken cancellationToken = default)
    {
        // Anonymous caller: bypass the tenant filter with IgnoreQueryFilters (soft-delete still
        // applied explicitly) so the public picker can see communities. Minimal fields, capped.
        var term = (search ?? string.Empty).Trim();
        var q = db.Communities.IgnoreQueryFilters().AsNoTracking().Where(c => !c.IsDeleted);
        if (term.Length > 0) q = q.Where(c => c.Name.Contains(term));
        return await q.OrderBy(c => c.Name)
            .Take(MaxSearchResults)
            .Select(c => new PublicCommunityDto(c.Id, c.Name, c.CommunityType))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PublicWingDto>> ListWingsAsync(Guid communityId, CancellationToken cancellationToken = default)
    {
        return await db.HierarchyNodes.IgnoreQueryFilters().AsNoTracking()
            .Where(n => !n.IsDeleted && n.CommunityId == communityId)
            .OrderBy(n => n.Name)
            .Select(n => new PublicWingDto(n.Id, n.Name, n.Level, n.ParentId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PublicUnitDto>> ListUnitsAsync(Guid communityId, Guid? wingId, string? search, CancellationToken cancellationToken = default)
    {
        var term = (search ?? string.Empty).Trim();
        var q = db.Units.IgnoreQueryFilters().AsNoTracking().Where(u => !u.IsDeleted && u.CommunityId == communityId);
        if (wingId is { } w) q = q.Where(u => u.HierarchyNodeId == w);
        if (term.Length > 0) q = q.Where(u => u.UnitNumber.Contains(term));
        q = q.Where(u => !db.HouseholdMembers.IgnoreQueryFilters()
            .Any(h => !h.IsDeleted && h.UnitId == u.Id && h.EndDate == null));
        return await q.OrderBy(u => u.UnitNumber)
            .Take(MaxUnitResults)
            .Select(u => new PublicUnitDto(u.Id, u.UnitNumber, u.HierarchyNodeId))
            .ToListAsync(cancellationToken);
    }

    public async Task<SubmitJoinRequestResult> SubmitJoinRequestAsync(SubmitJoinRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var name = (request.Name ?? string.Empty).Trim();
        var phone = (request.Phone ?? string.Empty).Trim();
        var pin = (request.Pin ?? string.Empty).Trim();

        if (name.Length is 0 or > 256) throw new ValidationException("name", "Your name is required (max 256).");
        if (phone.Length is 0 or > 32) throw new ValidationException("phone", "A phone number is required.");
        if (!IsValidPin(pin)) throw new ValidationException("pin", "The PIN must be exactly 4 digits.");
        if (request.CommunityId == Guid.Empty) throw new ValidationException("communityId", "Select your community.");
        if (request.UnitId == Guid.Empty) throw new ValidationException("unitId", "Select your unit.");

        // Anonymous caller: reads bypass the tenant filter (IgnoreQueryFilters), soft-delete applied
        // explicitly. Validate community+unit exist, enforce one-active-resident-per-unit + no
        // duplicate login + no existing pending request, add the request and notify the community's
        // admins — all committed in one SaveChanges on the request-scoped DbContext.
        var unit = await db.Units.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(u => !u.IsDeleted && u.Id == request.UnitId && u.CommunityId == request.CommunityId, cancellationToken)
            ?? throw new ValidationException("unitId", "That unit was not found in the selected community.");

        var phoneTaken = await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Phone == phone, cancellationToken);
        if (phoneTaken) throw new ConflictException("An account with that phone already exists. Please sign in instead.");

        var unitOccupied = await db.HouseholdMembers.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(h => !h.IsDeleted && h.UnitId == request.UnitId && h.EndDate == null, cancellationToken);
        if (unitOccupied) throw new ConflictException("This unit already has a registered resident.");

        var requestExists = await db.ResidentJoinRequests.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(r => !r.IsDeleted && r.UnitId == request.UnitId
                && (r.Status == ResidentJoinRequestStatuses.Pending || r.Status == ResidentJoinRequestStatuses.Approved),
                cancellationToken);
        if (requestExists) throw new ConflictException("A registration request for this unit is already in progress.");

        var joinRequest = new ResidentJoinRequest
        {
            CommunityId = request.CommunityId,
            UnitId = request.UnitId,
            Name = name,
            Phone = phone,
            PinHash = passwordHasher.Hash(pin),
            Status = ResidentJoinRequestStatuses.Pending,
        };
        db.ResidentJoinRequests.Add(joinRequest);

        // Notify the community's admins (users whose roles grant View on JoinRequest). Enqueued on
        // the same DbContext so it commits with the request row.
        await NotifyCommunityAdminsAsync(db, notifications, request.CommunityId,
            NotificationTemplateKeys.JoinRequestSubmitted,
            new Dictionary<string, string> { ["name"] = name, ["unitNumber"] = unit.UnitNumber },
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return new SubmitJoinRequestResult(joinRequest.Id, joinRequest.Status);
    }

    public Task<bool> IsSelfRegistrationEnabledAsync(CancellationToken cancellationToken = default) =>
        config.GetBoolAsync(SelfRegistrationEnabledKey, true, cancellationToken: cancellationToken);

    public async Task<SelfRegisterResult> SelfRegisterAsync(SelfRegisterRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await IsSelfRegistrationEnabledAsync(cancellationToken))
        {
            throw new ForbiddenException("Self-registration is disabled.");
        }

        var name = (request.Name ?? string.Empty).Trim();
        var phone = (request.Phone ?? string.Empty).Trim();
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        var password = request.Password ?? string.Empty;

        if (name.Length is 0 or > 256) throw new ValidationException("name", "Your name is required (max 256).");
        if (phone.Length is 0 or > 32) throw new ValidationException("phone", "A phone number is required.");
        if (password.Length < 6) throw new ValidationException("password", "The password must be at least 6 characters.");
        if (request.CommunityId == Guid.Empty) throw new ValidationException("communityId", "Select your community.");
        if (request.UnitId == Guid.Empty) throw new ValidationException("unitId", "Select your unit.");

        var unit = await db.Units.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(u => !u.IsDeleted && u.Id == request.UnitId && u.CommunityId == request.CommunityId, cancellationToken)
            ?? throw new ValidationException("unitId", "That unit was not found in the selected community.");

        var phoneTaken = await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Phone == phone, cancellationToken);
        if (phoneTaken) throw new ConflictException("An account with that phone already exists. Please sign in instead.");

        var unitOccupied = await db.HouseholdMembers.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(h => !h.IsDeleted && h.UnitId == request.UnitId && h.EndDate == null, cancellationToken);
        if (unitOccupied) throw new ConflictException("This unit already has a registered resident.");

        var user = new User
        {
            Phone = phone,
            Email = email,
            PasswordHash = passwordHasher.Hash(password),
            Status = UserStatus.Active,
        };
        db.Users.Add(user);

        var resident = new Resident
        {
            CommunityId = request.CommunityId,
            UserId = user.Id,
            Name = name,
            Phone = phone,
            Email = email,
            ResidentType = "owner",
            VerificationStatus = ResidentVerificationStatuses.Pending,
        };
        db.Residents.Add(resident);

        db.HouseholdMembers.Add(new HouseholdMember
        {
            CommunityId = request.CommunityId,
            ResidentId = resident.Id,
            UnitId = request.UnitId,
            Relationship = "self",
            StartDate = DateTime.UtcNow,
        });

        var residentRoleId = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.Name == "Resident")
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (residentRoleId is { } roleId)
        {
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId, CommunityId = request.CommunityId });
        }

        var photoFileId = await StorePhotoAsync(request.PhotoBase64, request.CommunityId, resident.Id, cancellationToken);
        if (photoFileId is { } pid)
        {
            resident.PhotoFileId = pid.ToString();
        }

        await db.SaveChangesAsync(cancellationToken);

        var login = await authService.IssueSessionForUserAsync(user.Id, cancellationToken);
        return new SelfRegisterResult(resident.Id, login);
    }

    // --- Admin list / approve / reject (normal request scope) -------------------------------------

    public async Task<IReadOnlyList<JoinRequestDto>> ListPendingJoinRequestsAsync(CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(PermissionAction.View, cancellationToken);

        // Tenant-scoped by the authenticated admin's authorized communities (global filter). Join to
        // the unit for its number. Pending first, newest first.
        var rows = await (
            from r in db.ResidentJoinRequests.AsNoTracking()
            where r.Status == ResidentJoinRequestStatuses.Pending
            join u in db.Units.AsNoTracking() on r.UnitId equals u.Id
            orderby r.CreatedAtUtc descending
            select new JoinRequestDto(
                r.Id, r.CommunityId, r.UnitId, u.UnitNumber, r.Name, r.Phone, r.Status,
                r.CreatedAtUtc, r.DecidedAtUtc, r.RejectReason))
            .ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<JoinRequestDto> ApproveJoinRequestAsync(Guid requestId, CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(PermissionAction.Approve, cancellationToken);

        // Tracked load, tenant-scoped (a foreign-community request is hidden → not found).
        var req = await db.ResidentJoinRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new NotFoundException("The join request was not found in your authorized communities.");
        authorizationGuard.EnsureCommunityAuthorized(req.CommunityId);

        if (req.Status == ResidentJoinRequestStatuses.Approved)
        {
            return await ToDtoAsync(req, cancellationToken); // idempotent: already approved
        }
        if (req.Status != ResidentJoinRequestStatuses.Pending)
        {
            throw new ConflictException("This request has already been decided.");
        }

        // Re-check the invariants at approve time (race: a unit may have been taken since submit).
        var phoneTaken = await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Phone == req.Phone, cancellationToken);
        if (phoneTaken) throw new ConflictException("An account with that phone now exists; cannot approve.");
        var unitOccupied = await db.HouseholdMembers.AsNoTracking()
            .AnyAsync(h => h.UnitId == req.UnitId && h.EndDate == null, cancellationToken);
        if (unitOccupied) throw new ConflictException("This unit already has a registered resident; cannot approve.");

        // Provision: User (phone + hashed PIN already stored on the request) → Resident → household.
        var user = new User
        {
            Phone = req.Phone,
            PasswordHash = req.PinHash, // already a salted hash of the PIN (not re-hashed)
            Status = UserStatus.Active,
        };
        db.Users.Add(user);

        var resident = new Resident
        {
            CommunityId = req.CommunityId,
            UserId = user.Id,
            Name = req.Name,
            Phone = req.Phone,
            ResidentType = "owner",
            VerificationStatus = "verified",
        };
        db.Residents.Add(resident);

        db.HouseholdMembers.Add(new HouseholdMember
        {
            CommunityId = req.CommunityId,
            ResidentId = resident.Id,
            UnitId = req.UnitId,
            Relationship = "self",
            StartDate = DateTime.UtcNow,
        });

        // Assign the "Resident" role scoped to this community so the new login has resident access.
        var residentRoleId = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.Name == "Resident")
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (residentRoleId is { } roleId)
        {
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId, CommunityId = req.CommunityId });
        }

        req.Status = ResidentJoinRequestStatuses.Approved;
        req.DecidedBy = tenantContext.UserId;
        req.DecidedAtUtc = DateTime.UtcNow;
        req.CreatedResidentId = resident.Id;

        // Notify the requester (their new user can receive once the row is committed).
        var communityName = await db.Communities.AsNoTracking()
            .Where(c => c.Id == req.CommunityId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken) ?? "your community";
        await notifications.EnqueueAsync(
            new NotificationRequest(NotificationTemplateKeys.JoinRequestApproved, user.Id, req.CommunityId,
                new Dictionary<string, string> { ["communityName"] = communityName }),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(req, cancellationToken);
    }

    public async Task<JoinRequestDto> RejectJoinRequestAsync(Guid requestId, string? reason, CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(PermissionAction.Approve, cancellationToken);

        var req = await db.ResidentJoinRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken)
            ?? throw new NotFoundException("The join request was not found in your authorized communities.");
        authorizationGuard.EnsureCommunityAuthorized(req.CommunityId);

        if (req.Status == ResidentJoinRequestStatuses.Rejected)
        {
            return await ToDtoAsync(req, cancellationToken);
        }
        if (req.Status != ResidentJoinRequestStatuses.Pending)
        {
            throw new ConflictException("This request has already been decided.");
        }

        req.Status = ResidentJoinRequestStatuses.Rejected;
        req.DecidedBy = tenantContext.UserId;
        req.DecidedAtUtc = DateTime.UtcNow;
        req.RejectReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        await db.SaveChangesAsync(cancellationToken);
        return await ToDtoAsync(req, cancellationToken);
    }

    // --- helpers ----------------------------------------------------------------------------------

    private async Task<Guid?> StorePhotoAsync(string? photoBase64, Guid communityId, Guid residentId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(photoBase64))
        {
            return null;
        }

        var raw = photoBase64.Trim();
        var contentType = "image/png";
        var comma = raw.IndexOf(',');
        if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
        {
            var header = raw[5..comma];
            var semi = header.IndexOf(';');
            var mime = semi > 0 ? header[..semi] : header;
            if (mime.Length > 0)
            {
                contentType = mime;
            }
            raw = raw[(comma + 1)..];
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(raw);
        }
        catch (FormatException)
        {
            throw new ValidationException("photoBase64", "The photo is not valid base64 data.");
        }

        if (bytes.Length == 0)
        {
            return null;
        }

        using var stream = new MemoryStream(bytes);
        var reference = await files.StoreAsync(
            new StoreFileRequest(
                OwningResourceType: "Resident",
                OwningResourceId: residentId,
                CommunityId: communityId,
                FileName: $"resident-{residentId}.png",
                ContentType: contentType,
                Content: stream),
            cancellationToken);
        return reference.Id;
    }

    private async Task RequirePermissionAsync(PermissionAction action, CancellationToken ct)
    {
        if (!await authorizationGuard.HasPermissionAsync(action, JoinRequestResource, ct))
        {
            throw new ForbiddenException("You do not have permission to manage join requests.");
        }
    }

    private async Task<JoinRequestDto> ToDtoAsync(ResidentJoinRequest r, CancellationToken ct)
    {
        var unitNumber = await db.Units.IgnoreQueryFilters()
            .Where(u => u.Id == r.UnitId).Select(u => u.UnitNumber).FirstOrDefaultAsync(ct) ?? "";
        return new JoinRequestDto(r.Id, r.CommunityId, r.UnitId, unitNumber, r.Name, r.Phone, r.Status,
            r.CreatedAtUtc, r.DecidedAtUtc, r.RejectReason);
    }

    /// <summary>Notify the community's admins (users granted View on JoinRequest in that community).</summary>
    private static async Task NotifyCommunityAdminsAsync(
        AppDbContext db, INotificationService notifications, Guid communityId, string templateKey,
        IReadOnlyDictionary<string, string> placeholders, CancellationToken ct)
    {
        var recipientUserIds = await (
            from ur in db.UserRoles.AsNoTracking()
            where ur.CommunityId == communityId || ur.CommunityId == null
            join rp in db.RolePermissions.AsNoTracking() on ur.RoleId equals rp.RoleId
            join p in db.Permissions.AsNoTracking() on rp.PermissionId equals p.Id
            where p.ResourceType == JoinRequestResource && p.Action == PermissionAction.View
            select ur.UserId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var userId in recipientUserIds)
        {
            await notifications.EnqueueAsync(
                new NotificationRequest(templateKey, userId, communityId, placeholders), ct);
        }
    }

    private static bool IsValidPin(string pin) => pin.Length == 4 && pin.All(char.IsDigit);
}
