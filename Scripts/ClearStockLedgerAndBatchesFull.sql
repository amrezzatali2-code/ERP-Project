-- ============================================================
-- مسح كامل دفتر الحركة المخزنية + FIFO + Stock_Batches + Batches (التشغيلات)
-- ⚠️ يحذف كل حركات المخزن وليس «Opening» فقط — للتطوير أو بعد نسخة احتياطية
-- يجب إزالة أي صف يشير إلى Batch قبل حذف Batches (تحويلات/تسويات/خصومات يدوية)
-- لا يمس LedgerEntries المحاسبي ولا فواتير المبيعات/المشتريات (قد تبقى بيانات يتيمة)
-- لمسح متكامل مع المستندات استخدم Scripts/ClearBulkImportData.sql أو ClearAllTransactions.sql
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

DELETE FROM StockFifoMap;
DELETE FROM StockLedger;
DELETE FROM [Stock_Batches];

DELETE FROM StockTransferLines;
DELETE FROM StockTransfers;

DELETE FROM StockAdjustmentLines;
DELETE FROM StockAdjustments;

DELETE FROM ProductDiscountOverrides;

DELETE FROM Batches;

COMMIT TRANSACTION;

PRINT N'تم — مسح مخزني كامل (StockLedger + التشغيلات + ما يرتبط بها من تحويل/تسوية/خصم).';
