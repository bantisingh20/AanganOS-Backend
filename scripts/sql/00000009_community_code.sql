-- =====================================================================================================
-- Migration Script: 00000009_community_code.sql
-- Purpose:
--   Communities (wings / towers) get a short Code, e.g. 'A' for "A Wing". Unit numbers stay SAVED
--   with the prefix as before ('A-101'); dropdowns show "Code-UnitNumber" (A-101) via unitLabel(),
--   which never doubles a prefix that is already there.
--
--   1. Add dbo.Communities.Code NVARCHAR(20) NULL.
--   2. Backfill a code for OLD communities, best source first:
--      a) Unit prefix — the prefix most of the community's saved unit numbers share:
--         'A-101', 'A/101', 'A 101' → 'A'; 'A101' → 'A' (letters followed by a digit).
--         Used only when at least 80% of its (non-deleted) units carry that same prefix.
--      b) Name — "<X> Wing", "Wing <X>", "Tower <X>", "Block <X>", "Building <X>", "<X> Block",
--         "<X> Tower" → X; or a name that is itself one short word (≤ 5 chars, e.g. "A", "B1").
--      X must be one word (no spaces), ≤ 20 characters.
--      A code is written only when it is unique within the organization: no other wing already
--      has it and no other wing would get it in this run (those are left NULL for an admin to set).
--   3. Prints every community still without a code, so it can be filled in admin → Communities.
--
-- Safe & Idempotent: only fills NULL codes; never overwrites a code set in admin; unit numbers are
-- not touched.
-- Rollback:  ALTER TABLE dbo.Communities DROP COLUMN Code;
--   (or, to undo only the backfill: UPDATE dbo.Communities SET Code = NULL WHERE Code IS NOT NULL;
--    before any admin has set codes by hand)
-- =====================================================================================================

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Communities') AND name = 'Code')
BEGIN
    ALTER TABLE dbo.Communities ADD Code NVARCHAR(20) NULL;
    PRINT 'Added Code column to dbo.Communities.';
END
GO

-- -----------------------------------------------------------------------------------------------------
-- 2a. From unit-number prefixes
-- -----------------------------------------------------------------------------------------------------
;WITH U AS (
    SELECT u.CommunityId, LTRIM(RTRIM(u.UnitNumber)) AS N
    FROM dbo.Units u
    INNER JOIN dbo.Communities c ON c.Id = u.CommunityId AND c.IsDeleted = 0 AND c.Code IS NULL
    WHERE u.IsDeleted = 0
),
P AS (
    SELECT CommunityId,
           CASE
               -- 'A-101' / 'A/101' / 'A 101': text before the first separator, when a digit follows it
               WHEN PATINDEX('%[-/ ]%', N) > 1
                    AND SUBSTRING(N, PATINDEX('%[-/ ]%', N) + 1, 1) LIKE '[0-9]'
                   THEN LEFT(N, PATINDEX('%[-/ ]%', N) - 1)
               -- 'A101': leading letters directly followed by a digit
               WHEN PATINDEX('%[0-9]%', N) > 1
                    AND LEFT(N, PATINDEX('%[0-9]%', N) - 1) NOT LIKE '%[^A-Za-z]%'
                   THEN LEFT(N, PATINDEX('%[0-9]%', N) - 1)
           END AS Prefix
    FROM U
),
Totals AS (
    SELECT CommunityId, COUNT(*) AS UnitCount FROM U GROUP BY CommunityId
),
Ranked AS (
    SELECT p.CommunityId, p.Prefix, COUNT(*) AS Hits,
           ROW_NUMBER() OVER (PARTITION BY p.CommunityId ORDER BY COUNT(*) DESC, p.Prefix) AS rn
    FROM P p
    WHERE p.Prefix IS NOT NULL AND LEN(p.Prefix) BETWEEN 1 AND 20 AND CHARINDEX(' ', p.Prefix) = 0
    GROUP BY p.CommunityId, p.Prefix
),
Best AS (
    SELECT r.CommunityId, c.OrganizationId, r.Prefix AS NewCode
    FROM Ranked r
    INNER JOIN Totals t ON t.CommunityId = r.CommunityId
    INNER JOIN dbo.Communities c ON c.Id = r.CommunityId
    WHERE r.rn = 1 AND r.Hits * 100 >= t.UnitCount * 80
),
Valid AS (
    SELECT *, COUNT(*) OVER (PARTITION BY OrganizationId, NewCode) AS SameCodeInOrg FROM Best
)
UPDATE c
SET c.Code = v.NewCode
FROM dbo.Communities c
INNER JOIN Valid v ON v.CommunityId = c.Id
WHERE v.SameCodeInOrg = 1
  AND c.Code IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.Communities o
      WHERE o.OrganizationId = v.OrganizationId AND o.IsDeleted = 0 AND o.Code = v.NewCode AND o.Id <> c.Id
  );
