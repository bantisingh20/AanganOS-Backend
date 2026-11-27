-- =====================================================================================================
-- Migration Script: 00000008_endpoint_permission_grants.sql
-- Purpose:
--   Server-side RBAC is back on (Security:EnforcePermissions, PermissionAuthorizationMiddleware).
--   Every API endpoint declares [RequirePermission(Action, "Resource")] with an ENTITY name
--   ("Visitor", "Ticket", ...), but the catalog only had MENU keys ("visitors", "app.walkin", ...).
--   117 of the 250 protected endpoints had no permission row at all, and the seeds gave
--   CommunityAdmin / OrganizationAdmin only 'residents' + 'app.admin.resident-approvals'.
--   With enforcement on, admins would be locked out of almost everything and residents/guards
--   could not raise tickets, invite visitors, verify passes or register walk-ins.
--
--   This script:
--     1. Adds one permission row per (Resource, Action) used by an endpoint (exact entity names,
--        so the check never depends on the evaluator's fuzzy name matching).
--     2. Grants them:
--          Superadmin        : all (it bypasses anyway; keeps the Roles page truthful)
--          OrganizationAdmin : all except the platform-only list below
--          CommunityAdmin    : same as OrganizationAdmin, minus Organization Edit and Community Create
--          Resident          : what the resident app screens call (list in @ResidentGrants)
--          Security          : what the guard app screens call (list in @SecurityGrants)
--     Platform-only (Superadmin): Organization Create/Delete, Community Delete,
--       Role Create/Edit/Delete, UserRole Assign, Configuration Edit, MasterData Edit,
--       error_logs View/Delete, admin.communication-test View/Edit.
--       (These services have no org/community scope guard, so an admin holding them could edit
--        global roles, settings or other organizations.)
--
--   Only ADDS rows. Existing menu permissions and anything granted in the Roles page are untouched.
--   The Roles page saves a role's full current grant set, so these grants survive later edits there.
--   Matrix and reasoning: docs/PERMISSIONS.md.
--
-- Safe & Idempotent.
-- Rollback (removes only what this script added):
--   DELETE rp FROM dbo.RolePermissions rp JOIN dbo.Permissions p ON p.Id = rp.PermissionId
--     WHERE p.CreatedBy = 'migration-00000008';
--   DELETE FROM dbo.Permissions WHERE CreatedBy = 'migration-00000008';
--   (Grants on rows that already existed are not tracked; to fully roll back, also set
--    Security__EnforcePermissions=false.)
-- Actions: 0=View, 1=Create, 2=Edit, 3=Delete, 4=Approve, 5=Export, 6=Assign, 7=Access
-- =====================================================================================================

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- Grants staged in a temp table so the later batches can read them.
IF OBJECT_ID('tempdb..#EndpointPermissions') IS NOT NULL DROP TABLE #EndpointPermissions;
IF OBJECT_ID('tempdb..#Grants') IS NOT NULL DROP TABLE #Grants;
CREATE TABLE #EndpointPermissions (Resource NVARCHAR(128) NOT NULL, Action INT NOT NULL, PRIMARY KEY (Resource, Action));
CREATE TABLE #Grants (RoleName NVARCHAR(64) NOT NULL, Resource NVARCHAR(128) NOT NULL, Action INT NOT NULL, PRIMARY KEY (RoleName, Resource, Action));
GO

-- -----------------------------------------------------------------------------------------------------
-- 1. Every (Resource, Action) declared by [RequirePermission] in src/App.Api/Controllers (152 pairs)
-- -----------------------------------------------------------------------------------------------------
INSERT INTO #EndpointPermissions (Resource, Action) VALUES
('admin.communication-test', 0), ('admin.communication-test', 2),
('AmcContract', 0), ('AmcContract', 1), ('AmcContract', 2),
('Amenity', 0), ('Amenity', 1), ('Amenity', 2), ('Amenity', 3),
('Announcement', 0), ('Announcement', 1), ('Announcement', 2), ('Announcement', 3),
('Asset', 0), ('Asset', 1), ('Asset', 2), ('Asset', 3),
('Audit', 0),
('BlackoutDate', 0), ('BlackoutDate', 1), ('BlackoutDate', 3),
('Booking', 0), ('Booking', 1), ('Booking', 2), ('Booking', 4),
('Community', 0), ('Community', 1), ('Community', 2), ('Community', 3),
('Configuration', 0), ('Configuration', 2),
('CustodyLocation', 1),
('CustodyTimelineEntry', 0),
('EmergencyContact', 0), ('EmergencyContact', 1),
('EntryExitEvent', 0), ('EntryExitEvent', 1),
('error_logs', 0), ('error_logs', 3),
('EvReservation', 0), ('EvReservation', 1), ('EvReservation', 2),
('FacilitySlot', 0), ('FacilitySlot', 1), ('FacilitySlot', 3),
('GuardShift', 0), ('GuardShift', 1),
('HierarchyNode', 0), ('HierarchyNode', 1), ('HierarchyNode', 2), ('HierarchyNode', 3),
('HouseholdMember', 0), ('HouseholdMember', 1),
('Incident', 0), ('Incident', 1), ('Incident', 2),
('InspectionChecklist', 0), ('InspectionChecklist', 1),
('JoinRequest', 0), ('JoinRequest', 4),
('MaintenanceBill', 0), ('MaintenanceBill', 1), ('MaintenanceBill', 2),
('MaintenanceCharge', 0), ('MaintenanceCharge', 1), ('MaintenanceCharge', 2),
('MaintenancePayment', 0), ('MaintenancePayment', 1), ('MaintenancePayment', 4),
('MaintenancePlan', 0), ('MaintenancePlan', 1), ('MaintenancePlan', 2),
('MasterData', 2),
('MeterReading', 0), ('MeterReading', 1),
('Organization', 0), ('Organization', 1), ('Organization', 2), ('Organization', 3),
('OwnershipHistory', 0), ('OwnershipHistory', 1),
('Parcel', 0), ('Parcel', 1), ('Parcel', 2),
('ParcelAuthorization', 1),
('ParkingAssignment', 0), ('ParkingAssignment', 6),
('ParkingSlot', 0), ('ParkingSlot', 1), ('ParkingSlot', 2), ('ParkingSlot', 6),
('ParkingViolation', 0), ('ParkingViolation', 1), ('ParkingViolation', 2),
('ParkingZone', 0), ('ParkingZone', 1),
('Permission', 0),
('Pet', 0), ('Pet', 1),
('RecurringVisitor', 0), ('RecurringVisitor', 1),
('Resident', 0), ('Resident', 1), ('Resident', 2), ('Resident', 3), ('Resident', 4),
('ResidentEvent', 0), ('ResidentEvent', 1),
('Role', 0), ('Role', 1), ('Role', 2), ('Role', 3),
('SosAlert', 0), ('SosAlert', 1), ('SosAlert', 2),
('StorageLocation', 0), ('StorageLocation', 1), ('StorageLocation', 6),
('TenancyHistory', 0), ('TenancyHistory', 1), ('TenancyHistory', 2),
('Ticket', 0), ('Ticket', 1), ('Ticket', 2), ('Ticket', 6),
('TicketAttachment', 0),
('TicketComment', 0), ('TicketComment', 1),
('Unit', 0), ('Unit', 1), ('Unit', 2), ('Unit', 3), ('Unit', 5),
('User', 0), ('User', 1), ('User', 2), ('User', 3),
('UserRole', 0), ('UserRole', 6),
('Vehicle', 0), ('Vehicle', 1),
('Visitor', 0), ('Visitor', 1), ('Visitor', 4),
('VisitPass', 0), ('VisitPass', 1),
('WatchlistEntry', 0), ('WatchlistEntry', 1),
('WorkOrder', 0), ('WorkOrder', 1), ('WorkOrder', 2), ('WorkOrder', 6);
GO

-- -----------------------------------------------------------------------------------------------------
-- 2. Role grants
-- -----------------------------------------------------------------------------------------------------
-- 2.1 Superadmin: everything.
INSERT INTO #Grants (RoleName, Resource, Action)
SELECT 'Superadmin', Resource, Action FROM #EndpointPermissions;

-- 2.2 Admins: everything except the platform-only list.
DECLARE @PlatformOnly TABLE (Resource NVARCHAR(128), Action INT);
INSERT INTO @PlatformOnly (Resource, Action) VALUES
('Organization', 1), ('Organization', 3),
('Community', 3),
('Role', 1), ('Role', 2), ('Role', 3),
('UserRole', 6),
('Configuration', 2),
('MasterData', 2),
('error_logs', 0), ('error_logs', 3),
('admin.communication-test', 0), ('admin.communication-test', 2);

INSERT INTO #Grants (RoleName, Resource, Action)
SELECT r.RoleName, e.Resource, e.Action
FROM #EndpointPermissions e
CROSS JOIN (VALUES ('OrganizationAdmin'), ('CommunityAdmin')) r(RoleName)
WHERE NOT EXISTS (SELECT 1 FROM @PlatformOnly x WHERE x.Resource = e.Resource AND x.Action = e.Action)
  -- A community admin manages ONE community: it cannot edit the organization or create communities.
  AND NOT (r.RoleName = 'CommunityAdmin' AND ((e.Resource = 'Organization' AND e.Action = 2) OR (e.Resource = 'Community' AND e.Action = 1)));
GO

-- 2.3 Resident: resident app screens (home, profile, household, helpdesk, maintenance, visitors,
--     invite, approvals, recurring visitors, parcels, my tasks) plus resident self-service features.
INSERT INTO #Grants (RoleName, Resource, Action)
SELECT 'Resident', v.Resource, v.Action FROM (VALUES
-- View
('Announcement', 0), ('Community', 0), ('Organization', 0), ('Unit', 0),
('Resident', 0), ('HouseholdMember', 0), ('EmergencyContact', 0), ('Pet', 0), ('Vehicle', 0),
('Ticket', 0), ('TicketComment', 0), ('TicketAttachment', 0),
('MaintenanceBill', 0), ('MaintenanceCharge', 0), ('MaintenancePayment', 0),
('Visitor', 0), ('VisitPass', 0), ('RecurringVisitor', 0), ('Parcel', 0),
('Amenity', 0), ('FacilitySlot', 0), ('BlackoutDate', 0), ('Booking', 0), ('EvReservation', 0),
('ParkingAssignment', 0),
-- Create
('Ticket', 1), ('TicketComment', 1), ('VisitPass', 1), ('RecurringVisitor', 1),
('Resident', 1), ('HouseholdMember', 1), ('EmergencyContact', 1), ('Pet', 1), ('Vehicle', 1),
('Booking', 1), ('EvReservation', 1), ('SosAlert', 1), ('ParcelAuthorization', 1),
-- Edit (own profile; own bookings)
('Resident', 2), ('Booking', 2), ('EvReservation', 2),
-- Approve walk-in visitors for own unit
('Visitor', 4)
) v(Resource, Action);

-- 2.4 Security: guard app screens (verify pass, walk-in, entry/exit, vehicle lookup, watchlist, SOS)
--     plus the gate-side parcel / incident flows.
INSERT INTO #Grants (RoleName, Resource, Action)
SELECT 'Security', v.Resource, v.Action FROM (VALUES
-- View
('Announcement', 0), ('Community', 0), ('Organization', 0), ('Unit', 0), ('Resident', 0),
('Visitor', 0), ('VisitPass', 0), ('RecurringVisitor', 0), ('EntryExitEvent', 0), ('Vehicle', 0),
('WatchlistEntry', 0), ('SosAlert', 0), ('Parcel', 0), ('GuardShift', 0), ('Incident', 0),
('ParkingViolation', 0),
-- Create
('Visitor', 1), ('EntryExitEvent', 1), ('WatchlistEntry', 1), ('Incident', 1), ('Parcel', 1),
('SosAlert', 1), ('ParkingViolation', 1),
-- Edit (acknowledge/resolve SOS, hand over parcels, update incidents)
('SosAlert', 2), ('Parcel', 2), ('Incident', 2)
) v(Resource, Action);
GO

-- -----------------------------------------------------------------------------------------------------
-- 3. Permission rows (exact entity names) — insert the missing ones
-- -----------------------------------------------------------------------------------------------------
INSERT INTO dbo.Permissions (Id, ResourceType, Action, IsDeleted, CreatedAtUtc, CreatedBy)
SELECT NEWID(), e.Resource, e.Action, 0, SYSUTCDATETIME(), 'migration-00000008'
FROM #EndpointPermissions e
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Permissions p
    WHERE p.ResourceType = e.Resource AND p.Action = e.Action AND p.IsDeleted = 0
);
PRINT CONCAT('00000008: inserted ', @@ROWCOUNT, ' permission row(s).');
GO

-- -----------------------------------------------------------------------------------------------------
-- 4. Role → permission grants (global system roles only: CommunityId IS NULL)
-- -----------------------------------------------------------------------------------------------------
INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT DISTINCT r.Id, p.Id
FROM #Grants g
INNER JOIN dbo.Roles r
    ON r.Name = g.RoleName AND r.CommunityId IS NULL AND r.IsDeleted = 0
CROSS APPLY (
    SELECT TOP 1 p.Id FROM dbo.Permissions p
    WHERE p.ResourceType = g.Resource AND p.Action = g.Action AND p.IsDeleted = 0
    ORDER BY p.CreatedAtUtc
) p
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id
);
PRINT CONCAT('00000008: inserted ', @@ROWCOUNT, ' role grant(s).');
GO

DROP TABLE #Grants;
DROP TABLE #EndpointPermissions;
GO

PRINT '00000008_endpoint_permission_grants.sql executed successfully.';
GO
