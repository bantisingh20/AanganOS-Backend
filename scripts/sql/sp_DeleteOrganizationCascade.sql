-- =====================================================================================================
-- Stored Procedure: dbo.sp_DeleteOrganizationCascade
-- Purpose:
--   Completely and safely delete an Organization, all its Communities, and ALL associated child data
--   across the entire AanganOS database in proper foreign key order within an atomic transaction.
-- Parameters:
--   @OrganizationId: UNIQUEIDENTIFIER - The ID of the organization to purge
-- Usage:
--   EXEC dbo.sp_DeleteOrganizationCascade @OrganizationId = 'YOUR-ORGANIZATION-GUID';
-- =====================================================================================================

CREATE OR ALTER PROCEDURE dbo.sp_DeleteOrganizationCascade
    @OrganizationId UNIQUEIDENTIFIER
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    -- Validate existence
    IF NOT EXISTS (SELECT 1 FROM dbo.Organizations WHERE Id = @OrganizationId)
    BEGIN
        DECLARE @NotFoundMsg NVARCHAR(200) = 'Organization with ID ' + CAST(@OrganizationId AS NVARCHAR(36)) + ' does not exist.';
        RAISERROR(@NotFoundMsg, 16, 1);
        RETURN;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        -- 1. Gather all related IDs in temporary tables
        DECLARE @CommunityIds TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO @CommunityIds (Id)
        SELECT Id FROM dbo.Communities WHERE OrganizationId = @OrganizationId;

        DECLARE @UnitIds TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO @UnitIds (Id)
        SELECT Id FROM dbo.Units WHERE CommunityId IN (SELECT Id FROM @CommunityIds);

        DECLARE @ResidentIds TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO @ResidentIds (Id)
        SELECT Id FROM dbo.Residents 
        WHERE OrganizationId = @OrganizationId 
           OR CommunityId IN (SELECT Id FROM @CommunityIds);

        DECLARE @UserIds TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO @UserIds (Id)
        SELECT Id FROM dbo.Users 
        WHERE OrganizationId = @OrganizationId 
           OR CommunityId IN (SELECT Id FROM @CommunityIds)
        UNION
        SELECT UserId FROM dbo.Residents 
        WHERE UserId IS NOT NULL 
          AND (OrganizationId = @OrganizationId OR CommunityId IN (SELECT Id FROM @CommunityIds));

        DECLARE @TicketIds TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO @TicketIds (Id)
        SELECT Id FROM dbo.Tickets WHERE CommunityId IN (SELECT Id FROM @CommunityIds);

        DECLARE @AssetIds TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO @AssetIds (Id)
        SELECT Id FROM dbo.Assets WHERE CommunityId IN (SELECT Id FROM @CommunityIds);

        DECLARE @WorkOrderIds TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO @WorkOrderIds (Id)
        SELECT Id FROM dbo.WorkOrders WHERE CommunityId IN (SELECT Id FROM @CommunityIds);

        DECLARE @AmenityIds TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO @AmenityIds (Id)
        SELECT Id FROM dbo.Amenities WHERE CommunityId IN (SELECT Id FROM @CommunityIds);

        DECLARE @ParcelIds TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO @ParcelIds (Id)
        SELECT Id FROM dbo.Parcels WHERE CommunityId IN (SELECT Id FROM @CommunityIds);

        DECLARE @ParkingSlotIds TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO @ParkingSlotIds (Id)
        SELECT Id FROM dbo.ParkingSlots WHERE CommunityId IN (SELECT Id FROM @CommunityIds);

        DECLARE @ParkingZoneIds TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO @ParkingZoneIds (Id)
        SELECT Id FROM dbo.ParkingZones WHERE CommunityId IN (SELECT Id FROM @CommunityIds);

        DECLARE @ResidentEventIds TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO @ResidentEventIds (Id)
        SELECT Id FROM dbo.ResidentEvents WHERE CommunityId IN (SELECT Id FROM @CommunityIds);

        DECLARE @VehicleIds TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO @VehicleIds (Id)
        SELECT Id FROM dbo.Vehicles WHERE CommunityId IN (SELECT Id FROM @CommunityIds);

        DECLARE @VisitorIds TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO @VisitorIds (Id)
        SELECT Id FROM dbo.Visitors WHERE CommunityId IN (SELECT Id FROM @CommunityIds);

        DECLARE @MaintenanceBillIds TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO @MaintenanceBillIds (Id)
        SELECT Id FROM dbo.MaintenanceBills WHERE CommunityId IN (SELECT Id FROM @CommunityIds);

        DECLARE @InspectionChecklistIds TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO @InspectionChecklistIds (Id)
        SELECT Id FROM dbo.InspectionChecklists 
        WHERE CommunityId IN (SELECT Id FROM @CommunityIds)
           OR WorkOrderId IN (SELECT Id FROM @WorkOrderIds)
           OR AssetId IN (SELECT Id FROM @AssetIds);

        DECLARE @CustomRoleIds TABLE (Id UNIQUEIDENTIFIER PRIMARY KEY);
        INSERT INTO @CustomRoleIds (Id)
        SELECT Id FROM dbo.Roles WHERE CommunityId IN (SELECT Id FROM @CommunityIds);

        -----------------------------------------------------------------------------------------
        -- STEP 1: Deep Leaf tables (child references)
        -----------------------------------------------------------------------------------------
        -- Tickets children
        DELETE FROM dbo.TicketAttachments WHERE TicketId IN (SELECT Id FROM @TicketIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.TicketComments WHERE TicketId IN (SELECT Id FROM @TicketIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);

        -- WorkOrder & Asset children
        DELETE FROM dbo.InspectionChecklistItems WHERE InspectionChecklistId IN (SELECT Id FROM @InspectionChecklistIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.InspectionChecklists WHERE Id IN (SELECT Id FROM @InspectionChecklistIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.EquipmentHistoryEntries WHERE WorkOrderId IN (SELECT Id FROM @WorkOrderIds) OR AssetId IN (SELECT Id FROM @AssetIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.WorkOrderLabour WHERE WorkOrderId IN (SELECT Id FROM @WorkOrderIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.WorkOrderParts WHERE WorkOrderId IN (SELECT Id FROM @WorkOrderIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.AmcContracts WHERE AssetId IN (SELECT Id FROM @AssetIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.MeterReadings WHERE AssetId IN (SELECT Id FROM @AssetIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);

        -- Parcels children
        DELETE FROM dbo.CustodyTimelineEntries WHERE ParcelId IN (SELECT Id FROM @ParcelIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.ParcelHandovers WHERE ParcelId IN (SELECT Id FROM @ParcelIds);
        DELETE FROM dbo.ParcelAuthorizations WHERE ParcelId IN (SELECT Id FROM @ParcelIds);

        -- Amenities children
        DELETE FROM dbo.SlotBookingCounts WHERE AmenityId IN (SELECT Id FROM @AmenityIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.Bookings WHERE AmenityId IN (SELECT Id FROM @AmenityIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.BookingRules WHERE AmenityId IN (SELECT Id FROM @AmenityIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.BlackoutDates WHERE AmenityId IN (SELECT Id FROM @AmenityIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.FacilitySlots WHERE AmenityId IN (SELECT Id FROM @AmenityIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);

        -- Events children
        DELETE FROM dbo.ResidentEventRegistrations WHERE ResidentEventId IN (SELECT Id FROM @ResidentEventIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);

        -- Parking & Vehicles children
        DELETE FROM dbo.ParkingViolations WHERE VehicleId IN (SELECT Id FROM @VehicleIds) OR ParkingSlotId IN (SELECT Id FROM @ParkingSlotIds) OR UnitId IN (SELECT Id FROM @UnitIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.ParkingAssignments WHERE ParkingSlotId IN (SELECT Id FROM @ParkingSlotIds) OR VisitorId IN (SELECT Id FROM @VisitorIds) OR UnitId IN (SELECT Id FROM @UnitIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.EvReservations WHERE ParkingSlotId IN (SELECT Id FROM @ParkingSlotIds) OR UnitId IN (SELECT Id FROM @UnitIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);

        -- Residents children
        DELETE FROM dbo.CommunicationPreferences WHERE ResidentId IN (SELECT Id FROM @ResidentIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.EmergencyContacts WHERE ResidentId IN (SELECT Id FROM @ResidentIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.HouseholdMembers WHERE ResidentId IN (SELECT Id FROM @ResidentIds) OR UnitId IN (SELECT Id FROM @UnitIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.OwnershipHistories WHERE ResidentId IN (SELECT Id FROM @ResidentIds) OR UnitId IN (SELECT Id FROM @UnitIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.TenancyHistories WHERE ResidentId IN (SELECT Id FROM @ResidentIds) OR UnitId IN (SELECT Id FROM @UnitIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);

        -- Maintenance & Billing
        DELETE FROM dbo.MaintenancePayments WHERE MaintenanceBillId IN (SELECT Id FROM @MaintenanceBillIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.MaintenanceBills WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.MaintenanceExpenses WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.MaintenanceCharges WHERE UnitId IN (SELECT Id FROM @UnitIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.MaintenancePlans WHERE AssetId IN (SELECT Id FROM @AssetIds) OR CommunityId IN (SELECT Id FROM @CommunityIds);

        -----------------------------------------------------------------------------------------
        -- STEP 2: Mid-level tables
        -----------------------------------------------------------------------------------------
        DELETE FROM dbo.Tickets WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.WorkOrders WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.Assets WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.Parcels WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.CustodyLocations WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.Amenities WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.ResidentEvents WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.ParkingSlots WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.ParkingZones WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.StorageLocations WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.Pets WHERE CommunityId IN (SELECT Id FROM @CommunityIds) OR UnitId IN (SELECT Id FROM @UnitIds);
        DELETE FROM dbo.Vehicles WHERE CommunityId IN (SELECT Id FROM @CommunityIds) OR UnitId IN (SELECT Id FROM @UnitIds);
        DELETE FROM dbo.VisitPasses WHERE CommunityId IN (SELECT Id FROM @CommunityIds) OR UnitId IN (SELECT Id FROM @UnitIds);
        DELETE FROM dbo.Visitors WHERE CommunityId IN (SELECT Id FROM @CommunityIds) OR UnitId IN (SELECT Id FROM @UnitIds);
        DELETE FROM dbo.RecurringVisitors WHERE CommunityId IN (SELECT Id FROM @CommunityIds) OR UnitId IN (SELECT Id FROM @UnitIds);
        DELETE FROM dbo.WatchlistEntries WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.GuardShifts WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.Incidents WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.SosAlerts WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.AccessIntegrationEvents WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.EntryExitEvents WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.ResidentJoinRequests WHERE CommunityId IN (SELECT Id FROM @CommunityIds) OR UnitId IN (SELECT Id FROM @UnitIds);
        DELETE FROM dbo.Residents WHERE OrganizationId = @OrganizationId OR CommunityId IN (SELECT Id FROM @CommunityIds);

        -----------------------------------------------------------------------------------------
        -- STEP 3: Units and Hierarchy Nodes
        -----------------------------------------------------------------------------------------
        DELETE FROM dbo.Units WHERE CommunityId IN (SELECT Id FROM @CommunityIds);

        -- Break self-referencing hierarchy cycles before deleting nodes
        UPDATE dbo.HierarchyNodes
        SET ParentId = NULL
        WHERE CommunityId IN (SELECT Id FROM @CommunityIds);

        DELETE FROM dbo.HierarchyNodes WHERE CommunityId IN (SELECT Id FROM @CommunityIds);

        -----------------------------------------------------------------------------------------
        -- STEP 4: Organization & Community level operational / config / log tables
        -----------------------------------------------------------------------------------------
        DELETE FROM dbo.Announcements WHERE OrganizationId = @OrganizationId OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.NotificationOutbox WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.NotificationTemplates WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.MasterDataItems WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.Configurations WHERE OrganizationId = @OrganizationId OR CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.StoredFiles WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.AuditLogs WHERE CommunityId IN (SELECT Id FROM @CommunityIds);
        DELETE FROM dbo.ErrorLogs WHERE OrganizationId = @OrganizationId OR CommunityId IN (SELECT Id FROM @CommunityIds) OR UserId IN (SELECT Id FROM @UserIds);
        DELETE FROM dbo.SampleTenantRecords WHERE CommunityId IN (SELECT Id FROM @CommunityIds);

        -----------------------------------------------------------------------------------------
        -- STEP 5: Users, Sessions, Devices, Tokens, and Custom Community Roles
        -----------------------------------------------------------------------------------------
        DELETE FROM dbo.LoginSessions WHERE UserId IN (SELECT Id FROM @UserIds);
        DELETE FROM dbo.OtpTokens WHERE UserId IN (SELECT Id FROM @UserIds);
        DELETE FROM dbo.Devices WHERE UserId IN (SELECT Id FROM @UserIds);
        DELETE FROM dbo.Users WHERE OrganizationId = @OrganizationId OR CommunityId IN (SELECT Id FROM @CommunityIds) OR Id IN (SELECT Id FROM @UserIds);

        -- Custom community-scoped roles
        DELETE FROM dbo.RolePermissions WHERE RoleId IN (SELECT Id FROM @CustomRoleIds);
        DELETE FROM dbo.Roles WHERE Id IN (SELECT Id FROM @CustomRoleIds);

        -----------------------------------------------------------------------------------------
        -- STEP 6: Subscriptions, Communities, and Organization
        -----------------------------------------------------------------------------------------
        DELETE FROM dbo.SubscriptionRenewalRequests WHERE OrganizationId = @OrganizationId;
        DELETE FROM dbo.OrgSubscriptions WHERE OrganizationId = @OrganizationId;
        DELETE FROM dbo.Communities WHERE OrganizationId = @OrganizationId;
        DELETE FROM dbo.Organizations WHERE Id = @OrganizationId;

        COMMIT TRANSACTION;

        SELECT 
            @OrganizationId AS DeletedOrganizationId,
            (SELECT COUNT(*) FROM @CommunityIds) AS DeletedCommunitiesCount,
            (SELECT COUNT(*) FROM @UnitIds) AS DeletedUnitsCount,
            (SELECT COUNT(*) FROM @ResidentIds) AS DeletedResidentsCount,
            (SELECT COUNT(*) FROM @UserIds) AS DeletedUsersCount,
            'Organization and all associated data deleted successfully.' AS StatusMessage;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        DECLARE @ErrMsg NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @ErrSeverity INT = ERROR_SEVERITY();
        DECLARE @ErrState INT = ERROR_STATE();
        RAISERROR(@ErrMsg, @ErrSeverity, @ErrState);
    END CATCH
END;
GO
