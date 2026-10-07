-- =====================================================================
-- 013 -- New-item requests carry the requester's estimated price for
-- one unit. The estimated cost of a request is Quantity x this.
-- Nullable: requests made before this change have no estimate.
--
-- Run once, with the app stopped. Safe to re-run.
-- =====================================================================
USE SchoolInventoryManagement;
GO

IF COL_LENGTH('dbo.NewItemRequests', 'EstimatedUnitPrice') IS NULL
    ALTER TABLE dbo.NewItemRequests ADD EstimatedUnitPrice DECIMAL(12,2) NULL;
GO

SELECT COL_LENGTH('dbo.NewItemRequests', 'EstimatedUnitPrice') AS EstimatedUnitPrice_Bytes; -- 5 = added
GO
