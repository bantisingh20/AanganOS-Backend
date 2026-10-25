using UrbanNest.Application.Abstractions.Security;
using UrbanNest.Application.Audit;
using UrbanNest.Application.Config;
using UrbanNest.Application.Exceptions;
using UrbanNest.Application.Residents;
using UrbanNest.Domain.Identity;
using UrbanNest.Domain.Residents;
using UrbanNest.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace UrbanNest.Infrastructure.Residents;

/// <summary>
/// EF-backed <see cref="IResidentAssetsService"/> (Task 12.5, Req 19.1–19.5).
///
/// <para><b>Configurable types (Req 19.1, 19.3).</b> <c>VehicleType</c>/<c>PetType</c> are validated
/// at runtime against the editable <c>Vehicle_Type</c>/<c>Pet_Type</c> master-data lists resolved
/// for the unit's own community — not frozen enums.</para>
///
/// <para><b>Concurrency-safe vehicle uniqueness (Req 19.2).</b> A duplicate
/// <c>(CommunityId, RegistrationNumber)</c> is NOT pre-checked with a read-then-write (which would
/// race two concurrent registrations past the gap). The write relies on the DB unique index
/// (UX_Vehicle_CommunityId_RegistrationNumber) as the single source of truth and translates the race
/// loser's violation into CONFLICT via <see cref="UniqueConstraintGuard"/> — exactly the pattern
/// <c>UnitService</c> uses for unit numbers.</para>
///
/// <para><b>Assign gating (Req 19.4).</b> Parking/storage assignment re-checks the <c>Assign</c>
/// permission + authorized community in the service (defense-in-depth on top of the Task 12.7
/// endpoint gate) and validates the target unit is in the same community. ponytail: a slot/location
/// carries a single reassignable <c>AssignedUnitId</c> with no active-assignment concurrency guard —
/// the full parking module (one active assignment per slot, violations, EV) is Phase 6 (Task 24).</para>
///
/// <para><b>Resident self-view (Req 19.5, 5.4).</b> <see cref="Vehicle"/>/<see cref="Pet"/> are
/// <c>IUnitScoped</c>, so the DbContext resident self-scope filter restricts a resident principal to
/// their own unit's records automatically — no bespoke predicate here. All reads are tenant-scoped
/// by the global filter and use <c>AsNoTracking</c>; mutations are audited.</para>
/// </summary>
public sealed class ResidentAssetsService(
    AppDbContext db,
    IAuditService audit,
    IConfigService config,
    IAuthorizationGuard authorizationGuard)
    : IResidentAssetsService
{
    private const string DuplicateVehicleMessage =
        "A vehicle with the same registration number already exists in this community.";
    private const string DuplicateParkingSlotMessage =
        "A parking slot with the same code already exists in this community.";
    private const string DuplicateStorageLocationMessage =
        "A storage location with the same code already exists in this community.";

    public async Task<VehicleDto> RegisterVehicleAsync(RegisterVehicleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var registration = RequireText(request.RegistrationNumber, "registrationNumber", "A vehicle registration number is required.");

        // Resolve the unit (tenant-scoped) to anchor the vehicle in the unit's community and run the
        // same-community check — a vehicle is always registered against a known unit (Req 19.1).
        var communityId = await RequireUnitAndCommunityAsync(request.UnitId, cancellationToken);
        await RequireValidTypeAsync(MasterDataKeys.VehicleType, request.VehicleType, communityId, "vehicleType", "vehicle type", cancellationToken);

        var vehicle = new Vehicle
        {
            CommunityId = communityId,
            UnitId = request.UnitId,
            RegistrationNumber = registration,
            VehicleType = request.VehicleType.Trim(),
        };

        db.Vehicles.Add(vehicle);

        // Concurrency-safe (Req 19.2): let the DB unique index decide the winner (no read-then-write).
        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateVehicleMessage);

        await audit.AppendAsync(
            new AuditEntry("Create", "Vehicle", vehicle.Id.ToString(), vehicle.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(vehicle);
    }

    public async Task<PetDto> RegisterPetAsync(RegisterPetRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var communityId = await RequireUnitAndCommunityAsync(request.UnitId, cancellationToken);
        await RequireValidTypeAsync(MasterDataKeys.PetType, request.PetType, communityId, "petType", "pet type", cancellationToken);

        var pet = new Pet
        {
            CommunityId = communityId,
            UnitId = request.UnitId,
            PetType = request.PetType.Trim(),
            Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
        };

        db.Pets.Add(pet);
        await db.SaveChangesAsync(cancellationToken);

        await audit.AppendAsync(
            new AuditEntry("Create", "Pet", pet.Id.ToString(), pet.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(pet);
    }

    public async Task<ParkingSlotDto> CreateParkingSlotAsync(CreateParkingSlotRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var code = RequireText(request.Code, "code", "A parking slot code is required.");

        var slot = new ParkingSlot { CommunityId = request.CommunityId, Code = code };
        db.ParkingSlots.Add(slot);

        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateParkingSlotMessage);

        await audit.AppendAsync(
            new AuditEntry("Create", "ParkingSlot", slot.Id.ToString(), slot.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(slot);
    }

    public async Task<StorageLocationDto> CreateStorageLocationAsync(CreateStorageLocationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var code = RequireText(request.Code, "code", "A storage location code is required.");

        var location = new StorageLocation { CommunityId = request.CommunityId, Code = code };
        db.StorageLocations.Add(location);

        await UniqueConstraintGuard.SaveTranslatingUniqueViolationAsync(
            () => db.SaveChangesAsync(cancellationToken), DuplicateStorageLocationMessage);

        await audit.AppendAsync(
            new AuditEntry("Create", "StorageLocation", location.Id.ToString(), location.CommunityId, Detail: null),
            cancellationToken);

        return ToDto(location);
    }

    public async Task<ParkingSlotDto> AssignParkingSlotAsync(Guid parkingSlotId, AssignToUnitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await RequireAssignPermissionAsync("ParkingSlot", cancellationToken);

        // Tracked load so the slot's RowVersion guards the assignment flip — a concurrent reassign
        // losing the row-version check → CONFLICT (central mapping in AppDbContext).
        var slot = await db.ParkingSlots.FirstOrDefaultAsync(s => s.Id == parkingSlotId, cancellationToken)
            ?? throw new NotFoundException("Parking slot not found.");

        authorizationGuard.EnsureCommunityAuthorized(slot.CommunityId);
        await ApplyAssignmentAsync(request.UnitId, slot.CommunityId, cancellationToken);

        slot.AssignedUnitId = request.UnitId; // null => clear the association (Req 19.4)

        await audit.AppendAsync(
            new AuditEntry("Assign", "ParkingSlot", slot.Id.ToString(), slot.CommunityId,
                Detail: request.UnitId?.ToString()),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(slot);
    }

    public async Task<StorageLocationDto> AssignStorageLocationAsync(Guid storageLocationId, AssignToUnitRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await RequireAssignPermissionAsync("StorageLocation", cancellationToken);

        var location = await db.StorageLocations.FirstOrDefaultAsync(s => s.Id == storageLocationId, cancellationToken)
            ?? throw new NotFoundException("Storage location not found.");

        authorizationGuard.EnsureCommunityAuthorized(location.CommunityId);
        await ApplyAssignmentAsync(request.UnitId, location.CommunityId, cancellationToken);

        location.AssignedUnitId = request.UnitId; // null => clear

        await audit.AppendAsync(
            new AuditEntry("Assign", "StorageLocation", location.Id.ToString(), location.CommunityId,
                Detail: request.UnitId?.ToString()),
            cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return ToDto(location);
    }

    public async Task<IReadOnlyList<VehicleDto>> ListVehiclesForUnitAsync(Guid unitId, CancellationToken cancellationToken = default)
    {
        // Tenant-scoped + resident self-scoped by the global filter (Req 19.5). Hits the
        // (CommunityId, UnitId) index. No N+1 — a single projecting read.
        var rows = await db.Vehicles.AsNoTracking()
            .Where(v => v.UnitId == unitId)
            .OrderBy(v => v.RegistrationNumber)
            .ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<PetDto>> ListPetsForUnitAsync(Guid unitId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Pets.AsNoTracking()
            .Where(p => p.UnitId == unitId)
            .OrderBy(p => p.PetType)
            .ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<ParkingSlotDto>> ListParkingSlotsForUnitAsync(Guid unitId, CancellationToken cancellationToken = default)
    {
        var rows = await db.ParkingSlots.AsNoTracking()
            .Where(s => s.AssignedUnitId == unitId)
            .OrderBy(s => s.Code)
            .ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<IReadOnlyList<StorageLocationDto>> ListStorageLocationsForUnitAsync(Guid unitId, CancellationToken cancellationToken = default)
    {
        var rows = await db.StorageLocations.AsNoTracking()
            .Where(s => s.AssignedUnitId == unitId)
            .OrderBy(s => s.Code)
            .ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    /// <summary>
    /// Resolve a unit (tenant-scoped) and return its community id, rejecting an unknown/foreign-tenant
    /// unit. The returned community anchors the asset's CommunityId, so a vehicle/pet is always owned
    /// by the same community as its unit (same-community by construction, Req 19.1/19.3).
    /// </summary>
    private async Task<Guid> RequireUnitAndCommunityAsync(Guid unitId, CancellationToken cancellationToken)
    {
        var communityId = await db.Units.AsNoTracking()
            .Where(u => u.Id == unitId)
            .Select(u => (Guid?)u.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        return communityId ?? throw new ValidationException("unitId", "The unit was not found in your authorized communities.");
    }

    /// <summary>
    /// Validate the assignment target: when a unit is supplied it must belong to the same community
    /// as the slot/location (Req 19.4). A null unit clears the association and needs no unit check.
    /// </summary>
    private async Task ApplyAssignmentAsync(Guid? unitId, Guid communityId, CancellationToken cancellationToken)
    {
        if (unitId is not { } targetUnitId)
        {
            return;
        }

        var unitCommunityId = await db.Units.AsNoTracking()
            .Where(u => u.Id == targetUnitId)
            .Select(u => (Guid?)u.CommunityId)
            .FirstOrDefaultAsync(cancellationToken);

        if (unitCommunityId is null || unitCommunityId.Value != communityId)
        {
            throw new ValidationException("unitId", "The unit must belong to the same community as the slot or location.");
        }
    }

    private async Task RequireAssignPermissionAsync(string resourceType, CancellationToken cancellationToken)
    {
        // Defense-in-depth (Req 19.4): the Task 12.7 endpoint gate declares [RequirePermission(Assign,
        // ...)], but the invariant must hold regardless of caller — no permission → FORBIDDEN with
        // nothing mutated.
        if (!await authorizationGuard.HasPermissionAsync(PermissionAction.Assign, resourceType, cancellationToken))
        {
            throw new ForbiddenException($"You do not have permission to assign a {resourceType}.");
        }
    }

    private async Task RequireValidTypeAsync(string listKey, string? value, Guid communityId, string field, string label, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(field, $"A {label} is required.");
        }

        // Validate against the configurable list resolved for this community (Req 19.1/19.3) — not a
        // frozen enum, so types stay editable per community.
        var allowed = await config.GetMasterDataAsync(listKey, communityId, cancellationToken: cancellationToken);
        if (!allowed.Any(e => string.Equals(e.Code, value.Trim(), StringComparison.Ordinal)))
        {
            throw new ValidationException(field, $"'{value}' is not a valid {label}.");
        }
    }

    private static string RequireText(string? value, string field, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException(field, message);
        }

        return value.Trim();
    }

    private static VehicleDto ToDto(Vehicle v) =>
        new(v.Id, v.CommunityId, v.UnitId ?? Guid.Empty, v.RegistrationNumber, v.VehicleType);

    private static PetDto ToDto(Pet p) =>
        new(p.Id, p.CommunityId, p.UnitId ?? Guid.Empty, p.PetType, p.Name);

    private static ParkingSlotDto ToDto(ParkingSlot s) =>
        new(s.Id, s.CommunityId, s.Code, s.AssignedUnitId);

    private static StorageLocationDto ToDto(StorageLocation s) =>
        new(s.Id, s.CommunityId, s.Code, s.AssignedUnitId);
}
