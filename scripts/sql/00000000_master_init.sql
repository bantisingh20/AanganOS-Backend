-- =====================================================================================================
-- MASTER DATABASE INITIALIZATION SCRIPT (DATA ONLY - ZERO DDL)
-- File: 00000000_master_init.sql
-- Description:
--   Contains ONLY INSERT / MERGE queries for baseline initialization.
--   Tables, columns, indexes, foreign keys, and constraints are auto-created at runtime by DbContext (EnsureCreatedAsync).
--   Every menu item uses its OWN DISTINCT key as RequiredPermissionResource (ZERO shared keys between menus!).
--   All statements use 'IF NOT EXISTS' / MERGE so it is 100% idempotent and non-destructive!
-- =====================================================================================================

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

PRINT '>>> Initializing Master Seed Data...';
GO

-- =====================================================================================================
-- SECTION 1: SYSTEM ROLES
-- =====================================================================================================
PRINT '1. Seeding Baseline Roles...';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Name = 'Superadmin' AND CommunityId IS NULL)
BEGIN
    INSERT INTO dbo.Roles (Id, Name, Scope, CommunityId, Surface, IsDeleted, CreatedAtUtc, CreatedBy)
    VALUES ('E35EEAB9-946B-429B-9B41-954149872A56', 'Superadmin', 0, NULL, 1, 0, SYSUTCDATETIME(), 'master-seed');
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Name = 'OrganizationAdmin' AND CommunityId IS NULL)
BEGIN
    INSERT INTO dbo.Roles (Id, Name, Scope, CommunityId, Surface, IsDeleted, CreatedAtUtc, CreatedBy)
    VALUES ('3A07ADCA-3169-46DB-9869-6AF4A5679C9A', 'OrganizationAdmin', 0, NULL, 1, 0, SYSUTCDATETIME(), 'master-seed');
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Name = 'CommunityAdmin' AND CommunityId IS NULL)
BEGIN
    INSERT INTO dbo.Roles (Id, Name, Scope, CommunityId, Surface, IsDeleted, CreatedAtUtc, CreatedBy)
    VALUES ('B5FEF3D7-6F71-46EA-8C45-A656F9898233', 'CommunityAdmin', 0, NULL, 1, 0, SYSUTCDATETIME(), 'master-seed');
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Name = 'Resident' AND CommunityId IS NULL)
BEGIN
    INSERT INTO dbo.Roles (Id, Name, Scope, CommunityId, Surface, IsDeleted, CreatedAtUtc, CreatedBy)
    VALUES ('A9BD2C84-6338-4F15-AE11-F648FEBEA6C6', 'Resident', 0, NULL, 0, 0, SYSUTCDATETIME(), 'master-seed');
END;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Roles WHERE Name = 'Security' AND CommunityId IS NULL)
BEGIN
    INSERT INTO dbo.Roles (Id, Name, Scope, CommunityId, Surface, IsDeleted, CreatedAtUtc, CreatedBy)
    VALUES ('8737D8FD-D5BD-4148-BF61-94943E198539', 'Security', 0, NULL, 0, 0, SYSUTCDATETIME(), 'master-seed');
