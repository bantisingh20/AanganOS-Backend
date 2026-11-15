-- =====================================================================================================
-- Migration Script: 00000002_consolidate_common_menus_and_permissions.sql
-- Purpose: 
--   1. Consolidate duplicate Web (Surface=1) and Mobile (Surface=0) menus into Common menus (Surface=2)
--   2. Remove duplicate permissions and seed canonical common permissions (announcements, billing, visitors, tickets, residents, notifications, amenities)
--   3. Migrate existing RolePermissions so all roles keep their access with zero double rights
--   4. Retire artificial app.services container (Services tab in mobile displays permitted tiles by default)
-- Safe & Idempotent (can be executed multiple times without errors or data duplication)
-- =====================================================================================================

PRINT 'Executing 00000002_consolidate_common_menus_and_permissions.sql...';
GO

-- =====================================================================================================
-- 1. UN-NEST MOBILE GROUPS & RETIRE app.services CONTAINER
-- Services tab is default: whatever permission login person has will show in services.
-- =====================================================================================================
PRINT '1. Un-nesting mobile groups and retiring app.services container...';
GO

DECLARE @ServicesId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = 'app.services');

IF @ServicesId IS NOT NULL
BEGIN
    -- Re-parent all direct children of app.services to ROOT (ParentId = NULL)
    UPDATE dbo.MenuItems
    SET ParentId = NULL,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE ParentId = @ServicesId;

    -- Soft-delete app.services
    UPDATE dbo.MenuItems
    SET IsActive = 0,
        IsDeleted = 1,
        ShowInSidebar = 0,
        DeletedAtUtc = SYSUTCDATETIME()
    WHERE Id = @ServicesId AND IsDeleted = 0;

    PRINT 'Retired app.services container and re-parented child groups to root.';
END
GO

-- =====================================================================================================
-- 2. CONSOLIDATE DUPLICATE MENUS INTO COMMON MENUS (Surface = 2: Common)
-- Surface values: 0 = App (Mobile), 1 = Web (Admin), 2 = Common (Web & Mobile)
-- =====================================================================================================
PRINT '2. Consolidating duplicate menus into Common menus (Surface = 2)...';
GO

-- 2.1 ANNOUNCEMENTS
-- Step A: Soft-delete duplicate app.announcements
UPDATE dbo.MenuItems
SET IsActive = 0, IsDeleted = 1, ShowInSidebar = 0, DeletedAtUtc = SYSUTCDATETIME()
WHERE [Key] = 'app.announcements' AND IsDeleted = 0;

-- Step B: Consolidate admin.announcements to Common announcements
IF EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.announcements')
BEGIN
    UPDATE dbo.MenuItems
    SET [Key] = 'announcements',
        Label = N'Announcements',
        Icon = 'megaphone',
        Route = '/announcements',
        Surface = 2, -- Common
        RequiredPermissionResource = 'announcements',
        RequiredPermissionActions = '0,1,2,3,4,7',
        IsActive = 1,
        IsDeleted = 0,
        ShowInSidebar = 1,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE [Key] = 'admin.announcements';
    PRINT 'Consolidated announcements to Common menu.';
END
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'announcements' AND IsDeleted = 0)
BEGIN
    INSERT INTO dbo.MenuItems (
        Id, ParentId, [Key], Label, Icon, Route, Surface,
        RequiredPermissionResource, RequiredPermissionActions,
        SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy
    )
    VALUES (
        NEWID(),
        (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = 'admin.group.operations'),
        'announcements',
        N'Announcements',
        'megaphone',
        '/announcements',
        2, -- Common
        'announcements',
        '0,1,2,3,4,7',
        25,
        1,
        0,
        1,
        SYSUTCDATETIME(),
        'migration-00000002'
    );
    PRINT 'Inserted Common announcements menu.';
END
ELSE
BEGIN
    UPDATE dbo.MenuItems
    SET Surface = 2,
        RequiredPermissionResource = 'announcements',
        RequiredPermissionActions = '0,1,2,3,4,7',
        IsActive = 1,
        IsDeleted = 0,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE [Key] = 'announcements' AND IsDeleted = 0;
END
GO