PRINT CONCAT('00000009: code from unit prefixes for ', @@ROWCOUNT, ' community(ies).');
GO

-- -----------------------------------------------------------------------------------------------------
-- 2b. From the community name
-- -----------------------------------------------------------------------------------------------------
;WITH Names AS (
    SELECT c.Id, c.OrganizationId, LTRIM(RTRIM(c.Name)) AS Nm
    FROM dbo.Communities c
    WHERE c.IsDeleted = 0 AND c.Code IS NULL
),
Candidates AS (
    SELECT Id, OrganizationId,
           LTRIM(RTRIM(CASE
               WHEN Nm LIKE '% Wing'      THEN LEFT(Nm, LEN(Nm) - 5)
               WHEN Nm LIKE '% Block'     THEN LEFT(Nm, LEN(Nm) - 6)
               WHEN Nm LIKE '% Tower'     THEN LEFT(Nm, LEN(Nm) - 6)
               WHEN Nm LIKE 'Wing %'      THEN SUBSTRING(Nm, 6, 200)
               WHEN Nm LIKE 'Tower %'     THEN SUBSTRING(Nm, 7, 200)
               WHEN Nm LIKE 'Block %'     THEN SUBSTRING(Nm, 7, 200)
               WHEN Nm LIKE 'Building %'  THEN SUBSTRING(Nm, 10, 200)
               WHEN LEN(Nm) <= 5 AND CHARINDEX(' ', Nm) = 0 THEN Nm
           END)) AS NewCode
    FROM Names
),
Valid AS (
    SELECT Id, OrganizationId, NewCode,
           COUNT(*) OVER (PARTITION BY OrganizationId, NewCode) AS SameCodeInOrg
    FROM Candidates
    WHERE NewCode IS NOT NULL AND NewCode <> '' AND LEN(NewCode) <= 20 AND CHARINDEX(' ', NewCode) = 0
)
UPDATE c
SET c.Code = v.NewCode
FROM dbo.Communities c
INNER JOIN Valid v ON v.Id = c.Id
WHERE v.SameCodeInOrg = 1
  AND c.Code IS NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.Communities o
      WHERE o.OrganizationId = v.OrganizationId AND o.IsDeleted = 0 AND o.Code = v.NewCode AND o.Id <> v.Id
  );
PRINT CONCAT('00000009: code from names for ', @@ROWCOUNT, ' community(ies).');
GO

-- -----------------------------------------------------------------------------------------------------
-- 3. Report what is still missing (set these in admin → Communities)
-- -----------------------------------------------------------------------------------------------------
DECLARE @missing INT = (SELECT COUNT(*) FROM dbo.Communities WHERE IsDeleted = 0 AND Code IS NULL);
PRINT CONCAT('00000009: ', @missing, ' community(ies) still without a code.');
DECLARE @list NVARCHAR(MAX) = (
    SELECT STRING_AGG(CAST(Name AS NVARCHAR(MAX)), ', ')
    FROM (SELECT TOP 50 Name FROM dbo.Communities WHERE IsDeleted = 0 AND Code IS NULL ORDER BY Name) x
);
IF @list IS NOT NULL PRINT CONCAT('00000009: without code (first 50): ', @list);
GO

PRINT '00000009_community_code.sql executed successfully.';
GO
