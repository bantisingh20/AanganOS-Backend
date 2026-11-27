-- =====================================================================================================
-- Migration Script: 00000010_add_master_data_menus.sql
-- Purpose:
--   Add the two admin-web (Surface = 1) menu items for MasterDataMenuPage, under Administration:
--     admin.master-data  "Master Data"   /master-data   (opens the Master Data tab)
--     admin.master-menu  "Master Menu"   /master-menu   (opens the Menus & Tiles tab)
--   Each has its own permission resource (= its key) with actions 0,1,2,3 (View/Create/Edit/Delete).
--
--   Who sees them (mirrors the existing admin menus, nothing new is opened up):
--     Superadmin                                  -> both, all actions
--     any role holding admin.config  (Configuration)   -> admin.master-data, same actions it holds there
--     any role holding admin.menu    (Menu Management) -> admin.master-menu, same actions it holds there
--   Saving master data on the API still needs Edit:MasterData (platform-only, see 00000008), so a
--   non-Superadmin who sees the menu can browse but not save.
--
-- Safe & Idempotent (re-running updates the two rows in place and adds only missing grants).
-- Rollback:
--   DELETE rp FROM dbo.RolePermissions rp JOIN dbo.Permissions p ON p.Id = rp.PermissionId
--     WHERE p.ResourceType IN ('admin.master-data', 'admin.master-menu');
--   DELETE FROM dbo.Permissions WHERE ResourceType IN ('admin.master-data', 'admin.master-menu');
--   DELETE FROM dbo.MenuItems WHERE [Key] IN ('admin.master-data', 'admin.master-menu');
-- Actions: 0=View, 1=Create, 2=Edit, 3=Delete
-- =====================================================================================================

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

-- -----------------------------------------------------------------------------------------------------
-- 1. Menu items
-- -----------------------------------------------------------------------------------------------------
DECLARE @AdminGroupId UNIQUEIDENTIFIER = (
    SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = 'admin.group.administration' AND IsDeleted = 0
);

DECLARE @Menus TABLE ([Key] NVARCHAR(128), Label NVARCHAR(200), Icon NVARCHAR(100), Route NVARCHAR(200), SortOrder INT);
INSERT INTO @Menus ([Key], Label, Icon, Route, SortOrder) VALUES
    ('admin.master-data', N'Master Data', 'list',        '/master-data', 32),
    ('admin.master-menu', N'Master Menu', 'MenuOutlined', '/master-menu', 33);

UPDATE mi
SET ParentId = @AdminGroupId,
    Label = m.Label,
    Icon = m.Icon,
    Route = m.Route,
    Surface = 1,
    RequiredPermissionResource = m.[Key],
    RequiredPermissionActions = '0,1,2,3',
    SortOrder = m.SortOrder,
    IsActive = 1,
    IsDeleted = 0,
    ShowInSidebar = 1,
    UpdatedAtUtc = SYSUTCDATETIME()
FROM dbo.MenuItems mi
INNER JOIN @Menus m ON m.[Key] = mi.[Key];
PRINT CONCAT('00000010: updated ', @@ROWCOUNT, ' existing menu item(s).');

INSERT INTO dbo.MenuItems (
    Id, ParentId, [Key], Label, Icon, Route, Surface,
    RequiredPermissionResource, RequiredPermissionActions,
    SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy
)
SELECT NEWID(), @AdminGroupId, m.[Key], m.Label, m.Icon, m.Route, 1,
       m.[Key], '0,1,2,3',
       m.SortOrder, 1, 0, 1, SYSUTCDATETIME(), 'migration-00000010'
FROM @Menus m
WHERE NOT EXISTS (SELECT 1 FROM dbo.MenuItems mi WHERE mi.[Key] = m.[Key]);
PRINT CONCAT('00000010: inserted ', @@ROWCOUNT, ' menu item(s).');

IF @AdminGroupId IS NULL
    PRINT '00000010: WARNING admin.group.administration not found; the menus were added at the top level.';
GO

-- -----------------------------------------------------------------------------------------------------
-- 2. Permission rows (one per resource x action)
-- -----------------------------------------------------------------------------------------------------
INSERT INTO dbo.Permissions (Id, ResourceType, Action, IsDeleted, CreatedAtUtc, CreatedBy)
SELECT NEWID(), r.Resource, a.Action, 0, SYSUTCDATETIME(), 'migration-00000010'
FROM (VALUES ('admin.master-data'), ('admin.master-menu')) r(Resource)
CROSS JOIN (VALUES (0), (1), (2), (3)) a(Action)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.Permissions p
    WHERE p.ResourceType = r.Resource AND p.Action = a.Action AND p.IsDeleted = 0
);
PRINT CONCAT('00000010: inserted ', @@ROWCOUNT, ' permission row(s).');
GO

-- -----------------------------------------------------------------------------------------------------
-- 3. Grants
-- -----------------------------------------------------------------------------------------------------
-- 3.1 Superadmin: both menus, all actions.
INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT r.Id, p.Id
FROM dbo.Roles r
CROSS JOIN dbo.Permissions p
WHERE r.Name = 'Superadmin' AND r.CommunityId IS NULL AND r.IsDeleted = 0
  AND p.ResourceType IN ('admin.master-data', 'admin.master-menu') AND p.IsDeleted = 0
  AND NOT EXISTS (SELECT 1 FROM dbo.RolePermissions rp WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id);
PRINT CONCAT('00000010: Superadmin grants added: ', @@ROWCOUNT);

-- 3.2 Mirror: whoever holds Configuration gets Master Data, whoever holds Menu Management gets
--     Master Menu, with the same actions.
INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT DISTINCT rp.RoleId, np.Id
FROM dbo.RolePermissions rp
INNER JOIN dbo.Roles r ON r.Id = rp.RoleId AND r.IsDeleted = 0
INNER JOIN dbo.Permissions op ON op.Id = rp.PermissionId AND op.IsDeleted = 0
INNER JOIN (VALUES ('admin.config', 'admin.master-data'), ('admin.menu', 'admin.master-menu')) map(OldResource, NewResource)
    ON map.OldResource = op.ResourceType
INNER JOIN dbo.Permissions np ON np.ResourceType = map.NewResource AND np.Action = op.Action AND np.IsDeleted = 0
WHERE NOT EXISTS (SELECT 1 FROM dbo.RolePermissions x WHERE x.RoleId = rp.RoleId AND x.PermissionId = np.Id);
PRINT CONCAT('00000010: mirrored grants added: ', @@ROWCOUNT);
GO

PRINT '00000010_add_master_data_menus.sql executed successfully.';
GO