-- 2.2 MAINTENANCE BILLING
-- Step A: Soft-delete duplicate app.billing and its submenus
UPDATE dbo.MenuItems
SET IsActive = 0, IsDeleted = 1, ShowInSidebar = 0, DeletedAtUtc = SYSUTCDATETIME()
WHERE [Key] LIKE 'app.billing%' AND IsDeleted = 0;

-- Step B: Consolidate admin.billing to Common billing
IF EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.billing')
BEGIN
    UPDATE dbo.MenuItems
    SET [Key] = 'billing',
        Label = N'Maintenance Billing',
        Icon = 'wallet',
        Route = '/billing',
        Surface = 2, -- Common
        RequiredPermissionResource = 'billing',
        RequiredPermissionActions = '0,1,2,3,4,7',
        IsActive = 1,
        IsDeleted = 0,
        ShowInSidebar = 1,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE [Key] = 'admin.billing';
    PRINT 'Consolidated billing to Common menu.';
END
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'billing' AND IsDeleted = 0)
BEGIN
    INSERT INTO dbo.MenuItems (
        Id, ParentId, [Key], Label, Icon, Route, Surface,
        RequiredPermissionResource, RequiredPermissionActions,
        SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy
    )
    VALUES (
        NEWID(),
        (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = 'admin.group.operations'),
        'billing',
        N'Maintenance Billing',
        'wallet',
        '/billing',
        2, -- Common
        'billing',
        '0,1,2,3,4,7',
        40,
        1,
        0,
        1,
        SYSUTCDATETIME(),
        'migration-00000002'
    );
    PRINT 'Inserted Common billing menu.';
END
ELSE
BEGIN
    UPDATE dbo.MenuItems
    SET Surface = 2,
        RequiredPermissionResource = 'billing',
        RequiredPermissionActions = '0,1,2,3,4,7',
        IsActive = 1,
        IsDeleted = 0,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE [Key] = 'billing' AND IsDeleted = 0;
END
GO

-- 2.3 VISITORS
-- Step A: Soft-delete duplicate app.visitors.my
UPDATE dbo.MenuItems
SET IsActive = 0, IsDeleted = 1, ShowInSidebar = 0, DeletedAtUtc = SYSUTCDATETIME()
WHERE [Key] = 'app.visitors.my' AND IsDeleted = 0;

-- Step B: Consolidate admin.visitors to Common visitors
IF EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.visitors')
BEGIN
    UPDATE dbo.MenuItems
    SET [Key] = 'visitors',
        Label = N'Visitors',
        Icon = 'idcard',
        Route = '/visitors',
        Surface = 2, -- Common
        RequiredPermissionResource = 'visitors',
        RequiredPermissionActions = '0,1,2,3,4,7',
        IsActive = 1,
        IsDeleted = 0,
        ShowInSidebar = 1,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE [Key] = 'admin.visitors';
    PRINT 'Consolidated visitors to Common menu.';
END
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'visitors' AND IsDeleted = 0)
BEGIN
    INSERT INTO dbo.MenuItems (
        Id, ParentId, [Key], Label, Icon, Route, Surface,
        RequiredPermissionResource, RequiredPermissionActions,
        SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy
    )
    VALUES (
        NEWID(),
        (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = 'admin.group.operations'),
        'visitors',
        N'Visitors',
        'idcard',
        '/visitors',
        2, -- Common
        'visitors',
        '0,1,2,3,4,7',
        20,
        1,
        0,
        1,
        SYSUTCDATETIME(),
        'migration-00000002'
    );
    PRINT 'Inserted Common visitors menu.';
END
ELSE
BEGIN
    UPDATE dbo.MenuItems
    SET Surface = 2,
        RequiredPermissionResource = 'visitors',
        RequiredPermissionActions = '0,1,2,3,4,7',
        IsActive = 1,
        IsDeleted = 0,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE [Key] = 'visitors' AND IsDeleted = 0;
END
GO

-- 2.4 HELP DESK / TICKETS
-- Step A: Soft-delete duplicate app.helpdesk (app.maintenance is PRESERVED for resident personal bills)
UPDATE dbo.MenuItems
SET IsActive = 0, IsDeleted = 1, ShowInSidebar = 0, DeletedAtUtc = SYSUTCDATETIME()
WHERE [Key] = 'app.helpdesk' AND IsDeleted = 0;

