-- ============================================================
-- مسح: دفتر الحركة المخزنية (StockLedger) + دفتر الأستاذ (LedgerEntries)
--       + الأصناف (Products) + العملاء (Customers) + سجل نشاط المستخدمين (UserActivityLogs)
-- SQL Server — نسخة احتياطية كاملة قبل التنفيذ
-- ⚠️ يحذف العمليات المرتبطة بالمخزن والمحاسبة والمستندات ثم الأطراف والأصناف
-- لا يمس: Accounts, Warehouses, Categories, Users (إلا تصفير CustomerId), ...
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

-- ========= 1) دفتر الأستاذ المحاسبي =========
DELETE FROM LedgerEntries;

-- ========= 2) مخزون: FIFO ثم الحركة ثم الرصيد السريع =========
DELETE FROM StockFifoMap;
DELETE FROM StockLedger;
DELETE FROM [Stock_Batches];

-- ========= 3) تحويلات وتسويات وخصومات ترتبط بتشغيلة (قبل حذف Batches) =========
DELETE FROM StockTransferLines;
DELETE FROM StockTransfers;

DELETE FROM StockAdjustmentLines;
DELETE FROM StockAdjustments;

DELETE FROM ProductDiscountOverrides;

-- ========= 4) التشغيلات (جدول Batches) =========
DELETE FROM Batches;

-- ========= 5) مشتريات خارجية + فاكس + مطابقة مورد =========
DELETE FROM PurchasingOrderAmendments;
DELETE FROM PurchasingOrders;

DELETE FROM VendorFaxLines;
DELETE FROM VendorFaxUploads;
DELETE FROM VendorProductMappings;

-- ========= 6) خط سير الفاتورة =========
DELETE FROM SalesInvoiceRouteFridgeLines;
DELETE FROM SalesInvoiceRoutes;

-- ========= 7) إيصالات وعمليات =========
DELETE FROM CashReceipts;
DELETE FROM CashPayments;

DELETE FROM SalesInvoiceLines;
DELETE FROM SalesInvoices;

DELETE FROM SalesReturnLines;
DELETE FROM SalesReturns;

DELETE FROM PILines;
DELETE FROM PurchaseInvoices;

DELETE FROM PurchaseReturnLines;
DELETE FROM PurchaseReturns;

DELETE FROM SOLines;
DELETE FROM SalesOrders;

DELETE FROM PRLines;
DELETE FROM PurchaseRequests;

DELETE FROM DebitNotes;
DELETE FROM CreditNotes;

-- ========= 8) مستخدمون مرتبطون بطرف =========
UPDATE Users SET CustomerId = NULL WHERE CustomerId IS NOT NULL;

-- ========= 9) أصناف: سجل أسعار ثم الأصناف =========
DELETE FROM ProductPriceHistory;
DELETE FROM Products;

-- ========= 10) عملاء =========
DELETE FROM Customers;

-- ========= 11) سجل الحركات / سجل نشاط المستخدمين (شاشة الإعدادات — UserActivityLogs) =========
DELETE FROM UserActivityLogs;

COMMIT TRANSACTION;

PRINT N'تم: LedgerEntries + StockLedger + مخزون/تشغيلات + مستندات + Products + Customers + UserActivityLogs.';
