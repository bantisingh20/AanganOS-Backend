SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

IF OBJECT_ID('dbo.MaintenanceExpenses', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.MaintenanceExpenses (
        Id            UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_MaintenanceExpenses PRIMARY KEY,
        CommunityId   UNIQUEIDENTIFIER NOT NULL,
        Category      NVARCHAR(60)     NOT NULL,
        Title         NVARCHAR(200)    NOT NULL,
        Amount        DECIMAL(18,2)    NOT NULL,
        SpentOnUtc    DATETIME2        NOT NULL,
        Vendor        NVARCHAR(200)    NOT NULL CONSTRAINT DF_MaintenanceExpenses_Vendor DEFAULT(N''),
        Notes         NVARCHAR(1000)   NOT NULL CONSTRAINT DF_MaintenanceExpenses_Notes DEFAULT(N''),
        IsDeleted     BIT              NOT NULL CONSTRAINT DF_MaintenanceExpenses_IsDeleted DEFAULT(0),
        DeletedAtUtc  DATETIME2        NULL,
        RowVersion    ROWVERSION       NOT NULL,
        CreatedAtUtc  DATETIME2        NOT NULL,
        CreatedBy     NVARCHAR(200)    NULL,
        UpdatedAtUtc  DATETIME2        NULL,
        UpdatedBy     NVARCHAR(200)    NULL
    );

    CREATE INDEX IX_MaintenanceExpense_CommunityId_SpentOnUtc
        ON dbo.MaintenanceExpenses (CommunityId, SpentOnUtc);
END

SELECT OBJECT_ID('dbo.MaintenanceExpenses', 'U') AS TableObjectId;