-- Step B: Ensure 'app.maintenance' (My Maintenance) is active and properly mapped for residents
IF EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.maintenance')
BEGIN
    UPDATE dbo.MenuItems
    SET Label = N'My Maintenance',
        Icon = 'card',
        Route = '/maintenance',
        Surface = 0, -- App / Mobile
        RequiredPermissionResource = 'maintenance',
        RequiredPermissionActions = '0,7',
        IsActive = 1,
        IsDeleted = 0,
        ShowInSidebar = 1,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE [Key] = 'app.maintenance';
    PRINT 'Restored and verified My Maintenance menu for residents.';
END

-- Step C: Consolidate admin.maintenance to Common tickets
IF EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.maintenance')
BEGIN
    UPDATE dbo.MenuItems
    SET [Key] = 'tickets',
        Label = N'Help Desk',
        Icon = 'tool',
        Route = '/maintenance',
        Surface = 2, -- Common
        RequiredPermissionResource = 'tickets',
        RequiredPermissionActions = '0,1,2,3,4,7',
        IsActive = 1,
        IsDeleted = 0,
        ShowInSidebar = 1,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE [Key] = 'admin.maintenance';
    PRINT 'Consolidated maintenance to Common tickets menu.';
END
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'tickets' AND IsDeleted = 0)
BEGIN
    INSERT INTO dbo.MenuItems (
        Id, ParentId, [Key], Label, Icon, Route, Surface,
        RequiredPermissionResource, RequiredPermissionActions,
        SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy
    )
    VALUES (
        NEWID(),
        (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = 'admin.group.operations'),
        'tickets',
        N'Help Desk',
        'tool',
        '/maintenance',
        2, -- Common
        'tickets',
        '0,1,2,3,4,7',
        10,
        1,
        0,
        1,
        SYSUTCDATETIME(),
        'migration-00000002'
    );
    PRINT 'Inserted Common tickets menu.';
END
ELSE
BEGIN
    UPDATE dbo.MenuItems
    SET Surface = 2,
        RequiredPermissionResource = 'tickets',
        RequiredPermissionActions = '0,1,2,3,4,7',
        IsActive = 1,
        IsDeleted = 0,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE [Key] = 'tickets' AND IsDeleted = 0;
END
GO

-- 2.5 RESIDENTS
-- Step A: Soft-delete duplicate app.admin.residents
UPDATE dbo.MenuItems
SET IsActive = 0, IsDeleted = 1, ShowInSidebar = 0, DeletedAtUtc = SYSUTCDATETIME()
WHERE [Key] = 'app.admin.residents' AND IsDeleted = 0;

-- Step B: Consolidate admin.residents to Common residents
IF EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.residents')
BEGIN
    UPDATE dbo.MenuItems
    SET [Key] = 'residents',
        Label = N'Residents',
        Icon = 'team',
        Route = '/residents',
        Surface = 2, -- Common
        RequiredPermissionResource = 'residents',
        RequiredPermissionActions = '0,1,2,3,4,7',
        IsActive = 1,
        IsDeleted = 0,
        ShowInSidebar = 1,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE [Key] = 'admin.residents';
    PRINT 'Consolidated residents to Common menu.';
END
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'residents' AND IsDeleted = 0)
BEGIN
    INSERT INTO dbo.MenuItems (
        Id, ParentId, [Key], Label, Icon, Route, Surface,
        RequiredPermissionResource, RequiredPermissionActions,
        SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy
    )
    VALUES (
        NEWID(),
        (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = 'admin.group.community'),
        'residents',
        N'Residents',
        'team',
        '/residents',
        2, -- Common
        'residents',
        '0,1,2,3,4,7',
        30,
        1,
        0,
        1,
        SYSUTCDATETIME(),
        'migration-00000002'
    );
    PRINT 'Inserted Common residents menu.';
END
ELSE
BEGIN
    UPDATE dbo.MenuItems
    SET Surface = 2,
        RequiredPermissionResource = 'residents',
        RequiredPermissionActions = '0,1,2,3,4,7',
        IsActive = 1,
        IsDeleted = 0,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE [Key] = 'residents' AND IsDeleted = 0;
