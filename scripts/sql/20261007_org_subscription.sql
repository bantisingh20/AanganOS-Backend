SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.OrgSubscriptions', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OrgSubscriptions
    (
        Id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_OrgSubscriptions PRIMARY KEY,
        OrganizationId  UNIQUEIDENTIFIER NOT NULL,
        PlanMonths      INT NOT NULL CONSTRAINT DF_OrgSubscriptions_PlanMonths DEFAULT(12),
        StartsAtUtc     DATETIME2 NOT NULL,
        ExpiresAtUtc    DATETIME2 NOT NULL,
        Status          NVARCHAR(20) NOT NULL CONSTRAINT DF_OrgSubscriptions_Status DEFAULT('active'),
        IsDeleted       BIT NOT NULL CONSTRAINT DF_OrgSubscriptions_IsDeleted DEFAULT(0),
        DeletedAtUtc    DATETIME2 NULL,
        RowVersion      ROWVERSION NOT NULL,
        CreatedAtUtc    DATETIME2 NOT NULL,
        CreatedBy       NVARCHAR(MAX) NULL,
        UpdatedAtUtc    DATETIME2 NULL,
        UpdatedBy       NVARCHAR(MAX) NULL
    );
END;
GO

IF OBJECT_ID('dbo.SubscriptionRenewalRequests', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SubscriptionRenewalRequests
    (
        Id                  UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_SubscriptionRenewalRequests PRIMARY KEY,
        OrganizationId      UNIQUEIDENTIFIER NOT NULL,
        RequestedPlanMonths INT NOT NULL CONSTRAINT DF_SubRenewal_PlanMonths DEFAULT(12),
        Status              NVARCHAR(20) NOT NULL CONSTRAINT DF_SubRenewal_Status DEFAULT('pending'),
        RequestedBy         UNIQUEIDENTIFIER NOT NULL,
        RequestedAtUtc      DATETIME2 NOT NULL,
        ResolvedBy          UNIQUEIDENTIFIER NULL,
        ResolvedAtUtc       DATETIME2 NULL,
        ResultExpiresAtUtc  DATETIME2 NULL,
        IsDeleted           BIT NOT NULL CONSTRAINT DF_SubRenewal_IsDeleted DEFAULT(0),
        DeletedAtUtc        DATETIME2 NULL,
        RowVersion          ROWVERSION NOT NULL,
        CreatedAtUtc        DATETIME2 NOT NULL,
        CreatedBy           NVARCHAR(MAX) NULL,
        UpdatedAtUtc        DATETIME2 NULL,
        UpdatedBy           NVARCHAR(MAX) NULL
    );
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_OrgSubscription_OrganizationId_Active' AND object_id = OBJECT_ID('dbo.OrgSubscriptions'))
BEGIN
    CREATE UNIQUE INDEX UX_OrgSubscription_OrganizationId_Active
        ON dbo.OrgSubscriptions (OrganizationId)
        WHERE IsDeleted = 0;
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_SubRenewal_OrganizationId_Pending' AND object_id = OBJECT_ID('dbo.SubscriptionRenewalRequests'))
BEGIN
    CREATE UNIQUE INDEX UX_SubRenewal_OrganizationId_Pending
        ON dbo.SubscriptionRenewalRequests (OrganizationId)
        WHERE IsDeleted = 0 AND Status = 'pending';
END;
GO
