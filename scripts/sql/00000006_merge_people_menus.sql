-- =====================================================================================================
-- Migration Script: 00000006_merge_people_menus.sql
-- Purpose:
--   "Residents" (/residents) and "Manage People" (/users) are now ONE shared People page in both
--   apps (KYC tabs + Approve/Reject + add/edit/delete). Showing both in the sidebar is a duplicate.
--
--   - Keep [Key] = 'residents' (the app's "Manage Residents" tile is mapped to this key) and label it
--     'Manage People'.
--   - Hide [Key] = 'admin.users' from the sidebar. The /users URL keeps working.
--
--   Checked on 27 Nov 2026: every role that can view one entry can view the other
--   (CommunityAdmin, OrganizationAdmin, Superadmin hold both 'residents' and 'admin.users' View),
--   so nobody loses access.
--
-- Safe & Idempotent.
-- Rollback:
--   UPDATE dbo.MenuItems SET ShowInSidebar = 1 WHERE [Key] = 'admin.users' AND IsDeleted = 0;
--   UPDATE dbo.MenuItems SET Label = N'Residents' WHERE [Key] = 'residents' AND IsDeleted = 0;
-- =====================================================================================================

PRINT 'Executing 00000006_merge_people_menus.sql...';
GO

UPDATE dbo.MenuItems
SET Label = N'Manage People',
    UpdatedAtUtc = SYSUTCDATETIME(),
    UpdatedBy = 'migration-00000006'
WHERE [Key] = 'residents'
  AND IsDeleted = 0
  AND Label <> N'Manage People';
GO

UPDATE dbo.MenuItems
SET ShowInSidebar = 0,
    UpdatedAtUtc = SYSUTCDATETIME(),
    UpdatedBy = 'migration-00000006'
WHERE [Key] = 'admin.users'
  AND IsDeleted = 0
  AND ShowInSidebar = 1;
GO