END
GO

-- 2.6 NOTIFICATIONS
-- Step A: Soft-delete duplicate app.notifications
UPDATE dbo.MenuItems
SET IsActive = 0, IsDeleted = 1, ShowInSidebar = 0, DeletedAtUtc = SYSUTCDATETIME()
WHERE [Key] = 'app.notifications' AND IsDeleted = 0;

-- Step B: Consolidate admin.notifications to Common notifications
IF EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.notifications')
BEGIN
    UPDATE dbo.MenuItems
    SET [Key] = 'notifications',
        Label = N'Notifications',
        Icon = 'bell',
        Route = '/notifications',
        Surface = 2, -- Common
        RequiredPermissionResource = 'notifications',
        RequiredPermissionActions = '0,1,2,3,4,7',
        IsActive = 1,
        IsDeleted = 0,
        ShowInSidebar = 1,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE [Key] = 'admin.notifications';
    PRINT 'Consolidated notifications to Common menu.';
END
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'notifications' AND IsDeleted = 0)
BEGIN
    INSERT INTO dbo.MenuItems (
        Id, ParentId, [Key], Label, Icon, Route, Surface,
        RequiredPermissionResource, RequiredPermissionActions,
        SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy
    )
    VALUES (
        NEWID(),
        (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = 'admin.group.operations'),
        'notifications',
        N'Notifications',
        'bell',
        '/notifications',
        2, -- Common
        'notifications',
        '0,1,2,3,4,7',
        40,
        1,
        0,
        1,
        SYSUTCDATETIME(),
        'migration-00000002'
    );
    PRINT 'Inserted Common notifications menu.';
END
ELSE
BEGIN
    UPDATE dbo.MenuItems
    SET Surface = 2,
        RequiredPermissionResource = 'notifications',
        RequiredPermissionActions = '0,1,2,3,4,7',
        IsActive = 1,
        IsDeleted = 0,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE [Key] = 'notifications' AND IsDeleted = 0;
END
GO

-- 2.7 AMENITIES
IF EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.amenities')
BEGIN
    UPDATE dbo.MenuItems
    SET [Key] = 'amenities',
        Label = N'Amenities',
        Icon = 'coffee',
        Route = '/amenities',
        Surface = 2, -- Common
        RequiredPermissionResource = 'amenities',
        RequiredPermissionActions = '0,1,2,3,4,7',
        IsActive = 1,
        IsDeleted = 0,
        ShowInSidebar = 1,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE [Key] = 'admin.amenities';
    PRINT 'Updated amenities to Common menu.';
END
GO

-- 2.8 Common Surface on Shared Functional Groups
UPDATE dbo.MenuItems
SET Surface = 2, UpdatedAtUtc = SYSUTCDATETIME()
WHERE [Key] IN ('admin.group.community', 'admin.group.operations');
GO

-- =====================================================================================================
-- 3. SEED CANONICAL PERMISSIONS CATALOG FOR COMMON RESOURCES
-- Actions: 0=View, 1=Create, 2=Edit, 3=Delete, 4=Approve, 7=Access
-- =====================================================================================================
PRINT '3. Seeding Canonical Permissions for Common resources...';
GO

DECLARE @CanonicalResources TABLE (Resource NVARCHAR(128));
INSERT INTO @CanonicalResources (Resource)
VALUES 
    ('announcements'),
    ('billing'),
    ('maintenance'),
    ('visitors'),
    ('tickets'),
    ('residents'),
    ('notifications'),
    ('amenities');

DECLARE @Actions TABLE (ActionVal INT);
INSERT INTO @Actions (ActionVal)
VALUES (0), (1), (2), (3), (4), (7);

INSERT INTO dbo.Permissions (Id, ResourceType, Action, IsDeleted, CreatedAtUtc, CreatedBy)
SELECT NEWID(), r.Resource, a.ActionVal, 0, SYSUTCDATETIME(), 'migration-00000002'
FROM @CanonicalResources r
CROSS JOIN @Actions a
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Permissions p
    WHERE p.ResourceType = r.Resource AND p.Action = a.ActionVal
);
GO

