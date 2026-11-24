-- Migration 00000003: Add PropertyType to Organizations, OrganizationId/Scope to Announcements,
-- and Seed Communication / SMTP & SMS Testing Menu with Exclusive Superadmin Rights

PRINT 'Executing 00000003_add_announcement_scope_and_org_property_type.sql...';
GO

-- =====================================================================================================
-- 1. SCHEMA CHANGES: Organizations & Announcements
-- =====================================================================================================

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Organizations') AND name = 'PropertyType')
BEGIN
    ALTER TABLE dbo.Organizations ADD PropertyType NVARCHAR(100) NULL;
    PRINT 'Added PropertyType column to dbo.Organizations.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Announcements') AND name = 'OrganizationId')
BEGIN
    ALTER TABLE dbo.Announcements ADD OrganizationId UNIQUEIDENTIFIER NULL;
    PRINT 'Added OrganizationId column to dbo.Announcements.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Announcements') AND name = 'Scope')
BEGIN
    ALTER TABLE dbo.Announcements ADD Scope NVARCHAR(50) NULL;
    PRINT 'Added Scope column to dbo.Announcements.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Announcements_OrganizationId_PublishedAtUtc' AND object_id = OBJECT_ID('dbo.Announcements'))
BEGIN
    CREATE INDEX IX_Announcements_OrganizationId_PublishedAtUtc ON dbo.Announcements (OrganizationId, PublishedAtUtc);
    PRINT 'Created index IX_Announcements_OrganizationId_PublishedAtUtc.';
END
GO

-- =====================================================================================================
-- 2. SEED MENU ITEM: admin.communication-test (Email & SMS Testing)
-- =====================================================================================================
PRINT 'Seeding Communication / SMTP & SMS Testing Menu Item...';
GO

DECLARE @AdminGroupId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = 'admin.group.administration');

IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.communication-test')
BEGIN
    INSERT INTO dbo.MenuItems (
        Id,
        ParentId,
        [Key],
        Label,
        Icon,
        Route,
        Surface,
        RequiredPermissionResource,
        RequiredPermissionActions,
        SortOrder,
        IsActive,
        IsDeleted,
        ShowInSidebar,
        CreatedAtUtc,
        CreatedBy
    )
    VALUES (
        'c4e82b71-3f41-4796-9310-09c37e6da481',
        @AdminGroupId,
        'admin.communication-test',
        N'Email & SMS Testing',
        'SendOutlined',
        '/communication-test',
        1, -- Surface 1 = Web / Admin
        'admin.communication-test',
        '0,1', -- 0 = View, 1 = Edit/Execute
        40,
        1,
        0,
        1,
        SYSUTCDATETIME(),
        'migration-00000003'
    );
    PRINT 'Inserted admin.communication-test menu item under Administration group.';
END
ELSE
BEGIN
    UPDATE dbo.MenuItems
    SET ParentId = @AdminGroupId,
        Label = N'Email & SMS Testing',
        Icon = 'SendOutlined',
        Route = '/communication-test',
        Surface = 1,
        RequiredPermissionResource = 'admin.communication-test',
        RequiredPermissionActions = '0,1',
        SortOrder = 40,
        IsActive = 1,
        IsDeleted = 0,
        ShowInSidebar = 1,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE [Key] = 'admin.communication-test';
    PRINT 'Updated admin.communication-test menu item.';
END
GO

-- =====================================================================================================
-- 3. SEED PERMISSIONS: admin.communication-test
-- =====================================================================================================
PRINT 'Seeding Permissions for admin.communication-test...';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE ResourceType = 'admin.communication-test' AND Action = 0 AND IsDeleted = 0)
BEGIN
    INSERT INTO dbo.Permissions (Id, ResourceType, Action, IsDeleted, CreatedAtUtc, CreatedBy)
    VALUES (NEWID(), 'admin.communication-test', 0, 0, SYSUTCDATETIME(), 'migration-00000003');
    PRINT 'Created Permission admin.communication-test: View (0)';
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE ResourceType = 'admin.communication-test' AND Action = 1 AND IsDeleted = 0)
BEGIN
    INSERT INTO dbo.Permissions (Id, ResourceType, Action, IsDeleted, CreatedAtUtc, CreatedBy)
    VALUES (NEWID(), 'admin.communication-test', 1, 0, SYSUTCDATETIME(), 'migration-00000003');
    PRINT 'Created Permission admin.communication-test: Edit/Execute (1)';
END
GO

-- =====================================================================================================
-- 4. GRANT RIGHTS ONLY TO SUPERADMIN
-- =====================================================================================================
PRINT 'Assigning admin.communication-test permissions ONLY to Superadmin role...';
GO

DECLARE @SuperadminRoleId UNIQUEIDENTIFIER = (
    SELECT TOP 1 Id FROM dbo.Roles 
    WHERE (Name = 'Superadmin' OR Name = 'SuperAdmin' OR Name = 'PowerUser') 
      AND CommunityId IS NULL
);

IF @SuperadminRoleId IS NOT NULL
BEGIN
    -- Ensure Superadmin has both View (0) and Edit (1) permissions
    INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
    SELECT @SuperadminRoleId, p.Id
    FROM dbo.Permissions p
    WHERE p.ResourceType = 'admin.communication-test'
      AND p.IsDeleted = 0
      AND NOT EXISTS (
          SELECT 1 FROM dbo.RolePermissions rp
          WHERE rp.RoleId = @SuperadminRoleId AND rp.PermissionId = p.Id
      );

    -- Remove any rights to this resource from any other roles to ensure Superadmin-only access
    DELETE FROM dbo.RolePermissions
    WHERE PermissionId IN (
        SELECT Id FROM dbo.Permissions WHERE ResourceType = 'admin.communication-test'
    )
    AND RoleId <> @SuperadminRoleId;

    PRINT 'Successfully assigned admin.communication-test permissions exclusively to Superadmin.';
END
ELSE
BEGIN
    PRINT 'WARNING: Superadmin role was not found; permissions could not be assigned.';
END
GO

PRINT '00000003_add_announcement_scope_and_org_property_type.sql executed successfully.';
GO
