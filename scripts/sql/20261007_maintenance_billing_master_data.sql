SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

DECLARE @rows TABLE (ListKey NVARCHAR(128), Code NVARCHAR(128), Label NVARCHAR(256), SortOrder INT);

INSERT INTO @rows (ListKey, Code, Label, SortOrder) VALUES
 ('Maintenance_Bill_Status', 'unpaid', 'Unpaid', 0),
 ('Maintenance_Bill_Status', 'partially_paid', 'Partially Paid', 1),
 ('Maintenance_Bill_Status', 'paid', 'Paid', 2),
 ('Maintenance_Bill_Status', 'waived', 'Waived', 3),
 ('Maintenance_Payment_Method', 'cash', 'Cash', 0),
 ('Maintenance_Payment_Method', 'cheque', 'Cheque', 1),
 ('Maintenance_Payment_Method', 'upi', 'UPI', 2),
 ('Maintenance_Payment_Method', 'bank_transfer', 'Bank Transfer', 3),
 ('Maintenance_Payment_Method', 'online', 'Online', 4),
 ('Maintenance_Payment_Status', 'pending_approval', 'Pending Approval', 0),
 ('Maintenance_Payment_Status', 'approved', 'Approved', 1),
 ('Maintenance_Payment_Status', 'rejected', 'Rejected', 2);

INSERT INTO MasterDataItems
 (Id, ListKey, Scope, CommunityId, Code, Label, SortOrder, IsActive, IsDeleted, CreatedAtUtc, CreatedBy)
SELECT NEWID(), r.ListKey, 0, NULL, r.Code, r.Label, r.SortOrder, 1, 0, '2026-01-01', 'seed'
FROM @rows r
WHERE NOT EXISTS (
    SELECT 1 FROM MasterDataItems m
    WHERE m.ListKey = r.ListKey AND m.Code = r.Code AND m.CommunityId IS NULL
);

SELECT ListKey, COUNT(*) AS Cnt
FROM MasterDataItems
WHERE ListKey LIKE 'Maintenance_Payment%' OR ListKey = 'Maintenance_Bill_Status'
GROUP BY ListKey;