END;
GO
-- =====================================================================================================
-- SECTION 2: SYSTEM MENU ITEMS (DISTINCT KEYS FOR EVERY MENU - ZERO SHARING)
-- =====================================================================================================
PRINT '2. Seeding System Menu Items...';
GO
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.billing.bills')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('e3151bbf-417a-64a6-a2fe-5b1e59c996af', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'b524fbf5-cf47-f4c2-4e11-a384212a1050')), 'app.billing.bills', N'Bills', NULL, NULL, 0, 'app.billing.bills', '0,1,2', 0, 1, 0, 0, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.home')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('96123ba4-af24-9b90-507e-71874088e6c2', NULL, 'app.home', N'Home', 'home', 'home', 0, NULL, NULL, 0, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.profile')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('064827a5-66e7-88a2-34eb-7f002a5c8fb1', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'f685360e-c873-be5c-a0c7-7f844cfa8d9b')), 'app.profile', N'My Profile', 'person', 'my-profile', 0, 'app.profile', '0,7', 0, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.visitors.invite')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('51defa81-6752-0e32-9834-849669d894b6', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '91cbbd5a-ac89-837d-a5b6-9e7ca5861cc3')), 'app.visitors.invite', N'Invite Visitor', 'person-add', 'invite-visitor', 0, 'app.visitors.invite', '1,7', 0, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.notifications')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('971743f6-2d80-3b55-e7e6-9cfabb28e62e', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'e27f4f5f-e012-f0d0-7a28-9ed111e5e156')), 'app.notifications', N'Notifications', 'bell', 'notifications', 0, 'app.notifications', '0,7', 0, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.verifypass')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('6dfac3e3-ae0d-d865-36b6-f4249ac90e58', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '0451efc9-a12d-080c-0be6-079ff218b378')), 'app.verifypass', N'Verify Pass', 'scan', 'verify-pass', 0, 'app.verifypass', '1,7', 0, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.household')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('d8ae6d6a-0427-d9e1-f352-eee25ed2a52e', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'f685360e-c873-be5c-a0c7-7f844cfa8d9b')), 'app.household', N'My Household', 'home', 'my-household', 0, 'app.household', '0,7', 10, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.services')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('86fddecd-a028-61a6-7504-e6e3b297c3fa', NULL, 'app.services', N'Services', 'grid', 'services', 0, NULL, NULL, 10, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.billing.charges')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('2fd9e06a-f551-257b-cb3b-8ff5ac2c7161', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'b524fbf5-cf47-f4c2-4e11-a384212a1050')), 'app.billing.charges', N'Charges', NULL, NULL, 0, 'app.billing.charges', '0,1,2', 10, 1, 0, 0, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.visitors.my')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('7561b9f8-23b9-0c3b-3c2a-16a9f6172bb9', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '91cbbd5a-ac89-837d-a5b6-9e7ca5861cc3')), 'app.visitors.my', N'My Visitors', 'list', 'visitors', 0, 'app.visitors.my', '0,7', 10, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.walkin')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('8a272f50-4745-bfa0-c43f-34054027e69b', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '0451efc9-a12d-080c-0be6-079ff218b378')), 'app.walkin', N'Walk-in Entry', 'person-add', 'register-walkin', 0, 'app.walkin', '1,7', 10, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.entryexit')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('a4b19929-8478-5cbf-8f8d-85914e5489e2', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '0451efc9-a12d-080c-0be6-079ff218b378')), 'app.entryexit', N'Entry / Exit', 'swap', 'entry-exit', 0, 'app.entryexit', '0,7', 20, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.group.myhome')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('f685360e-c873-be5c-a0c7-7f844cfa8d9b', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '86fddecd-a028-61a6-7504-e6e3b297c3fa')), 'app.group.myhome', N'My Home', NULL, NULL, 0, NULL, NULL, 20, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.billing.ledger')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('e4ef8f4d-746b-17c4-7d2d-ce093d12d9a8', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'b524fbf5-cf47-f4c2-4e11-a384212a1050')), 'app.billing.ledger', N'Ledger', NULL, NULL, 0, 'app.billing.ledger', '0,1,2', 20, 1, 0, 0, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.visitors.approvals')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('66c63772-f8b0-0950-23f2-f20af67f2d82', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '91cbbd5a-ac89-837d-a5b6-9e7ca5861cc3')), 'app.visitors.approvals', N'Approvals', 'checkmark-done', 'approvals', 0, 'app.visitors.approvals', '0,7', 20, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.recurring')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('7fb1eb72-d8de-09ca-0402-eb250a2c53a9', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '91cbbd5a-ac89-837d-a5b6-9e7ca5861cc3')), 'app.recurring', N'Recurring Visitors', 'repeat', 'recurring-visitors', 0, 'app.recurring', '0,7', 30, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.billing.dashboard')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('541c8bfb-7560-9061-90e2-e79f580a4a8c', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'b524fbf5-cf47-f4c2-4e11-a384212a1050')), 'app.billing.dashboard', N'Dashboard', NULL, NULL, 0, 'app.billing.dashboard', '0', 30, 1, 0, 0, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.group.community')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('91cbbd5a-ac89-837d-a5b6-9e7ca5861cc3', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '86fddecd-a028-61a6-7504-e6e3b297c3fa')), 'app.group.community', N'Community', NULL, NULL, 0, NULL, NULL, 30, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.vehiclelookup')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('b1b7c816-6172-e5ea-5b38-062d6057ba9f', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '0451efc9-a12d-080c-0be6-079ff218b378')), 'app.vehiclelookup', N'Vehicle Lookup', 'car', 'vehicle-lookup', 0, 'app.vehiclelookup', '0,7', 30, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.group.security')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('0451efc9-a12d-080c-0be6-079ff218b378', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '86fddecd-a028-61a6-7504-e6e3b297c3fa')), 'app.group.security', N'Security', NULL, NULL, 0, NULL, NULL, 40, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.parcels')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('e68418dd-a1ea-760e-3d5f-1d5b6338d17e', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '91cbbd5a-ac89-837d-a5b6-9e7ca5861cc3')), 'app.parcels', N'Parcels', 'cube', 'parcels', 0, 'app.parcels', '0,7', 40, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.watchlist')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('807ffafd-4891-cd01-d99c-43af018efc06', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '0451efc9-a12d-080c-0be6-079ff218b378')), 'app.watchlist', N'Watchlist', 'shield', 'watchlist', 0, 'app.watchlist', '0,7', 40, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.sos')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('3c130139-2293-cc53-44be-1b6c88961838', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '0451efc9-a12d-080c-0be6-079ff218b378')), 'app.sos', N'SOS Alerts', 'alert', 'sos', 0, 'app.sos', '0,7', 50, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.group.support')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('e27f4f5f-e012-f0d0-7a28-9ed111e5e156', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '86fddecd-a028-61a6-7504-e6e3b297c3fa')), 'app.group.support', N'Support', NULL, NULL, 0, NULL, NULL, 50, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.maintenance')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('b87186ee-64e6-eedb-95d1-b07b6dc5bfbd', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'f685360e-c873-be5c-a0c7-7f844cfa8d9b')), 'app.maintenance', N'My Maintenance', 'card', 'maintenance', 0, 'app.maintenance', '0,7', 50, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.billing')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('b524fbf5-cf47-f4c2-4e11-a384212a1050', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '91cbbd5a-ac89-837d-a5b6-9e7ca5861cc3')), 'app.billing', N'Billing', 'wallet', 'billing', 0, 'app.billing', '0,7', 55, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.helpdesk')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('a5c00a6a-b65b-5ee5-b274-607ad1868e97', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'e27f4f5f-e012-f0d0-7a28-9ed111e5e156')), 'app.helpdesk', N'Help Desk', 'construct', 'raise-ticket', 0, 'app.helpdesk', '0,7', 60, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'app.announcements')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('27601e32-453b-5e41-eb08-839ce745fd3c', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '91cbbd5a-ac89-837d-a5b6-9e7ca5861cc3')), 'app.announcements', N'Announcements', 'megaphone', 'announcements', 0, 'app.announcements', '0,7', 70, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.organizations')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('061ac0fc-3907-cb1e-42f1-62530a8c2e25', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '2f49cb3c-9559-81db-71db-f28a1a56146b')), 'admin.organizations', N'Organizations', 'BankOutlined', '/organizations', 1, 'admin.organizations', '0,1,2,3', 0, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.announcements')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('8189a96f-1b2f-ed4c-e47d-6a15b4d0fc44', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'e7c84a13-24ee-cddd-8e8f-9e9a25159edf')), 'admin.announcements', N'Announcements', 'NotificationOutlined', '/announcements', 1, 'admin.announcements', '0,1,2,3', 0, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.billing.bills')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('e8043e49-6faf-ef7e-3115-75cf044499b7', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'fcdc90fd-de27-9623-7da9-54fe9d036072')), 'admin.billing.bills', N'Bills', NULL, NULL, 1, 'admin.billing.bills', '0,1,2,3', 0, 1, 0, 0, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.users')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('901e18f1-35ee-36e0-64b0-223a900e596b', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '39d58abe-6e52-a2d0-d11c-4db2cff6c848')), 'admin.users', N'Manage People', 'UserOutlined', '/users', 1, 'admin.users', '0,1,2,3', 0, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.dashboard')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('f7a9e751-1b94-bd61-f06f-ae826d8bdaf3', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '264e43a5-b414-f566-f106-e9d332f675c2')), 'admin.dashboard', N'Dashboard', 'AppstoreOutlined', '/', 1, NULL, NULL, 0, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.group.overview')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('264e43a5-b414-f566-f106-e9d332f675c2', NULL, 'admin.group.overview', N'Overview', NULL, NULL, 1, NULL, NULL, 0, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.adminusers')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('3d29793c-a8eb-9992-03bb-c8bce9d81cee', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '39d58abe-6e52-a2d0-d11c-4db2cff6c848')), 'admin.adminusers', N'Admin Users', 'SafetyOutlined', '/admin-users', 1, 'admin.adminusers', '0,1,2,3', 10, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.group.community')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('2f49cb3c-9559-81db-71db-f28a1a56146b', NULL, 'admin.group.community', N'Community', NULL, NULL, 1, NULL, NULL, 10, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.maintenance')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('1cab2230-3d29-bd79-d0ac-3d111c1ee83e', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'e7c84a13-24ee-cddd-8e8f-9e9a25159edf')), 'admin.maintenance', N'Help Desk', 'ToolOutlined', '/maintenance', 1, 'admin.maintenance', '0,1,2,3', 10, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.communities')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('228dd260-aa84-18fe-41ac-1ac5540ecec7', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '2f49cb3c-9559-81db-71db-f28a1a56146b')), 'admin.communities', N'Communities', 'ApartmentOutlined', '/communities', 1, 'admin.communities', '0,1,2,3', 10, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.billing.charges')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('76a11372-a946-432f-3fe6-80a36f33a424', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'fcdc90fd-de27-9623-7da9-54fe9d036072')), 'admin.billing.charges', N'Charges', NULL, NULL, 1, 'admin.billing.charges', '0,1,2,3', 10, 1, 0, 0, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.roles')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('b33df62d-ce3b-65f5-ae22-a1fd302e5102', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '39d58abe-6e52-a2d0-d11c-4db2cff6c848')), 'admin.roles', N'Roles & Permissions', 'KeyRound', '/roles', 1, 'admin.roles', '0,1,2,3', 15, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.group.operations')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('e7c84a13-24ee-cddd-8e8f-9e9a25159edf', NULL, 'admin.group.operations', N'Operations', NULL, NULL, 1, NULL, NULL, 20, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.billing.expenses')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('96191d60-6b34-f6d5-d591-6908dc15ef0a', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'fcdc90fd-de27-9623-7da9-54fe9d036072')), 'admin.billing.expenses', N'Expenses', NULL, NULL, 1, 'admin.billing.expenses', '0,1,2,3', 20, 1, 0, 0, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.menu')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('fbc5dac3-283b-b119-58a0-41ae5f55a3fa', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '39d58abe-6e52-a2d0-d11c-4db2cff6c848')), 'admin.menu', N'Menu Management', 'MenuOutlined', '/menu-management', 1, 'admin.menu', '0,1,2,3', 20, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.units')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('85ab7e43-35b2-faf6-71d5-5adbcaccb3bc', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '2f49cb3c-9559-81db-71db-f28a1a56146b')), 'admin.units', N'Towers & Units', 'HomeOutlined', '/units', 1, 'admin.units', '0,1,2,3', 20, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.visitors')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('93d8b10c-1fce-1890-fb2e-f76d667d1002', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'e7c84a13-24ee-cddd-8e8f-9e9a25159edf')), 'admin.visitors', N'Visitors', 'IdcardOutlined', '/visitors', 1, 'admin.visitors', '0,1,2,3', 20, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.config')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('bfb6170b-e9be-0333-4d20-f7c521f6e2ef', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '39d58abe-6e52-a2d0-d11c-4db2cff6c848')), 'admin.config', N'Configuration', 'SettingOutlined', '/config', 1, 'admin.config', '0,1,2,3', 30, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.billing.dashboard')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('99f31517-4fcc-830f-d54b-cfd8be54089e', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'fcdc90fd-de27-9623-7da9-54fe9d036072')), 'admin.billing.dashboard', N'Dashboard', NULL, NULL, 1, 'admin.billing.dashboard', '0,1,2,3', 30, 1, 0, 0, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.amenities')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('7728c85c-48b6-1c00-6686-5e85a07614c3', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'e7c84a13-24ee-cddd-8e8f-9e9a25159edf')), 'admin.amenities', N'Amenities', 'CoffeeOutlined', '/amenities', 1, 'admin.amenities', '0', 30, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.group.administration')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('39d58abe-6e52-a2d0-d11c-4db2cff6c848', NULL, 'admin.group.administration', N'Administration', NULL, NULL, 1, NULL, NULL, 30, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.residents')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('4dc8b928-d688-b317-0c7c-265657708b88', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '2f49cb3c-9559-81db-71db-f28a1a56146b')), 'admin.residents', N'Residents', 'TeamOutlined', '/residents', 1, 'admin.residents', '0,1,2,3', 30, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.notification-templates')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('3a93202c-98a4-79ab-9a39-6b1b90d54c3a', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = '39d58abe-6e52-a2d0-d11c-4db2cff6c848')), 'admin.notification-templates', N'Notification Templates', 'MailOutlined', '/notification-templates', 1, 'admin.notification-templates', '0,1,2,3', 35, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.notifications')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('f5807732-30c7-175e-c3a7-51e3a350b9a9', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'e7c84a13-24ee-cddd-8e8f-9e9a25159edf')), 'admin.notifications', N'Notifications', 'MessageOutlined', '/notifications', 1, 'admin.notifications', '0', 40, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
IF NOT EXISTS (SELECT 1 FROM dbo.MenuItems WHERE [Key] = 'admin.billing')
BEGIN
    INSERT INTO dbo.MenuItems (Id, ParentId, [Key], Label, Icon, Route, Surface, RequiredPermissionResource, RequiredPermissionActions, SortOrder, IsActive, IsDeleted, ShowInSidebar, CreatedAtUtc, CreatedBy)
    VALUES ('fcdc90fd-de27-9623-7da9-54fe9d036072', (SELECT TOP 1 Id FROM dbo.MenuItems WHERE [Key] = (SELECT [Key] FROM dbo.MenuItems WHERE Id = 'e7c84a13-24ee-cddd-8e8f-9e9a25159edf')), 'admin.billing', N'Maintenance Billing', 'Wallet', '/maintenance-billing', 1, 'admin.billing', '0,1,2,3', 40, 1, 0, 1, SYSUTCDATETIME(), 'master-seed');
