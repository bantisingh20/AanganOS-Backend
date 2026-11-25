-- =====================================================================================================
-- Migration Script: 00000004_add_error_logs_and_menu.sql
-- Purpose:
--   1. Create ErrorLogs table to capture backend API call exceptions, errors, line numbers, and user/tenant context
--   2. Add Error Logs menu item under Administration in dbo.MenuItems (Surface = 1: Web Admin)
--   3. Seed error_logs permissions and grant to SuperAdmin & PowerUser roles
-- Safe & Idempotent (can be executed multiple times without errors or data duplication)
-- =====================================================================================================

PRINT 'Executing 00000004_add_error_logs_and_menu.sql...';
GO

-- 1. Create dbo.ErrorLogs table if not exists
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'ErrorLogs' AND schema_id = SCHEMA_ID('dbo'))
BEGIN
    CREATE TABLE dbo.ErrorLogs (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ErrorLogs PRIMARY KEY,
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_ErrorLogs_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        CorrelationId NVARCHAR(128) NULL,
        ApiPath NVARCHAR(512) NOT NULL,
        HttpMethod NVARCHAR(16) NOT NULL,
        StatusCode INT NOT NULL,
        ExceptionType NVARCHAR(256) NOT NULL,
        ErrorMessage NVARCHAR(MAX) NOT NULL,
        StackTrace NVARCHAR(MAX) NULL,
        FileName NVARCHAR(512) NULL,
        ControllerOrFunction NVARCHAR(256) NULL,
        LineNumber INT NULL,
        UserId UNIQUEIDENTIFIER NULL,
        UserName NVARCHAR(256) NULL,
        UserRole NVARCHAR(128) NULL,
        OrganizationId UNIQUEIDENTIFIER NULL,
        CommunityId UNIQUEIDENTIFIER NULL,
        RequestBody NVARCHAR(MAX) NULL,
        QueryParams NVARCHAR(MAX) NULL,
        ClientIp NVARCHAR(64) NULL,
        UserAgent NVARCHAR(512) NULL
    );

    CREATE NONCLUSTERED INDEX IX_ErrorLogs_CreatedAtUtc ON dbo.ErrorLogs (CreatedAtUtc DESC);
    CREATE NONCLUSTERED INDEX IX_ErrorLogs_StatusCode_CreatedAtUtc ON dbo.ErrorLogs (StatusCode, CreatedAtUtc DESC);
    CREATE NONCLUSTERED INDEX IX_ErrorLogs_UserId_CreatedAtUtc ON dbo.ErrorLogs (UserId, CreatedAtUtc DESC);
    CREATE NONCLUSTERED INDEX IX_ErrorLogs_OrganizationId_CreatedAtUtc ON dbo.ErrorLogs (OrganizationId, CreatedAtUtc DESC);
    CREATE NONCLUSTERED INDEX IX_ErrorLogs_CommunityId_CreatedAtUtc ON dbo.ErrorLogs (CommunityId, CreatedAtUtc DESC);

    PRINT 'Created table dbo.ErrorLogs and indexes.';
END
ELSE
BEGIN
    PRINT 'Table dbo.ErrorLogs already exists.';
END
GO

-- 2. Seed Permissions for error_logs (0: View, 3: Delete)
IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE ResourceType = 'error_logs' AND Action = 0)
BEGIN
    INSERT INTO dbo.Permissions (Id, ResourceType, Action, IsDeleted, CreatedAtUtc, CreatedBy)
    VALUES (NEWID(), 'error_logs', 0, 0, SYSUTCDATETIME(), 'migration-00000004');
    PRINT 'Seeded error_logs:0 (View) permission.';
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Permissions WHERE ResourceType = 'error_logs' AND Action = 3)
BEGIN
    INSERT INTO dbo.Permissions (Id, ResourceType, Action, IsDeleted, CreatedAtUtc, CreatedBy)
    VALUES (NEWID(), 'error_logs', 3, 0, SYSUTCDATETIME(), 'migration-00000004');
    PRINT 'Seeded error_logs:3 (Delete) permission.';
END
GO

-- 3. Grant error_logs permissions to SuperAdmin and PowerUser
DECLARE @SuperAdminRoleId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'SuperAdmin');
IF @SuperAdminRoleId IS NOT NULL
BEGIN
    INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
    SELECT @SuperAdminRoleId, p.Id
    FROM dbo.Permissions p
    WHERE p.ResourceType = 'error_logs'
      AND NOT EXISTS (
          SELECT 1 FROM dbo.RolePermissions rp
          WHERE rp.RoleId = @SuperAdminRoleId AND rp.PermissionId = p.Id
      );
    PRINT 'Granted error_logs permissions to SuperAdmin.';
END
GO

DECLARE @PowerUserRoleId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'PowerUser');
IF @PowerUserRoleId IS NOT NULL
BEGIN
    INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
    SELECT @PowerUserRoleId, p.Id
    FROM dbo.Permissions p
    WHERE p.ResourceType = 'error_logs'
      AND NOT EXISTS (
          SELECT 1 FROM dbo.RolePermissions rp
          WHERE rp.RoleId = @PowerUserRoleId AND rp.PermissionId = p.Id
      );
    PRINT 'Granted error_logs permissions to PowerUser.';
END
GO

-- 4. Insert or Update Error Logs Menu Item in dbo.MenuItems
DECLARE @AdminGroupId UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = 'admin.group.administration');

IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.error_logs')
BEGIN
    INSERT INTO dbo.MenuItems (
        Id, ParentId, [Key], Label, Icon, Route, Surface,
        RequiredPermissionResource, RequiredPermissionActions,
        SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy
    )
    VALUES (
        NEWID(),
        @AdminGroupId,
        'admin.error_logs',
        N'Error Logs',
        'alert-triangle',
        '/error-logs',
        1, -- 1: Web Admin
        'error_logs',
        '0,3',
        35,
        1,
        0,
        1,
        SYSUTCDATETIME(),
        'migration-00000004'
    );
    PRINT 'Inserted admin.error_logs menu item into dbo.MenuItems.';
END
ELSE
BEGIN
    UPDATE dbo.MenuItems
    SET ParentId = @AdminGroupId,
        Label = N'Error Logs',
        Icon = 'alert-triangle',
        Route = '/error-logs',
        Surface = 1,
        RequiredPermissionResource = 'error_logs',
        RequiredPermissionActions = '0,3',
        IsActive = 1,
        IsDeleted = 0,
        ShowInSidebar = 1,
        UpdatedAtUtc = SYSUTCDATETIME()
    WHERE [Key] = 'admin.error_logs';
    PRINT 'Updated admin.error_logs menu item in dbo.MenuItems.';
END
GO

PRINT '00000004_add_error_logs_and_menu.sql completed successfully.';
GO
