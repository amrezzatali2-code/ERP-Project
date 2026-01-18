-- =========================================================
-- سكريبت مسح جميع العمليات والقيود (للتطوير فقط)
-- ⚠️ تحذير: هذا السكريبت يمسح جميع البيانات المرتبطة بالعمليات
-- استخدمه فقط في مرحلة التطوير
-- 
-- الترتيب الصحيح للحذف (من الأبناء للأباء):
-- 1. الجداول المرتبطة بالقيود (StockFifoMap, LedgerEntries)
-- 2. الجداول المرتبطة بالسطور (StockLedger)
-- 3. سطور الفواتير (SalesInvoiceLines, PILines)
-- 4. الفواتير (SalesInvoices, PurchaseInvoices)
-- 5. المرتجعات والأوامر والتحويلات
-- =========================================================

BEGIN TRANSACTION;

-- 1) مسح StockFifoMap (يرتبط بـ StockLedger)
DELETE FROM StockFifoMap;
PRINT 'تم مسح StockFifoMap';

-- 2) مسح LedgerEntries (قيود دفتر الأستاذ)
DELETE FROM LedgerEntries;
PRINT 'تم مسح LedgerEntries';

-- 3) مسح StockBatches (أرصدة المخزون - يجب مسحه قبل StockLedger)
DELETE FROM StockBatches;
PRINT 'تم مسح StockBatches';

-- 4) مسح StockLedger (دفتر الحركة المخزنية)
DELETE FROM StockLedger;
PRINT 'تم مسح StockLedger';

-- 5) مسح Batches (بيانات التشغيلات - يجب مسحه بعد StockLedger)
DELETE FROM Batches;
PRINT 'تم مسح Batches';

-- 6) مسح سطور الفواتير (Cascade عادة، لكن نمسحها يدوياً للتأكد)
DELETE FROM SalesInvoiceLines;
PRINT 'تم مسح SalesInvoiceLines';

DELETE FROM PILines;
PRINT 'تم مسح PILines';

-- 7) مسح الفواتير (الهيدر)
DELETE FROM SalesInvoices;
PRINT 'تم مسح SalesInvoices';

DELETE FROM PurchaseInvoices;
PRINT 'تم مسح PurchaseInvoices';

-- 8) مسح المرتجعات (إن وجدت)
DELETE FROM SalesReturnLines;
DELETE FROM SalesReturns;
PRINT 'تم مسح SalesReturns';

DELETE FROM PurchaseReturnLines;
DELETE FROM PurchaseReturns;
PRINT 'تم مسح PurchaseReturns';

-- 9) مسح الأوامر (إن وجدت)
DELETE FROM SOLines;
DELETE FROM SalesOrders;
PRINT 'تم مسح SalesOrders';

DELETE FROM PRLines;
DELETE FROM PurchaseRequests;
PRINT 'تم مسح PurchaseRequests';

-- 10) مسح التحويلات المخزنية (إن وجدت)
DELETE FROM StockTransferLines;
DELETE FROM StockTransfers;
PRINT 'تم مسح StockTransfers';

-- 11) مسح التسويات المخزنية (إن وجدت)
DELETE FROM StockAdjustmentLines;
DELETE FROM StockAdjustment;
PRINT 'تم مسح StockAdjustments';

-- 12) إعادة تعيين أرصدة العملاء (مهم بعد مسح القيود)
UPDATE Customers SET CurrentBalance = 0;
PRINT 'تم إعادة تعيين أرصدة العملاء';

PRINT '========================================';
PRINT 'تم مسح جميع العمليات بنجاح';
PRINT '========================================';

-- ✅ Commit التغييرات
COMMIT TRANSACTION;
PRINT 'تم حفظ التغييرات';

-- ⚠️ ملاحظة: إذا حدث خطأ، استخدم ROLLBACK TRANSACTION; لإلغاء التغييرات