END;
GO

-- =====================================================================================================
-- SECTION 2.5: SYSTEM PERMISSIONS CATALOG (DISTINCT PER MENU)
-- =====================================================================================================
-- =====================================================================================================
-- SAFE RESOURCE SYNC: ENSURE ALL EXISTING MENUS HAVE UNIQUE DISTINCT KEYS
-- (ZERO IMPACT ON LABELS, PARENTID, OR ORDER - ONLY FIXES AUTO-CHECK COLLISION)
-- =====================================================================================================
PRINT 'Ensuring existing menus have unique distinct permission resources...';
GO

UPDATE dbo.MenuItems
SET RequiredPermissionResource = [Key]
WHERE RequiredPermissionResource IS NOT NULL
  AND RequiredPermissionResource != [Key];
GO

PRINT '2.5 Seeding Permissions Catalog...';
GO

INSERT INTO dbo.Permissions (Id, ResourceType, Action, IsDeleted, CreatedAtUtc, CreatedBy)
SELECT NEWID(), m.RequiredPermissionResource, a.val, 0, SYSUTCDATETIME(), 'master-seed'
FROM dbo.MenuItems m
CROSS APPLY (
    SELECT CAST(value AS INT) AS val 
    FROM STRING_SPLIT(m.RequiredPermissionActions, ',')
) a
WHERE m.RequiredPermissionResource IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.Permissions p 
      WHERE p.ResourceType = m.RequiredPermissionResource AND p.Action = a.val
  );
GO

-- =====================================================================================================
-- SECTION 3: BASELINE ROLE PERMISSIONS
-- =====================================================================================================
PRINT '3. Mapping Baseline Role Permissions...';
GO

