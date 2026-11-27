-- =====================================================================================================
-- Migration Script: 00000005_move_helpdesk_route.sql
-- Purpose:
--   The Help Desk menu item ([Key] = 'tickets', Surface = 2 / Common) pointed at '/maintenance'.
--   In the mobile app '/maintenance' is "My Maintenance" (resident bills, [Key] = 'app.maintenance'),
--   so tapping Help Desk in the app opened the bills screen.
--
--   Help Desk now has its own route in BOTH apps:
--     /helpdesk          ticket list
--     /helpdesk/new      raise a ticket
--     /helpdesk/:id      ticket details
--   Unchanged: '/maintenance' = My Maintenance (app), '/billing' = Maintenance Billing (both).
--   Old URLs (/maintenance in admin, /tickets, /my-tickets, /raise-ticket) redirect in the frontend.
--
--   Deploy order: ship the admin + app frontend that knows '/helpdesk' BEFORE this script runs
--   (the API runs pending scripts on startup).
--
-- Safe & Idempotent (can be executed multiple times without errors or data changes).
-- Rollback:  UPDATE dbo.MenuItems SET Route = '/maintenance' WHERE [Key] = 'tickets' AND IsDeleted = 0;
-- =====================================================================================================

PRINT 'Executing 00000005_move_helpdesk_route.sql...';
GO

DECLARE @moved INT;

UPDATE dbo.MenuItems
SET Route = '/helpdesk',
    UpdatedAtUtc = SYSUTCDATETIME(),
    UpdatedBy = 'migration-00000005'
WHERE [Key] = 'tickets'
  AND IsDeleted = 0
  AND (Route IS NULL OR Route <> '/helpdesk');

SET @moved = @@ROWCOUNT;
PRINT CONCAT('Help Desk menu rows moved to /helpdesk: ', @moved);
GO
