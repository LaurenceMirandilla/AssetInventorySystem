-- =====================================================================
-- 011 -- Assets can keep a warranty file: a photo of the warranty card or
-- receipt, or a PDF. Stores the file's URL; the file itself is saved in
-- wwwroot/uploads/warranty.
--
-- Run once, with the app stopped. Safe to re-run.
-- =====================================================================
USE SchoolInventoryManagement;
GO

IF COL_LENGTH('dbo.Assets', 'WarrantyFileURL') IS NULL
    ALTER TABLE dbo.Assets ADD WarrantyFileURL NVARCHAR(500) NULL;
GO

SELECT COL_LENGTH('dbo.Assets', 'WarrantyFileURL') AS WarrantyFileURL_Bytes; -- 1000 = added
GO
