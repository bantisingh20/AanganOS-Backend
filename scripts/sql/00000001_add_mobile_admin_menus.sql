-- =====================================================================================================
-- Migration Script: 00000001_add_mobile_admin_menus.sql
-- Purpose: Add Admin Group and Resident Management/Approvals Menu Items for Mobile App (Surface = 0)
-- Safe & Idempotent (can be executed multiple times without duplicates or errors)
-- =====================================================================================================

PRINT 'Executing 00000001_add_mobile_admin_menus.sql...';
GO

-- 1. Ensure 'app.services' exists as parent container
DECLARE @ServicesParentId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = 'app.services');

-- 2. Add 'app.group.admin' (Admin & Society Management group container under Services)
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.group.admin')
BEGIN
    INSERT INTO dbo.MenuItems (
        Id, ParentId, [Key], Label, Icon, Route, Surface,
        RequiredPermissionResource, RequiredPermissionActions,
        SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy
    )
    VALUES (
        NEWID(),
        @ServicesParentId,
        'app.group.admin',
        N'Admin & Management',
        'shield',
        NULL,
        0, -- App Surface
        NULL,
        NULL,
        50,
        1,
        0,
        1,
        SYSUTCDATETIME(),
        'migration-00000001'
    );
    PRINT 'Inserted menu group: app.group.admin';
END
ELSE
BEGIN
    PRINT 'Menu group app.group.admin already exists.';
END
GO

-- 3. Add 'app.admin.residents' (Manage Residents tile)
DECLARE @AdminGroupId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = 'app.group.admin');

IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.admin.residents')
BEGIN
    INSERT INTO dbo.MenuItems (
        Id, ParentId, [Key], Label, Icon, Route, Surface,
        RequiredPermissionResource, RequiredPermissionActions,
        SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy
    )
    VALUES (
        NEWID(),
        @AdminGroupId,
        'app.admin.residents',
        N'Manage Residents',
        'people',
        'manage-residents',
        0, -- App Surface
        'app.admin.residents',
        '0,7',
        10,
        1,
        0,
        1,
        SYSUTCDATETIME(),
        'migration-00000001'
    );
    PRINT 'Inserted menu item: app.admin.residents';
END
ELSE
BEGIN
    PRINT 'Menu item app.admin.residents already exists.';
END
GO

-- 4. Add 'app.admin.resident-approvals' (Resident Approvals tile)
DECLARE @AdminGroupId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = 'app.group.admin');

IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.admin.resident-approvals')
BEGIN
    INSERT INTO dbo.MenuItems (
        Id, ParentId, [Key], Label, Icon, Route, Surface,
        RequiredPermissionResource, RequiredPermissionActions,
        SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy
    )
    VALUES (
        NEWID(),
        @AdminGroupId,
        'app.admin.resident-approvals',
        N'Resident Approvals',
        'person-add',
        'resident-approvals',
        0, -- App Surface
        'app.admin.resident-approvals',
        '0,1,7',
        20,
        1,
        0,
        1,
        SYSUTCDATETIME(),
        'migration-00000001'
    );
    PRINT 'Inserted menu item: app.admin.resident-approvals';
END
ELSE
BEGIN
    PRINT 'Menu item app.admin.resident-approvals already exists.';
END
GO

-- 5. Seed Permissions Catalog for the new Menu Items
PRINT 'Seeding Permissions for new Admin Menu items...';
GO

INSERT INTO dbo.Permissions (Id, ResourceType, Action, IsDeleted, CreatedAtUtc, CreatedBy)
SELECT NEWID(), m.RequiredPermissionResource, a.val, 0, SYSUTCDATETIME(), 'migration-00000001'
FROM dbo.MenuItems m
CROSS APPLY (
    SELECT CAST(value AS INT) AS val 
    FROM STRING_SPLIT(m.RequiredPermissionActions, ',')
) a
WHERE m.[Key] IN ('app.admin.residents', 'app.admin.resident-approvals')
  AND m.RequiredPermissionResource IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.Permissions p 
      WHERE p.ResourceType = m.RequiredPermissionResource AND p.Action = a.val
  );
GO

-- 6. Grant Permissions to Admin Roles (Superadmin, OrganizationAdmin, CommunityAdmin)
PRINT 'Granting Permissions to Admin Roles...';
GO

-- 6.1 Superadmin
INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT 
    r.Id,
    p.Id
FROM dbo.Roles r
CROSS JOIN dbo.Permissions p
WHERE r.Name = 'Superadmin'
  AND p.ResourceType IN ('app.admin.residents', 'app.admin.resident-approvals')
  AND NOT EXISTS (
      SELECT 1 FROM dbo.RolePermissions rp 
      WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id
  );
GO

-- 6.2 OrganizationAdmin
INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT 
    r.Id,
    p.Id
FROM dbo.Roles r
CROSS JOIN dbo.Permissions p
WHERE r.Name = 'OrganizationAdmin'
  AND p.ResourceType IN ('app.admin.residents', 'app.admin.resident-approvals')
  AND NOT EXISTS (
      SELECT 1 FROM dbo.RolePermissions rp 
      WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id
  );
GO

-- 6.3 CommunityAdmin
INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT 
    r.Id,
    p.Id
FROM dbo.Roles r
CROSS JOIN dbo.Permissions p
WHERE r.Name = 'CommunityAdmin'
  AND p.ResourceType IN ('app.admin.residents', 'app.admin.resident-approvals')
  AND NOT EXISTS (
      SELECT 1 FROM dbo.RolePermissions rp 
      WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id
  );
GO

PRINT '00000001_add_mobile_admin_menus.sql executed successfully.';
GO
