SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;

DECLARE @rows TABLE (Code NVARCHAR(128), Label NVARCHAR(256), SortOrder INT);

INSERT INTO @rows (Code, Label, SortOrder) VALUES
 ('festival', 'Festival & Decoration', 0),
 ('gardening', 'Gardening & Landscaping', 1),
 ('painting', 'Painting & Renovation', 2),
 ('repairs', 'Repairs & Maintenance', 3),
 ('security', 'Security', 4),
 ('cleaning', 'Cleaning & Housekeeping', 5),
 ('electrical', 'Electrical', 6),
 ('plumbing', 'Plumbing', 7),
 ('water', 'Water & Tanker', 8),
 ('other', 'Other', 9);

INSERT INTO MasterDataItems
 (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy)
SELECT NEWID(), 'Maintenance_Expense_Category', 0, NULL, r.Code, r.Label, r.SortOrder, 1, 0, '2026-01-01', 'seed'
FROM @rows r
WHERE NOT EXISTS (
    SELECT 1 FROM MasterDataItems m
    WHERE m.ListKey = 'Maintenance_Expense_Category' AND m.Code = r.Code AND m.CommunityId IS NULL
);

SELECT COUNT(*) AS ExpenseCategories FROM MasterDataItems WHERE ListKey = 'Maintenance_Expense_Category';
