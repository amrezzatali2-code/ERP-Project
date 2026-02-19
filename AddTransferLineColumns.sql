-- إضافة الأعمدة المفقودة لجدول StockTransferLines
-- نفّذ هذا السكربت في SQL Server Management Studio أو أي أداة اتصال بقاعدة البيانات

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('StockTransferLines') AND name = 'PriceRetail')
BEGIN
    ALTER TABLE StockTransferLines ADD PriceRetail decimal(18,2) NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('StockTransferLines') AND name = 'WeightedDiscountPct')
BEGIN
    ALTER TABLE StockTransferLines ADD WeightedDiscountPct decimal(5,2) NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('StockTransferLines') AND name = 'DiscountPct')
BEGIN
    ALTER TABLE StockTransferLines ADD DiscountPct decimal(5,2) NULL;
END

PRINT 'تم إضافة الأعمدة بنجاح.';
