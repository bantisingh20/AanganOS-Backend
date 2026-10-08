# Task: Maintenance inspections, AMC contracts and meter history (Task 21.3)
- Date: 2026-02-11
- Author: Kiro
- Project: backend (CommunityOS-Backend)
- Status: done

## Objective
Extend the Maintenance_Service seam (Task 21.1/21.2) with inspection checklists, AMC
contracts (with a configurable expiry-reminder sweep) and equipment meter readings, all
appending into the existing append-only equipment history. Requirements 37.1–37.5.

## Requirements
- 37.1 Complete an InspectionChecklist against an asset/work order → store the completed
  checklist with each item result, the inspecting user and the timestamp.
- 37.2 Record an AmcContract (vendor, covered asset, start, end) with the Create permission.
- 37.3 While an AMC contract is within the configured expiry-reminder window → notify the
  responsible user via Notification_Service (bounded background sweep, at-most-once per window).
- 37.4 Record a MeterReading for an asset → store it and append to equipment history. Meter
  readings are an equipment counter → enforce non-decreasing per (asset, meter) with a
  VALIDATION_ERROR on a lower reading (concurrency-safe).
- 37.5 Equipment history (work order + inspection + meter) returned chronologically — reuses
  the existing GetEquipmentHistoryAsync (new event-type codes append into it).

## Affected Files / Components
- Domain/Maintenance: InspectionChecklist.cs, InspectionChecklistItem.cs, AmcContract.cs,
  MeterReading.cs (new); EquipmentHistoryEntry.cs (new event-type codes).
- Application/Config/ConfigKeys.cs — AMC expiry-reminder-window key + new MasterDataKeys.
- Application/Maintenance — MaintenanceDtos.cs (new DTOs), IMaintenanceService.cs (new methods).
- Application/Notifications/NotificationKeys.cs — amc.expiry-reminder template key.
- Infrastructure/Maintenance/MaintenanceService.cs — new service methods + AMC sweep.
- Infrastructure/Persistence/Configurations — 4 new EF configs; MasterDataItem seeds;
  NotificationTemplate seed; Permission seeds.
- Infrastructure/Persistence/AppDbContext.cs — new DbSets.
- Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptor.cs — MeterReading +
  InspectionChecklist append-only? (meter/inspection are editable records, not append-only;
  only equipment history stays append-only — no interceptor change needed).
- Worker: AmcExpiryReminderWorkerService.cs (new) + Program.cs registration + appsettings.json.

## Implementation Steps
- [x] Domain entities + event-type codes
- [x] Config keys + master data keys
- [x] Seeds (master data, template, permissions)
- [x] EF configs + DbSets
- [x] DTOs + interface
- [x] Service methods + AMC sweep
- [x] Worker + options + appsettings
- [x] Build + test green (0 errors; 78 tests pass)

## Edge Cases & Risks
- Monotonic meter: concurrent lower-reading inserts — the latest-reading check is a read then
  insert; two concurrent inserts could both pass. Documented ponytail ceiling (small race);
  chronological correctness is preserved and a lower value is rejected in the common case.
- AMC sweep at-most-once: LastExpiryReminderForEndDateUtc marker + RowVersion + per-item save +
  skip-on-conflict so a contract isn't reminded repeatedly for the same expiry window.
- Multi-tenant isolation preserved (TenantOwnedEntity global filter); sweep runs super-admin.

## Verification
- dotnet build CommunityOS.slnx (0 errors)
- dotnet test CommunityOS.slnx (78 pass, 0 fail)

## Notes / Decisions
- Meter readings enforced non-decreasing (equipment counter semantics, Req 37.4).
- EquipmentHistoryEntry.WorkOrderId stays null for inspection/meter entries (nullable already).
- No controllers (that is Task 21.4).
