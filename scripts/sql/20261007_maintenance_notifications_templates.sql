SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @rows TABLE (
    Id UNIQUEIDENTIFIER,
    [Key] NVARCHAR(200),
    TitleTemplate NVARCHAR(400),
    BodyTemplate NVARCHAR(2000)
);

INSERT INTO @rows (Id, [Key], TitleTemplate, BodyTemplate) VALUES
 ('b1f1a2c3-0001-4a10-9b01-aa0000000001',
  N'maintenance.payment.submitted',
  N'Maintenance payment awaiting approval',
  N'A resident submitted a payment of {amount} for unit {unitNumber} ({period}). Please review and approve or reject it.'),
 ('b1f1a2c3-0002-4a10-9b01-aa0000000002',
  N'maintenance.payment.approved',
  N'Payment approved',
  N'Your maintenance payment of {amount} for {period} has been approved. Thank you.'),
 ('b1f1a2c3-0003-4a10-9b01-aa0000000003',
  N'maintenance.payment.rejected',
  N'Payment rejected',
  N'Your maintenance payment of {amount} for {period} could not be approved. {reason}'),
 ('b1f1a2c3-0004-4a10-9b01-aa0000000004',
  N'maintenance.bill.generated',
  N'New maintenance bill',
  N'A maintenance bill of {amount} for {period} has been raised for your unit. Please pay before the due date.');

INSERT INTO [NotificationTemplates]
    ([Id], [Key], [Scope], [CommunityId], [Category], [CategoryKind],
     [TitleTemplate], [BodyTemplate], [Channels], [IsActive], [IsDeleted],
     [CreatedAtUtc], [CreatedBy])
SELECT r.Id, r.[Key], 0, NULL, N'invoice', 0,
       r.TitleTemplate, r.BodyTemplate, 5, 1, 0, '2026-01-01T00:00:00', N'seed'
FROM @rows r
WHERE NOT EXISTS (
    SELECT 1 FROM [NotificationTemplates] t
    WHERE t.[Key] = r.[Key] AND t.[CommunityId] IS NULL
);

SELECT [Key], [Category], [Channels] FROM [NotificationTemplates]
WHERE [Key] LIKE 'maintenance.%';