-- 3.1 Superadmin (PowerUser): Granted ALL active catalog permissions
INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT 
    (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Superadmin' AND CommunityId IS NULL),
    p.Id
FROM dbo.Permissions p
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.RolePermissions rp 
    WHERE rp.RoleId = (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Superadmin' AND CommunityId IS NULL)
      AND rp.PermissionId = p.Id
);
GO

-- 3.2 Resident Role: Granted baseline resident app features
INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT 
    (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Resident' AND CommunityId IS NULL),
    p.Id
FROM dbo.Permissions p
WHERE p.ResourceType LIKE 'app.%'
  AND p.ResourceType NOT IN ('app.verifypass', 'app.walkin', 'app.entryexit', 'app.vehiclelookup', 'app.watchlist', 'app.sos')
  AND NOT EXISTS (
    SELECT 1 FROM dbo.RolePermissions rp 
    WHERE rp.RoleId = (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Resident' AND CommunityId IS NULL)
      AND rp.PermissionId = p.Id
  );
GO

-- 3.3 Security Role: Granted baseline gate/visitor features
INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT 
    (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Security' AND CommunityId IS NULL),
    p.Id
FROM dbo.Permissions p
WHERE p.ResourceType IN ('app.verifypass', 'app.walkin', 'app.entryexit', 'app.vehiclelookup', 'app.watchlist', 'app.sos', 'app.notifications')
  AND NOT EXISTS (
    SELECT 1 FROM dbo.RolePermissions rp 
    WHERE rp.RoleId = (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Security' AND CommunityId IS NULL)
      AND rp.PermissionId = p.Id
  );
GO

-- =====================================================================================================
-- SECTION 4: DEFAULT SUPERADMIN USER
-- =====================================================================================================
PRINT '4. Seeding Initial SuperAdmin User...';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Phone = '+910000000000')
BEGIN
    INSERT INTO dbo.Users (
        Id,
        Phone,
        Email,
        Name,
        PasswordHash,
        RoleId,
        Status,
        IsDeleted,
        CreatedAtUtc,
        CreatedBy
    )
    VALUES (
        'B89F7041-92F6-4444-AC8A-CD5F1E29ACFA',
        '+910000000000',
        'superadmin@aanganos.local',
        'Super Admin',
        '10000.h+lXvWz8dZq1U2F3V4G5H6==.0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef',
        (SELECT TOP 1 Id FROM dbo.Roles WHERE Name = 'Superadmin' AND CommunityId IS NULL),
        1,
        0,
        SYSUTCDATETIME(),
        'master-seed'
    );
END;
GO

-- =====================================================================================================
-- SECTION 5: MASTER NOTIFICATION TEMPLATES
-- =====================================================================================================
PRINT '5. Seeding Master Notification Templates...';
GO
GO
-- =====================================================================================================
-- SECTION 6: SYSTEM & MOBILE CONFIGURATIONS
-- =====================================================================================================
PRINT '6. Seeding System & Mobile Configurations...';
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Configurations WHERE [Key] = 'app.home.tiles.resident' AND CommunityId IS NULL)
    INSERT INTO dbo.Configurations (Id, [Key], [Value], [Scope], CommunityId, IsDeleted, CreatedAtUtc, CreatedBy)
    VALUES (NEWID(), 'app.home.tiles.resident', 'my-profile,my-household,my-visitors,maintenance', 0, NULL, 0, SYSUTCDATETIME(), 'master-seed');

IF NOT EXISTS (SELECT 1 FROM dbo.Configurations WHERE [Key] = 'gate.visitPass.defaultValidity' AND CommunityId IS NULL)
    INSERT INTO dbo.Configurations (Id, [Key], [Value], [Scope], CommunityId, IsDeleted, CreatedAtUtc, CreatedBy)
    VALUES (NEWID(), 'gate.visitPass.defaultValidity', '2', 0, NULL, 0, SYSUTCDATETIME(), 'master-seed');

IF NOT EXISTS (SELECT 1 FROM dbo.Configurations WHERE [Key] = 'gate.walkIn.approvalRequired' AND CommunityId IS NULL)
    INSERT INTO dbo.Configurations (Id, [Key], [Value], [Scope], CommunityId, IsDeleted, CreatedAtUtc, CreatedBy)
    VALUES (NEWID(), 'gate.walkIn.approvalRequired', 'true', 0, NULL, 0, SYSUTCDATETIME(), 'master-seed');

IF NOT EXISTS (SELECT 1 FROM dbo.Configurations WHERE [Key] = 'gate.visitPass.defaultValidityUnit' AND CommunityId IS NULL)
    INSERT INTO dbo.Configurations (Id, [Key], [Value], [Scope], CommunityId, IsDeleted, CreatedAtUtc, CreatedBy)
    VALUES (NEWID(), 'gate.visitPass.defaultValidityUnit', 'days', 0, NULL, 0, SYSUTCDATETIME(), 'master-seed');

IF NOT EXISTS (SELECT 1 FROM dbo.Configurations WHERE [Key] = 'gate.visitPass.defaultMaxUses' AND CommunityId IS NULL)
    INSERT INTO dbo.Configurations (Id, [Key], [Value], [Scope], CommunityId, IsDeleted, CreatedAtUtc, CreatedBy)
    VALUES (NEWID(), 'gate.visitPass.defaultMaxUses', '1', 0, NULL, 0, SYSUTCDATETIME(), 'master-seed');

IF NOT EXISTS (SELECT 1 FROM dbo.Configurations WHERE [Key] = 'gate.visitPass.defaultValidityHours' AND CommunityId IS NULL)
    INSERT INTO dbo.Configurations (Id, [Key], [Value], [Scope], CommunityId, IsDeleted, CreatedAtUtc, CreatedBy)
    VALUES (NEWID(), 'gate.visitPass.defaultValidityHours', '24', 0, NULL, 0, SYSUTCDATETIME(), 'master-seed');

IF NOT EXISTS (SELECT 1 FROM dbo.Configurations WHERE [Key] = 'gate.recurringVisitor.expiringSoonDays' AND CommunityId IS NULL)
    INSERT INTO dbo.Configurations (Id, [Key], [Value], [Scope], CommunityId, IsDeleted, CreatedAtUtc, CreatedBy)
    VALUES (NEWID(), 'gate.recurringVisitor.expiringSoonDays', '10', 0, NULL, 0, SYSUTCDATETIME(), 'master-seed');

IF NOT EXISTS (SELECT 1 FROM dbo.Configurations WHERE [Key] = 'gate.walkIn.pendingExpiryMinutes' AND CommunityId IS NULL)
    INSERT INTO dbo.Configurations (Id, [Key], [Value], [Scope], CommunityId, IsDeleted, CreatedAtUtc, CreatedBy)
    VALUES (NEWID(), 'gate.walkIn.pendingExpiryMinutes', '15', 0, NULL, 0, SYSUTCDATETIME(), 'master-seed');

IF NOT EXISTS (SELECT 1 FROM dbo.Configurations WHERE [Key] = 'app.home.tiles.security' AND CommunityId IS NULL)
    INSERT INTO dbo.Configurations (Id, [Key], [Value], [Scope], CommunityId, IsDeleted, CreatedAtUtc, CreatedBy)
    VALUES (NEWID(), 'app.home.tiles.security', 'verify-pass,register-walkin,entry-exit,notifications', 0, NULL, 0, SYSUTCDATETIME(), 'master-seed');
GO
-- =====================================================================================================
-- SECTION 7: MASTER DATA ITEMS (DROPDOWNS & LISTS)
-- =====================================================================================================
PRINT '7. Seeding Master Data Items (Dropdowns & Lists)...';
GO
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Access_Integration_Action' AND Code = 'open_barrier' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Access_Integration_Action', 0, NULL, 'open_barrier', N'Open Boom Barrier', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Access_Integration_Action' AND Code = 'close_barrier' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Access_Integration_Action', 0, NULL, 'close_barrier', N'Close Boom Barrier', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Access_Integration_Action' AND Code = 'cctv_snapshot' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Access_Integration_Action', 0, NULL, 'cctv_snapshot', N'CCTV Snapshot', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Access_Integration_Action' AND Code = 'access_event' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Access_Integration_Action', 0, NULL, 'access_event', N'Access-Control Event', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Access_Integration_Status' AND Code = 'triggered' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Access_Integration_Status', 0, NULL, 'triggered', N'Triggered', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Access_Integration_Status' AND Code = 'succeeded' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Access_Integration_Status', 0, NULL, 'succeeded', N'Succeeded', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Access_Integration_Status' AND Code = 'failed' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Access_Integration_Status', 0, NULL, 'failed', N'Failed', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Amc_Contract_Status' AND Code = 'active' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Amc_Contract_Status', 0, NULL, 'active', N'Active', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Amc_Contract_Status' AND Code = 'expired' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Amc_Contract_Status', 0, NULL, 'expired', N'Expired', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Amc_Contract_Status' AND Code = 'cancelled' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Amc_Contract_Status', 0, NULL, 'cancelled', N'Cancelled', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Amenity_Status' AND Code = 'active' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Amenity_Status', 0, NULL, 'active', N'Active', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Amenity_Status' AND Code = 'inactive' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Amenity_Status', 0, NULL, 'inactive', N'Inactive', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Amenity_Status' AND Code = 'under_maintenance' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Amenity_Status', 0, NULL, 'under_maintenance', N'Under Maintenance', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Amenity_Type' AND Code = 'clubhouse' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Amenity_Type', 0, NULL, 'clubhouse', N'Clubhouse', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Amenity_Type' AND Code = 'gym' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Amenity_Type', 0, NULL, 'gym', N'Gym', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Amenity_Type' AND Code = 'pool' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Amenity_Type', 0, NULL, 'pool', N'Swimming Pool', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Amenity_Type' AND Code = 'hall' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Amenity_Type', 0, NULL, 'hall', N'Hall', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Amenity_Type' AND Code = 'court' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Amenity_Type', 0, NULL, 'court', N'Sports Court', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Amenity_Type' AND Code = 'garden' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Amenity_Type', 0, NULL, 'garden', N'Garden', 5, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Amenity_Type' AND Code = 'party_area' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Amenity_Type', 0, NULL, 'party_area', N'Party Area', 6, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Amenity_Type' AND Code = 'custom' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Amenity_Type', 0, NULL, 'custom', N'Custom', 7, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Announcement_Category' AND Code = 'general' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Announcement_Category', 0, NULL, 'general', N'General', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Announcement_Category' AND Code = 'event' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Announcement_Category', 0, NULL, 'event', N'Event', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Announcement_Category' AND Code = 'maintenance' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Announcement_Category', 0, NULL, 'maintenance', N'Maintenance', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Announcement_Category' AND Code = 'emergency' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Announcement_Category', 0, NULL, 'emergency', N'Emergency', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Asset_Category' AND Code = 'common_area' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Asset_Category', 0, NULL, 'common_area', N'Common Area', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Asset_Category' AND Code = 'utility' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Asset_Category', 0, NULL, 'utility', N'Utility', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Asset_Category' AND Code = 'safety' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Asset_Category', 0, NULL, 'safety', N'Safety', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Asset_Category' AND Code = 'amenity' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Asset_Category', 0, NULL, 'amenity', N'Amenity', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Asset_Category' AND Code = 'other' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Asset_Category', 0, NULL, 'other', N'Other', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Asset_Status' AND Code = 'active' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Asset_Status', 0, NULL, 'active', N'Active', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Asset_Status' AND Code = 'under_maintenance' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Asset_Status', 0, NULL, 'under_maintenance', N'Under Maintenance', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Asset_Status' AND Code = 'inactive' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Asset_Status', 0, NULL, 'inactive', N'Inactive', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Asset_Status' AND Code = 'retired' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Asset_Status', 0, NULL, 'retired', N'Retired', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Asset_Status' AND Code = 'disposed' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Asset_Status', 0, NULL, 'disposed', N'Disposed', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Asset_Type' AND Code = 'lift' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Asset_Type', 0, NULL, 'lift', N'Lift / Elevator', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Asset_Type' AND Code = 'pump' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Asset_Type', 0, NULL, 'pump', N'Pump', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Asset_Type' AND Code = 'dg_set' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Asset_Type', 0, NULL, 'dg_set', N'Diesel Generator', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Asset_Type' AND Code = 'hvac' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Asset_Type', 0, NULL, 'hvac', N'HVAC', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Asset_Type' AND Code = 'fire_safety' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Asset_Type', 0, NULL, 'fire_safety', N'Fire Safety', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Asset_Type' AND Code = 'electrical' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Asset_Type', 0, NULL, 'electrical', N'Electrical', 5, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Asset_Type' AND Code = 'plumbing' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Asset_Type', 0, NULL, 'plumbing', N'Plumbing', 6, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Asset_Type' AND Code = 'other' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Asset_Type', 0, NULL, 'other', N'Other', 7, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Booking_Status' AND Code = 'requested' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Booking_Status', 0, NULL, 'requested', N'Requested', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Booking_Status' AND Code = 'confirmed' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Booking_Status', 0, NULL, 'confirmed', N'Confirmed', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Booking_Status' AND Code = 'cancelled' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Booking_Status', 0, NULL, 'cancelled', N'Cancelled', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Booking_Status' AND Code = 'completed' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Booking_Status', 0, NULL, 'completed', N'Completed', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Booking_Status_Transition' AND Code = 'requested->confirmed' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Booking_Status_Transition', 0, NULL, 'requested->confirmed', N'Requested â†’ Confirmed (approve)', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Booking_Status_Transition' AND Code = 'requested->cancelled' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Booking_Status_Transition', 0, NULL, 'requested->cancelled', N'Requested â†’ Cancelled', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Booking_Status_Transition' AND Code = 'confirmed->cancelled' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Booking_Status_Transition', 0, NULL, 'confirmed->cancelled', N'Confirmed â†’ Cancelled', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Booking_Status_Transition' AND Code = 'confirmed->completed' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Booking_Status_Transition', 0, NULL, 'confirmed->completed', N'Confirmed â†’ Completed', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Community_Type' AND Code = 'apartment_society' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Community_Type', 0, NULL, 'apartment_society', N'Apartment Society', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Community_Type' AND Code = 'gated_community' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Community_Type', 0, NULL, 'gated_community', N'Gated Community', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Community_Type' AND Code = 'villa_bungalow' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Community_Type', 0, NULL, 'villa_bungalow', N'Villa / Bungalow Community', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Community_Type' AND Code = 'townhouse' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Community_Type', 0, NULL, 'townhouse', N'Townhouse Development', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Community_Type' AND Code = 'mixed' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Community_Type', 0, NULL, 'mixed', N'Mixed Development', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ev_Reservation_Status' AND Code = 'reserved' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ev_Reservation_Status', 0, NULL, 'reserved', N'Reserved', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ev_Reservation_Status' AND Code = 'charging' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ev_Reservation_Status', 0, NULL, 'charging', N'Charging', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ev_Reservation_Status' AND Code = 'completed' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ev_Reservation_Status', 0, NULL, 'completed', N'Completed', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ev_Reservation_Status' AND Code = 'cancelled' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ev_Reservation_Status', 0, NULL, 'cancelled', N'Cancelled', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Guard_Shift_Status' AND Code = 'scheduled' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Guard_Shift_Status', 0, NULL, 'scheduled', N'Scheduled', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Guard_Shift_Status' AND Code = 'active' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Guard_Shift_Status', 0, NULL, 'active', N'Active', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Guard_Shift_Status' AND Code = 'completed' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Guard_Shift_Status', 0, NULL, 'completed', N'Completed', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Guard_Shift_Status' AND Code = 'cancelled' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Guard_Shift_Status', 0, NULL, 'cancelled', N'Cancelled', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Hierarchy_Level' AND Code = 'phase' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Hierarchy_Level', 0, NULL, 'phase', N'Phase', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Hierarchy_Level' AND Code = 'building_tower' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Hierarchy_Level', 0, NULL, 'building_tower', N'Building / Tower', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Hierarchy_Level' AND Code = 'block' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Hierarchy_Level', 0, NULL, 'block', N'Block', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Hierarchy_Level' AND Code = 'zone' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Hierarchy_Level', 0, NULL, 'zone', N'Zone', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Hierarchy_Level' AND Code = 'floor' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Hierarchy_Level', 0, NULL, 'floor', N'Floor', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'ID_Proof_Type' AND Code = 'aadhaar' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'ID_Proof_Type', 0, NULL, 'aadhaar', N'Aadhaar Card', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'ID_Proof_Type' AND Code = 'pan' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'ID_Proof_Type', 0, NULL, 'pan', N'PAN Card', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'ID_Proof_Type' AND Code = 'driving_license' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'ID_Proof_Type', 0, NULL, 'driving_license', N'Driving License', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'ID_Proof_Type' AND Code = 'voter_id' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'ID_Proof_Type', 0, NULL, 'voter_id', N'Voter ID', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'ID_Proof_Type' AND Code = 'passport' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'ID_Proof_Type', 0, NULL, 'passport', N'Passport', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'ID_Proof_Type' AND Code = 'other' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'ID_Proof_Type', 0, NULL, 'other', N'Other', 5, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Incident_Category' AND Code = 'trespass' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Incident_Category', 0, NULL, 'trespass', N'Trespass', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Incident_Category' AND Code = 'theft' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Incident_Category', 0, NULL, 'theft', N'Theft', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Incident_Category' AND Code = 'vandalism' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Incident_Category', 0, NULL, 'vandalism', N'Vandalism', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Incident_Category' AND Code = 'medical' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Incident_Category', 0, NULL, 'medical', N'Medical', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Incident_Category' AND Code = 'fire' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Incident_Category', 0, NULL, 'fire', N'Fire', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Incident_Category' AND Code = 'dispute' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Incident_Category', 0, NULL, 'dispute', N'Dispute', 5, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Incident_Category' AND Code = 'other' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Incident_Category', 0, NULL, 'other', N'Other', 6, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Incident_Escalation_State' AND Code = 'none' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Incident_Escalation_State', 0, NULL, 'none', N'Not Escalated', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Incident_Escalation_State' AND Code = 'escalated' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Incident_Escalation_State', 0, NULL, 'escalated', N'Escalated', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Incident_Severity' AND Code = 'low' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Incident_Severity', 0, NULL, 'low', N'Low', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Incident_Severity' AND Code = 'medium' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Incident_Severity', 0, NULL, 'medium', N'Medium', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Incident_Severity' AND Code = 'high' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Incident_Severity', 0, NULL, 'high', N'High', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Incident_Severity' AND Code = 'critical' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Incident_Severity', 0, NULL, 'critical', N'Critical', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Incident_Status' AND Code = 'open' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Incident_Status', 0, NULL, 'open', N'Open', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Incident_Status' AND Code = 'acknowledged' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Incident_Status', 0, NULL, 'acknowledged', N'Acknowledged', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Incident_Status' AND Code = 'escalated' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Incident_Status', 0, NULL, 'escalated', N'Escalated', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Incident_Status' AND Code = 'resolved' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Incident_Status', 0, NULL, 'resolved', N'Resolved', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Inspection_Result' AND Code = 'pass' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Inspection_Result', 0, NULL, 'pass', N'Pass', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Inspection_Result' AND Code = 'fail' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Inspection_Result', 0, NULL, 'fail', N'Fail', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Inspection_Result' AND Code = 'pass_with_observations' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Inspection_Result', 0, NULL, 'pass_with_observations', N'Pass with Observations', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Inspection_Result' AND Code = 'not_applicable' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Inspection_Result', 0, NULL, 'not_applicable', N'Not Applicable', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Bill_Status' AND Code = 'unpaid' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Bill_Status', 0, NULL, 'unpaid', N'Unpaid', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Bill_Status' AND Code = 'partially_paid' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Bill_Status', 0, NULL, 'partially_paid', N'Partially Paid', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Bill_Status' AND Code = 'paid' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Bill_Status', 0, NULL, 'paid', N'Paid', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Bill_Status' AND Code = 'waived' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Bill_Status', 0, NULL, 'waived', N'Waived', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Expense_Category' AND Code = 'festival' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Expense_Category', 0, NULL, 'festival', N'Festival & Decoration', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Expense_Category' AND Code = 'gardening' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Expense_Category', 0, NULL, 'gardening', N'Gardening & Landscaping', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Expense_Category' AND Code = 'painting' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Expense_Category', 0, NULL, 'painting', N'Painting & Renovation', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Expense_Category' AND Code = 'repairs' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Expense_Category', 0, NULL, 'repairs', N'Repairs & Maintenance', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Expense_Category' AND Code = 'security' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Expense_Category', 0, NULL, 'security', N'Security', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Expense_Category' AND Code = 'cleaning' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Expense_Category', 0, NULL, 'cleaning', N'Cleaning & Housekeeping', 5, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Expense_Category' AND Code = 'electrical' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Expense_Category', 0, NULL, 'electrical', N'Electrical', 6, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Expense_Category' AND Code = 'plumbing' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Expense_Category', 0, NULL, 'plumbing', N'Plumbing', 7, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Expense_Category' AND Code = 'water' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Expense_Category', 0, NULL, 'water', N'Water & Tanker', 8, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Expense_Category' AND Code = 'other' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Expense_Category', 0, NULL, 'other', N'Other', 9, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Payment_Method' AND Code = 'cash' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Payment_Method', 0, NULL, 'cash', N'Cash', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Payment_Method' AND Code = 'cheque' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Payment_Method', 0, NULL, 'cheque', N'Cheque', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Payment_Method' AND Code = 'upi' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Payment_Method', 0, NULL, 'upi', N'UPI', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Payment_Method' AND Code = 'bank_transfer' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Payment_Method', 0, NULL, 'bank_transfer', N'Bank Transfer', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Payment_Method' AND Code = 'online' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Payment_Method', 0, NULL, 'online', N'Online', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Payment_Status' AND Code = 'pending_approval' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Payment_Status', 0, NULL, 'pending_approval', N'Pending Approval', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Payment_Status' AND Code = 'approved' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Payment_Status', 0, NULL, 'approved', N'Approved', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Payment_Status' AND Code = 'rejected' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Payment_Status', 0, NULL, 'rejected', N'Rejected', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Plan_Frequency' AND Code = 'daily' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Plan_Frequency', 0, NULL, 'daily', N'Daily', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Plan_Frequency' AND Code = 'weekly' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Plan_Frequency', 0, NULL, 'weekly', N'Weekly', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Plan_Frequency' AND Code = 'monthly' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Plan_Frequency', 0, NULL, 'monthly', N'Monthly', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Plan_Frequency' AND Code = 'quarterly' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Plan_Frequency', 0, NULL, 'quarterly', N'Quarterly', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Plan_Frequency' AND Code = 'half_yearly' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Plan_Frequency', 0, NULL, 'half_yearly', N'Half-Yearly', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Plan_Frequency' AND Code = 'yearly' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Plan_Frequency', 0, NULL, 'yearly', N'Yearly', 5, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Maintenance_Plan_Frequency' AND Code = 'custom' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Maintenance_Plan_Frequency', 0, NULL, 'custom', N'Custom (fixed interval)', 6, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Meter_Type' AND Code = 'running_hours' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Meter_Type', 0, NULL, 'running_hours', N'Running Hours', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Meter_Type' AND Code = 'energy_kwh' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Meter_Type', 0, NULL, 'energy_kwh', N'Energy (kWh)', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Meter_Type' AND Code = 'flow' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Meter_Type', 0, NULL, 'flow', N'Flow', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Meter_Type' AND Code = 'cycles' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Meter_Type', 0, NULL, 'cycles', N'Cycles', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Meter_Unit' AND Code = 'hours' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Meter_Unit', 0, NULL, 'hours', N'Hours', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Meter_Unit' AND Code = 'kwh' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Meter_Unit', 0, NULL, 'kwh', N'kWh', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Meter_Unit' AND Code = 'litres' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Meter_Unit', 0, NULL, 'litres', N'Litres', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Meter_Unit' AND Code = 'cycles' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Meter_Unit', 0, NULL, 'cycles', N'Cycles', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Notification_Category' AND Code = 'account' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Notification_Category', 0, NULL, 'account', N'Account & Security', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Notification_Category' AND Code = 'emergency' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Notification_Category', 0, NULL, 'emergency', N'Emergency Alerts', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Notification_Category' AND Code = 'responder' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Notification_Category', 0, NULL, 'responder', N'Security Responder Alerts', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Notification_Category' AND Code = 'announcement' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Notification_Category', 0, NULL, 'announcement', N'Announcements', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Notification_Category' AND Code = 'parcel' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Notification_Category', 0, NULL, 'parcel', N'Parcels & Deliveries', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Notification_Category' AND Code = 'complaint' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Notification_Category', 0, NULL, 'complaint', N'Complaints & Tickets', 5, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Notification_Category' AND Code = 'invoice' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Notification_Category', 0, NULL, 'invoice', N'Invoices & Billing', 6, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Notification_Category' AND Code = 'facility_booking' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Notification_Category', 0, NULL, 'facility_booking', N'Facility Bookings', 7, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Notification_Category' AND Code = 'document_expiry' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Notification_Category', 0, NULL, 'document_expiry', N'Document Expiry Reminders', 8, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Notification_Category' AND Code = 'visitor' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Notification_Category', 0, NULL, 'visitor', N'Visitor Requests', 9, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Notification_Category' AND Code = 'parking' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Notification_Category', 0, NULL, 'parking', N'Parking Alerts', 10, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Notification_Category' AND Code = 'event' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Notification_Category', 0, NULL, 'event', N'Resident Events', 11, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Category' AND Code = 'standard' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Category', 0, NULL, 'standard', N'Standard', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Category' AND Code = 'medicine' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Category', 0, NULL, 'medicine', N'Medicine', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Category' AND Code = 'perishable' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Category', 0, NULL, 'perishable', N'Perishable', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Category' AND Code = 'urgent' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Category', 0, NULL, 'urgent', N'Urgent', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Category' AND Code = 'document' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Category', 0, NULL, 'document', N'Document', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Condition' AND Code = 'good' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Condition', 0, NULL, 'good', N'Good', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Condition' AND Code = 'damaged' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Condition', 0, NULL, 'damaged', N'Damaged', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Condition' AND Code = 'opened' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Condition', 0, NULL, 'opened', N'Opened / Tampered', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Condition' AND Code = 'wet' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Condition', 0, NULL, 'wet', N'Wet', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status' AND Code = 'expected' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status', 0, NULL, 'expected', N'Expected', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status' AND Code = 'received' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status', 0, NULL, 'received', N'Received', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status' AND Code = 'owner_not_available' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status', 0, NULL, 'owner_not_available', N'Owner Not Available', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status' AND Code = 'in_custody' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status', 0, NULL, 'in_custody', N'In Custody', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status' AND Code = 'ready_for_pickup' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status', 0, NULL, 'ready_for_pickup', N'Ready for Pickup', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status' AND Code = 'handed_over' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status', 0, NULL, 'handed_over', N'Handed Over', 5, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status' AND Code = 'returned' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status', 0, NULL, 'returned', N'Returned', 6, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status' AND Code = 'lost_damaged' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status', 0, NULL, 'lost_damaged', N'Lost / Damaged', 7, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status_Transition' AND Code = 'expected->received' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status_Transition', 0, NULL, 'expected->received', N'Expected â†’ Received', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status_Transition' AND Code = 'received->in_custody' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status_Transition', 0, NULL, 'received->in_custody', N'Received â†’ In Custody', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status_Transition' AND Code = 'received->owner_not_available' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status_Transition', 0, NULL, 'received->owner_not_available', N'Received â†’ Owner Not Available', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status_Transition' AND Code = 'received->ready_for_pickup' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status_Transition', 0, NULL, 'received->ready_for_pickup', N'Received â†’ Ready for Pickup', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status_Transition' AND Code = 'received->handed_over' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status_Transition', 0, NULL, 'received->handed_over', N'Received â†’ Handed Over', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status_Transition' AND Code = 'received->lost_damaged' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status_Transition', 0, NULL, 'received->lost_damaged', N'Received â†’ Lost / Damaged', 5, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status_Transition' AND Code = 'owner_not_available->in_custody' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status_Transition', 0, NULL, 'owner_not_available->in_custody', N'Owner Not Available â†’ In Custody', 6, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status_Transition' AND Code = 'owner_not_available->returned' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status_Transition', 0, NULL, 'owner_not_available->returned', N'Owner Not Available â†’ Returned', 7, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status_Transition' AND Code = 'in_custody->ready_for_pickup' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status_Transition', 0, NULL, 'in_custody->ready_for_pickup', N'In Custody â†’ Ready for Pickup', 8, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status_Transition' AND Code = 'in_custody->handed_over' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status_Transition', 0, NULL, 'in_custody->handed_over', N'In Custody â†’ Handed Over', 9, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status_Transition' AND Code = 'in_custody->returned' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status_Transition', 0, NULL, 'in_custody->returned', N'In Custody â†’ Returned', 10, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status_Transition' AND Code = 'in_custody->lost_damaged' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status_Transition', 0, NULL, 'in_custody->lost_damaged', N'In Custody â†’ Lost / Damaged', 11, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status_Transition' AND Code = 'ready_for_pickup->handed_over' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status_Transition', 0, NULL, 'ready_for_pickup->handed_over', N'Ready for Pickup â†’ Handed Over', 12, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status_Transition' AND Code = 'ready_for_pickup->in_custody' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status_Transition', 0, NULL, 'ready_for_pickup->in_custody', N'Ready for Pickup â†’ In Custody', 13, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status_Transition' AND Code = 'ready_for_pickup->returned' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status_Transition', 0, NULL, 'ready_for_pickup->returned', N'Ready for Pickup â†’ Returned', 14, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Status_Transition' AND Code = 'ready_for_pickup->lost_damaged' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Status_Transition', 0, NULL, 'ready_for_pickup->lost_damaged', N'Ready for Pickup â†’ Lost / Damaged', 15, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Verification_Method' AND Code = 'otp' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Verification_Method', 0, NULL, 'otp', N'OTP', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Verification_Method' AND Code = 'qr' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Verification_Method', 0, NULL, 'qr', N'QR Code', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Verification_Method' AND Code = 'id' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Verification_Method', 0, NULL, 'id', N'ID Verification', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parcel_Verification_Method' AND Code = 'combination' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parcel_Verification_Method', 0, NULL, 'combination', N'Combination (OTP + QR + ID)', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Assignment_Type' AND Code = 'permanent' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Assignment_Type', 0, NULL, 'permanent', N'Permanent', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Assignment_Type' AND Code = 'temporary' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Assignment_Type', 0, NULL, 'temporary', N'Temporary', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Assignment_Type' AND Code = 'visitor' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Assignment_Type', 0, NULL, 'visitor', N'Visitor', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Slot_Type' AND Code = 'open' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Slot_Type', 0, NULL, 'open', N'Open', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Slot_Type' AND Code = 'covered' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Slot_Type', 0, NULL, 'covered', N'Covered', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Slot_Type' AND Code = 'visitor' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Slot_Type', 0, NULL, 'visitor', N'Visitor', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Slot_Type' AND Code = 'ev' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Slot_Type', 0, NULL, 'ev', N'EV Charging', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Slot_Type' AND Code = 'accessible' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Slot_Type', 0, NULL, 'accessible', N'Accessible', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Violation_Status' AND Code = 'open' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Violation_Status', 0, NULL, 'open', N'Open', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Violation_Status' AND Code = 'resolved' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Violation_Status', 0, NULL, 'resolved', N'Resolved', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Violation_Status' AND Code = 'waived' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Violation_Status', 0, NULL, 'waived', N'Waived', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Violation_Type' AND Code = 'wrong_slot' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Violation_Type', 0, NULL, 'wrong_slot', N'Parked in Wrong Slot', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Violation_Type' AND Code = 'overstay' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Violation_Type', 0, NULL, 'overstay', N'Overstay', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Violation_Type' AND Code = 'blocking' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Violation_Type', 0, NULL, 'blocking', N'Blocking / Obstruction', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Violation_Type' AND Code = 'no_permit' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Violation_Type', 0, NULL, 'no_permit', N'No Parking Permit', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Violation_Type' AND Code = 'unauthorized' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Violation_Type', 0, NULL, 'unauthorized', N'Unauthorized Vehicle', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Zone_Type' AND Code = 'surface' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Zone_Type', 0, NULL, 'surface', N'Surface', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Zone_Type' AND Code = 'basement' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Zone_Type', 0, NULL, 'basement', N'Basement', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Zone_Type' AND Code = 'podium' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Zone_Type', 0, NULL, 'podium', N'Podium / Stilt', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Zone_Type' AND Code = 'visitor' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Zone_Type', 0, NULL, 'visitor', N'Visitor', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Parking_Zone_Type' AND Code = 'ev' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Parking_Zone_Type', 0, NULL, 'ev', N'EV Charging', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Pet_Type' AND Code = 'dog' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Pet_Type', 0, NULL, 'dog', N'Dog', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Pet_Type' AND Code = 'cat' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Pet_Type', 0, NULL, 'cat', N'Cat', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Pet_Type' AND Code = 'bird' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Pet_Type', 0, NULL, 'bird', N'Bird', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Pet_Type' AND Code = 'other' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Pet_Type', 0, NULL, 'other', N'Other', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Recurring_Visitor_Status' AND Code = 'active' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Recurring_Visitor_Status', 0, NULL, 'active', N'Active', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Recurring_Visitor_Status' AND Code = 'revoked' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Recurring_Visitor_Status', 0, NULL, 'revoked', N'Revoked', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Resident_Event_Status' AND Code = 'published' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Resident_Event_Status', 0, NULL, 'published', N'Published', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Resident_Event_Status' AND Code = 'cancelled' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Resident_Event_Status', 0, NULL, 'cancelled', N'Cancelled', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Resident_Event_Status' AND Code = 'completed' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Resident_Event_Status', 0, NULL, 'completed', N'Completed', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Resident_Type' AND Code = 'owner' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Resident_Type', 0, NULL, 'owner', N'Owner', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Resident_Type' AND Code = 'co_owner' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Resident_Type', 0, NULL, 'co_owner', N'Co-Owner', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Resident_Type' AND Code = 'tenant' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Resident_Type', 0, NULL, 'tenant', N'Tenant', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Resident_Type' AND Code = 'family_member' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Resident_Type', 0, NULL, 'family_member', N'Family Member', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Resident_Type' AND Code = 'security' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Resident_Type', 0, NULL, 'security', N'Security / Guard', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Resident_Verification_Status' AND Code = 'pending' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Resident_Verification_Status', 0, NULL, 'pending', N'Pending', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Resident_Verification_Status' AND Code = 'verified' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Resident_Verification_Status', 0, NULL, 'verified', N'Verified', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Resident_Verification_Status' AND Code = 'rejected' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Resident_Verification_Status', 0, NULL, 'rejected', N'Rejected', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Sos_Alert_Status' AND Code = 'active' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Sos_Alert_Status', 0, NULL, 'active', N'Active', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Sos_Alert_Status' AND Code = 'acknowledged' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Sos_Alert_Status', 0, NULL, 'acknowledged', N'Acknowledged', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Sos_Alert_Status' AND Code = 'resolved' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Sos_Alert_Status', 0, NULL, 'resolved', N'Resolved', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Category' AND Code = 'plumbing' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Category', 0, NULL, 'plumbing', N'Plumbing', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Category' AND Code = 'electrical' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Category', 0, NULL, 'electrical', N'Electrical', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Category' AND Code = 'housekeeping' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Category', 0, NULL, 'housekeeping', N'Housekeeping', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Category' AND Code = 'security' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Category', 0, NULL, 'security', N'Security', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Category' AND Code = 'common_area' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Category', 0, NULL, 'common_area', N'Common Area', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Category' AND Code = 'other' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Category', 0, NULL, 'other', N'Other', 5, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Priority' AND Code = 'low' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Priority', 0, NULL, 'low', N'Low', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Priority' AND Code = 'medium' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Priority', 0, NULL, 'medium', N'Medium', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Priority' AND Code = 'high' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Priority', 0, NULL, 'high', N'High', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Priority' AND Code = 'urgent' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Priority', 0, NULL, 'urgent', N'Urgent', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status' AND Code = 'new' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status', 0, NULL, 'new', N'New', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status' AND Code = 'assigned' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status', 0, NULL, 'assigned', N'Assigned', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status' AND Code = 'in_progress' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status', 0, NULL, 'in_progress', N'In Progress', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status' AND Code = 'waiting' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status', 0, NULL, 'waiting', N'Waiting', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status' AND Code = 'resolved' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status', 0, NULL, 'resolved', N'Resolved', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status' AND Code = 'closed' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status', 0, NULL, 'closed', N'Closed', 5, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status' AND Code = 'reopened' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status', 0, NULL, 'reopened', N'Reopened', 6, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status_Transition' AND Code = 'new->assigned' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status_Transition', 0, NULL, 'new->assigned', N'New â†’ Assigned', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status_Transition' AND Code = 'assigned->in_progress' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status_Transition', 0, NULL, 'assigned->in_progress', N'Assigned â†’ In Progress', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status_Transition' AND Code = 'assigned->waiting' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status_Transition', 0, NULL, 'assigned->waiting', N'Assigned â†’ Waiting', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status_Transition' AND Code = 'assigned->assigned' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status_Transition', 0, NULL, 'assigned->assigned', N'Assigned â†’ Assigned (re-assign)', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status_Transition' AND Code = 'in_progress->waiting' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status_Transition', 0, NULL, 'in_progress->waiting', N'In Progress â†’ Waiting', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status_Transition' AND Code = 'in_progress->resolved' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status_Transition', 0, NULL, 'in_progress->resolved', N'In Progress â†’ Resolved', 5, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status_Transition' AND Code = 'in_progress->assigned' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status_Transition', 0, NULL, 'in_progress->assigned', N'In Progress â†’ Assigned (re-assign)', 6, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status_Transition' AND Code = 'waiting->in_progress' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status_Transition', 0, NULL, 'waiting->in_progress', N'Waiting â†’ In Progress', 7, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status_Transition' AND Code = 'waiting->resolved' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status_Transition', 0, NULL, 'waiting->resolved', N'Waiting â†’ Resolved', 8, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status_Transition' AND Code = 'waiting->assigned' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status_Transition', 0, NULL, 'waiting->assigned', N'Waiting â†’ Assigned (re-assign)', 9, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status_Transition' AND Code = 'resolved->closed' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status_Transition', 0, NULL, 'resolved->closed', N'Resolved â†’ Closed', 10, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status_Transition' AND Code = 'resolved->reopened' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status_Transition', 0, NULL, 'resolved->reopened', N'Resolved â†’ Reopened', 11, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status_Transition' AND Code = 'closed->reopened' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status_Transition', 0, NULL, 'closed->reopened', N'Closed â†’ Reopened', 12, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Status_Transition' AND Code = 'reopened->assigned' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Status_Transition', 0, NULL, 'reopened->assigned', N'Reopened â†’ Assigned', 13, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Subcategory' AND Code = 'leak' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Subcategory', 0, NULL, 'leak', N'Leak', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Subcategory' AND Code = 'blockage' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Subcategory', 0, NULL, 'blockage', N'Blockage', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Subcategory' AND Code = 'no_power' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Subcategory', 0, NULL, 'no_power', N'No Power', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Subcategory' AND Code = 'short_circuit' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Subcategory', 0, NULL, 'short_circuit', N'Short Circuit', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Subcategory' AND Code = 'cleaning' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Subcategory', 0, NULL, 'cleaning', N'Cleaning', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Subcategory' AND Code = 'waste' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Subcategory', 0, NULL, 'waste', N'Waste Removal', 5, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Subcategory' AND Code = 'lighting' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Subcategory', 0, NULL, 'lighting', N'Lighting', 6, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Ticket_Subcategory' AND Code = 'other' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Ticket_Subcategory', 0, NULL, 'other', N'Other', 7, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Unit_Status' AND Code = 'available' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Unit_Status', 0, NULL, 'available', N'Available', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Unit_Status' AND Code = 'occupied' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Unit_Status', 0, NULL, 'occupied', N'Occupied', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Unit_Status' AND Code = 'vacant' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Unit_Status', 0, NULL, 'vacant', N'Vacant', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Unit_Status' AND Code = 'under_maintenance' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Unit_Status', 0, NULL, 'under_maintenance', N'Under Maintenance', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Unit_Status' AND Code = 'blocked' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Unit_Status', 0, NULL, 'blocked', N'Blocked', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Unit_Type' AND Code = 'apartment' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Unit_Type', 0, NULL, 'apartment', N'Apartment', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Unit_Type' AND Code = 'villa' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Unit_Type', 0, NULL, 'villa', N'Villa', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Unit_Type' AND Code = 'bungalow' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Unit_Type', 0, NULL, 'bungalow', N'Bungalow', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Unit_Type' AND Code = 'townhouse' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Unit_Type', 0, NULL, 'townhouse', N'Townhouse', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Unit_Type' AND Code = 'plot' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Unit_Type', 0, NULL, 'plot', N'Plot', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Unit_Type' AND Code = 'parking' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Unit_Type', 0, NULL, 'parking', N'Parking', 5, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Vehicle_Type' AND Code = 'two_wheeler' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Vehicle_Type', 0, NULL, 'two_wheeler', N'Two Wheeler', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Vehicle_Type' AND Code = 'four_wheeler' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Vehicle_Type', 0, NULL, 'four_wheeler', N'Four Wheeler', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Vehicle_Type' AND Code = 'bicycle' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Vehicle_Type', 0, NULL, 'bicycle', N'Bicycle', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Vehicle_Type' AND Code = 'commercial' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Vehicle_Type', 0, NULL, 'commercial', N'Commercial', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Vehicle_Type' AND Code = 'other' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Vehicle_Type', 0, NULL, 'other', N'Other', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Visit_Pass_Status' AND Code = 'active' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Visit_Pass_Status', 0, NULL, 'active', N'Active', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Visit_Pass_Status' AND Code = 'used' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Visit_Pass_Status', 0, NULL, 'used', N'Used', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Visit_Pass_Status' AND Code = 'expired' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Visit_Pass_Status', 0, NULL, 'expired', N'Expired', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Visit_Pass_Status' AND Code = 'revoked' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Visit_Pass_Status', 0, NULL, 'revoked', N'Revoked', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Visitor_Category' AND Code = 'guest' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Visitor_Category', 0, NULL, 'guest', N'Guest', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Visitor_Category' AND Code = 'cab' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Visitor_Category', 0, NULL, 'cab', N'Cab', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Visitor_Category' AND Code = 'delivery' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Visitor_Category', 0, NULL, 'delivery', N'Delivery', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Visitor_Category' AND Code = 'service_provider' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Visitor_Category', 0, NULL, 'service_provider', N'Service Provider', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Visitor_Category' AND Code = 'contractor' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Visitor_Category', 0, NULL, 'contractor', N'Contractor', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Visitor_Status' AND Code = 'pending' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Visitor_Status', 0, NULL, 'pending', N'Pending', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Visitor_Status' AND Code = 'approved' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Visitor_Status', 0, NULL, 'approved', N'Approved', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Visitor_Status' AND Code = 'rejected' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Visitor_Status', 0, NULL, 'rejected', N'Rejected', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Visitor_Status' AND Code = 'expired' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Visitor_Status', 0, NULL, 'expired', N'Expired', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Visitor_Status' AND Code = 'entered' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Visitor_Status', 0, NULL, 'entered', N'Entered', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Visitor_Status' AND Code = 'exited' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Visitor_Status', 0, NULL, 'exited', N'Exited', 5, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Watchlist_Severity' AND Code = 'low' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Watchlist_Severity', 0, NULL, 'low', N'Low', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Watchlist_Severity' AND Code = 'medium' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Watchlist_Severity', 0, NULL, 'medium', N'Medium', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Watchlist_Severity' AND Code = 'high' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Watchlist_Severity', 0, NULL, 'high', N'High', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Watchlist_Severity' AND Code = 'critical' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Watchlist_Severity', 0, NULL, 'critical', N'Critical', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Watchlist_Status' AND Code = 'active' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Watchlist_Status', 0, NULL, 'active', N'Active', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Watchlist_Status' AND Code = 'cleared' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Watchlist_Status', 0, NULL, 'cleared', N'Cleared', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Watchlist_Subject_Type' AND Code = 'person' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Watchlist_Subject_Type', 0, NULL, 'person', N'Person', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Watchlist_Subject_Type' AND Code = 'vehicle' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Watchlist_Subject_Type', 0, NULL, 'vehicle', N'Vehicle', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Priority' AND Code = 'low' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Priority', 0, NULL, 'low', N'Low', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Priority' AND Code = 'medium' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Priority', 0, NULL, 'medium', N'Medium', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Priority' AND Code = 'high' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Priority', 0, NULL, 'high', N'High', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Priority' AND Code = 'urgent' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Priority', 0, NULL, 'urgent', N'Urgent', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status' AND Code = 'open' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status', 0, NULL, 'open', N'Open', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status' AND Code = 'assigned' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status', 0, NULL, 'assigned', N'Assigned', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status' AND Code = 'in_progress' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status', 0, NULL, 'in_progress', N'In Progress', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status' AND Code = 'on_hold' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status', 0, NULL, 'on_hold', N'On Hold', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status' AND Code = 'completed' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status', 0, NULL, 'completed', N'Completed', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status' AND Code = 'cancelled' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status', 0, NULL, 'cancelled', N'Cancelled', 5, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status_Transition' AND Code = 'open->assigned' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status_Transition', 0, NULL, 'open->assigned', N'Open â†’ Assigned', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status_Transition' AND Code = 'open->cancelled' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status_Transition', 0, NULL, 'open->cancelled', N'Open â†’ Cancelled', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status_Transition' AND Code = 'assigned->in_progress' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status_Transition', 0, NULL, 'assigned->in_progress', N'Assigned â†’ In Progress', 2, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status_Transition' AND Code = 'assigned->on_hold' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status_Transition', 0, NULL, 'assigned->on_hold', N'Assigned â†’ On Hold', 3, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status_Transition' AND Code = 'assigned->assigned' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status_Transition', 0, NULL, 'assigned->assigned', N'Assigned â†’ Assigned (re-assign)', 4, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status_Transition' AND Code = 'assigned->completed' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status_Transition', 0, NULL, 'assigned->completed', N'Assigned â†’ Completed', 5, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status_Transition' AND Code = 'assigned->cancelled' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status_Transition', 0, NULL, 'assigned->cancelled', N'Assigned â†’ Cancelled', 6, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status_Transition' AND Code = 'in_progress->on_hold' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status_Transition', 0, NULL, 'in_progress->on_hold', N'In Progress â†’ On Hold', 7, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status_Transition' AND Code = 'in_progress->completed' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status_Transition', 0, NULL, 'in_progress->completed', N'In Progress â†’ Completed', 8, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status_Transition' AND Code = 'in_progress->assigned' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status_Transition', 0, NULL, 'in_progress->assigned', N'In Progress â†’ Assigned (re-assign)', 9, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status_Transition' AND Code = 'in_progress->cancelled' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status_Transition', 0, NULL, 'in_progress->cancelled', N'In Progress â†’ Cancelled', 10, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status_Transition' AND Code = 'on_hold->in_progress' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status_Transition', 0, NULL, 'on_hold->in_progress', N'On Hold â†’ In Progress', 11, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status_Transition' AND Code = 'on_hold->assigned' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status_Transition', 0, NULL, 'on_hold->assigned', N'On Hold â†’ Assigned (re-assign)', 12, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status_Transition' AND Code = 'on_hold->completed' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status_Transition', 0, NULL, 'on_hold->completed', N'On Hold â†’ Completed', 13, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Status_Transition' AND Code = 'on_hold->cancelled' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Status_Transition', 0, NULL, 'on_hold->cancelled', N'On Hold â†’ Cancelled', 14, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Type' AND Code = 'preventive' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Type', 0, NULL, 'preventive', N'Preventive', 0, 1, 0, SYSUTCDATETIME(), 'master-seed');
IF NOT EXISTS (SELECT 1 FROM dbo.MasterDataItems WHERE ListKey = 'Work_Order_Type' AND Code = 'corrective' AND CommunityId IS NULL)
    INSERT INTO dbo.MasterDataItems (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy) VALUES (NEWID(), 'Work_Order_Type', 0, NULL, 'corrective', N'Corrective', 1, 1, 0, SYSUTCDATETIME(), 'master-seed');
GO
PRINT '>>> All Master Migration & Seed Data initialized successfully!';
GO
