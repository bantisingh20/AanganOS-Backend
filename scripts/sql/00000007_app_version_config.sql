-- =====================================================================================================
-- Migration Script: 00000007_app_version_config.sql
-- Purpose:
--   Platform-wide settings read by the public GET /api/v1/app-version endpoint. The Android app compares
--   its own versionCode against them on start / resume:
--     versionCode <  minVersionCode    -> blocking "Update required" screen
--     versionCode <  latestVersionCode -> dismissible "Update available" prompt (once per version)
--
--   After publishing a new APK, edit these in admin -> Config (no deploy needed):
--     app.android.latestVersionCode  e.g. 10002
--     app.android.latestVersionName  e.g. 1.0.2
--     app.android.minVersionCode     raise only when old builds must stop working
--     app.android.updateUrl          where users download the new build (Play Store or APK link)
--     app.android.updateMessage      optional text shown in the prompt
--
--   Seeds are inserted only when missing, so values already edited in admin are never overwritten.
--   Starting values: latest = min = 1 (matches the current build), so nobody is prompted yet.
-- Safe & Idempotent.
-- =====================================================================================================

PRINT 'Executing 00000007_app_version_config.sql...';
GO

DECLARE @seed TABLE ([Key] NVARCHAR(200), [Value] NVARCHAR(MAX));
INSERT INTO @seed ([Key], [Value]) VALUES
  ('app.android.latestVersionCode', '1'),
  ('app.android.latestVersionName', '1.0'),
  ('app.android.minVersionCode',    '1'),
  ('app.android.updateUrl',         ''),
  ('app.android.updateMessage',     '');

INSERT INTO dbo.Configurations (Id, [Key], Scope, CommunityId, OrganizationId, [Value], IsDeleted, CreatedAtUtc, CreatedBy)
SELECT NEWID(), s.[Key], 0, NULL, NULL, s.[Value], 0, SYSUTCDATETIME(), 'migration-00000007'
FROM @seed s
WHERE NOT EXISTS (
  SELECT 1 FROM dbo.Configurations c
  WHERE c.[Key] = s.[Key] AND c.Scope = 0 AND c.CommunityId IS NULL AND c.OrganizationId IS NULL AND c.IsDeleted = 0
);

PRINT CONCAT('App version config rows inserted: ', @@ROWCOUNT);
GO