-- =====================================================================================================
-- 4. MIGRATE ROLE PERMISSIONS (Zero Double Rights)
-- Automatically map any role holding old duplicate permissions to the canonical Common permission!
-- =====================================================================================================
PRINT '4. Migrating RolePermissions to Common resources...';
GO

DECLARE @PermMapping TABLE (OldResource NVARCHAR(128), NewResource NVARCHAR(128));
INSERT INTO @PermMapping (OldResource, NewResource)
VALUES
    ('admin.announcements', 'announcements'),
    ('app.announcements',   'announcements'),
    ('admin.billing',       'billing'),
    ('app.billing',         'billing'),
    ('app.maintenance',     'maintenance'),
    ('admin.visitors',      'visitors'),
    ('app.visitors.my',     'visitors'),
    ('app.visitors',        'visitors'),
    ('admin.maintenance',   'tickets'),
    ('app.helpdesk',        'tickets'),
    ('admin.residents',     'residents'),
    ('app.admin.residents', 'residents'),
    ('admin.notifications', 'notifications'),
    ('app.notifications',   'notifications'),
    ('admin.amenities',     'amenities');

-- Insert missing grants for roles that held any old permission
INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT DISTINCT
    rp.RoleId,
    newPerm.Id
FROM dbo.RolePermissions rp
INNER JOIN dbo.Permissions oldPerm ON rp.PermissionId = oldPerm.Id
INNER JOIN @PermMapping m ON oldPerm.ResourceType = m.OldResource
INNER JOIN dbo.Permissions newPerm ON newPerm.ResourceType = m.NewResource AND newPerm.Action = oldPerm.Action
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.RolePermissions existing
    WHERE existing.RoleId = rp.RoleId AND existing.PermissionId = newPerm.Id
);

PRINT 'Migrated existing RolePermissions to canonical Common permissions.';
GO

-- 4.2 Ensure Resident role has My Maintenance permissions (View & Access)
DECLARE @ResidentRoleId UNIQUEIDENTIFIER = (
    SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Resident' AND CommunityId IS NULL
);

IF @ResidentRoleId IS NOT NULL
BEGIN
    INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
    SELECT @ResidentRoleId, p.Id
    FROM dbo.Permissions p
    WHERE p.ResourceType = 'maintenance' AND p.Action IN (0, 7)
      AND NOT EXISTS (
          SELECT 1 FROM dbo.RolePermissions rp
          WHERE rp.RoleId = @ResidentRoleId AND rp.PermissionId = p.Id
      );
    PRINT 'Granted personal maintenance permissions (0, 7) to Resident role.';
END
GO

-- 4.3 Ensure Superadmin (PowerUser) role has all canonical permissions
DECLARE @SuperadminRoleId UNIQUEIDENTIFIER = (
    SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Superadmin' AND CommunityId IS NULL
);

IF @SuperadminRoleId IS NOT NULL
BEGIN
    INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
    SELECT @SuperadminRoleId, p.Id
    FROM dbo.Permissions p
    WHERE p.ResourceType IN ('announcements', 'billing', 'maintenance', 'visitors', 'tickets', 'residents', 'notifications', 'amenities')
      AND NOT EXISTS (
          SELECT 1 FROM dbo.RolePermissions rp
          WHERE rp.RoleId = @SuperadminRoleId AND rp.PermissionId = p.Id
      );
    PRINT 'Granted all canonical permissions to Superadmin.';
END
GO

-- 4.4 Soft-delete deprecated duplicate permission entries (excluding maintenance)
PRINT 'Retiring deprecated duplicate permissions...';
GO

UPDATE dbo.Permissions
SET IsDeleted = 1
WHERE ResourceType IN (
    'admin.announcements', 'app.announcements',
    'admin.billing', 'app.billing',
    'admin.visitors', 'app.visitors.my',
    'admin.maintenance', 'app.helpdesk',
    'admin.residents', 'app.admin.residents',
    'admin.notifications', 'app.notifications',
    'admin.amenities'
)
AND IsDeleted = 0;
GO

PRINT '00000002_consolidate_common_menus_and_permissions.sql executed successfully.';
GO
