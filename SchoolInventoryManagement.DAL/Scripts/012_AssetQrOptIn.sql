-- =====================================================================
-- 012 -- QR codes are no longer automatic: an asset has one only when
-- QRCodeData holds the asset code it points to, and scanning only opens
-- assets that have one.
--
-- Gives every asset registered before this change its QR, so labels
-- already printed keep scanning. Skip it if none were printed.
-- Safe to re-run.
-- =====================================================================
USE SchoolInventoryManagement;
GO

UPDATE dbo.Assets
SET QRCodeData = AssetCode
WHERE QRCodeData IS NULL OR QRCodeData = '';
GO
