-- ============================================================
-- مسح بيانات الاستيراد الجماعي (أصناف / أرصدة مخزون / عملاء / أرصدة عملاء / خزينة)
-- يطابق منطق Controllers/ProductsController.cs و VendorProductMappingsController
-- قاعدة البيانات: SQL Server
-- ⚠️ نفّذ على نسخة احتياطية أو بيئة تطوير فقط
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

/*
|-----------------------------------------------------------------------------|
|  القسم (1) — مسح أرصدة الافتتاح فقط (أخف ضرراً)                              |
|  - أرصدة عملاء افتتاحية: LedgerEntries حيث SourceType = Opening (1)        |
|  - رصيد خزينة افتتاحي: نفس الجدول + حساب الخزينة (كود 1101 عادةً)            |
|  - رصيد أول المدة مخزني: StockLedger حيث SourceType = N'Opening'             |
|  بعد أرصدة العملاء الافتتاحية شغّل من التطبيق إعادة حساب أرصدة العملاء      |
|  (أو حدّث CurrentBalance يدوياً إن لزم).                                      |
|-----------------------------------------------------------------------------|
*/
-- فك تعليق القسم (1) فقط إذا أردت هذا المستوى من المسح:
/*
BEGIN TRANSACTION;

-- (1.1) LedgerEntries — أرصدة عملاء افتتاحية (استيراد أرصدة العملاء)
DELETE FROM LedgerEntries
WHERE SourceType = 1 /* Opening */ AND CustomerId IS NOT NULL;

-- (1.2) LedgerEntries — رصيد خزينة افتتاحي (استيراد رصيد الخزينة من Excel)
DECLARE @TreasuryAccountId INT =
(
    SELECT TOP (1) AccountId
    FROM Accounts
    WHERE AccountCode = N'1101' OR AccountCode LIKE N'1101%'
    ORDER BY AccountCode
);
IF @TreasuryAccountId IS NOT NULL
    DELETE FROM LedgerEntries
    WHERE SourceType = 1 AND CustomerId IS NULL AND AccountId = @TreasuryAccountId;

-- (1.3) StockFifoMap — أي ربط FIFO يشير لقيود Opening قبل حذفها
DELETE FROM StockFifoMap
WHERE OutEntryId IN (SELECT EntryId FROM StockLedger WHERE SourceType = N'Opening')
   OR InEntryId  IN (SELECT EntryId FROM StockLedger WHERE SourceType = N'Opening');

-- (1.4) StockLedger — رصيد أول المدة للأصناف فقط
DELETE FROM StockLedger WHERE SourceType = N'Opening';

-- تصفير أرصدة سريعة للمخزون (اختياري — أو استخدم تقرير مزامنة من البرنامج)
DELETE FROM [Stock_Batches];

COMMIT TRANSACTION;
PRINT N'تم تنفيذ القسم (1) — أرصدة افتتاح فقط.';
RETURN;
*/


/*
|-----------------------------------------------------------------------------|
|  القسم (2) — مسح كامل كما «استيراد الأصناف» + «استيراد العملاء»              |
|  يحذف العمليات ثم العملاء ثم الأصناف. خطير على بيانات الإنتاج.               |
|  الترتيب: أبناء قبل آباء، مع مراعاة المفاتيح الأجنبية.                       |
|-----------------------------------------------------------------------------|
*/
BEGIN TRANSACTION;

-- ========= LedgerEntries (كل القيود بما فيها الافتتاح والعمليات) =========
DELETE FROM LedgerEntries;

-- ========= مخزون: FIFO ثم دفتر الحركة ثم الدفعات السريعة =========
DELETE FROM StockFifoMap;
DELETE FROM StockLedger;
DELETE FROM [Stock_Batches];

-- ========= تسويات وتحويلات وخصومات (قبل Batches — FK على Batch) =========
DELETE FROM StockTransferLines;
DELETE FROM StockTransfers;

DELETE FROM StockAdjustmentLines;
DELETE FROM StockAdjustments;

DELETE FROM ProductDiscountOverrides;

DELETE FROM Batches;

-- ========= مشتريات: أوامر شراء خارجية (إن وُجدت) =========
DELETE FROM PurchasingOrderAmendments;
DELETE FROM PurchasingOrders;

-- ========= فاكس المورد + مطابقة أصناف العميل (استيراد مطابقة المورد) =========
DELETE FROM VendorFaxLines;
DELETE FROM VendorFaxUploads;
DELETE FROM VendorProductMappings;

-- ========= خط سير الفاتورة (قبل حذف فواتير المبيعات) =========
DELETE FROM SalesInvoiceRouteFridgeLines;
DELETE FROM SalesInvoiceRoutes;

-- ========= سطور ثم رؤوس المستندات (نفس ترتيب استيراد العملاء في الكود) =========
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

-- ========= مستخدمون مرتبطون بعميل (استيراد العملاء يصفّرها) =========
UPDATE Users SET CustomerId = NULL WHERE CustomerId IS NOT NULL;

-- (تشغيلات Batches حُذفت أعلاه — لا حاجة لتصفير CustomerId عليها)

-- ========= أصناف: سجل أسعار ثم الأصناف (ProductDiscountOverrides حُذف أعلاه) =========
DELETE FROM ProductPriceHistory;
DELETE FROM Products;

-- ========= عملاء (بعد حذف كل ما يشير لهم) =========
DELETE FROM Customers;

-- ========= سجل نشاط المستخدمين (سجل الحركات — UserActivityLogs) =========
DELETE FROM UserActivityLogs;

-- ========= إعادة أرصدة العملاء في الجدول (بعد مسح القيود أصبحت صفراً منطقياً) =========
-- الجدول فارغ؛ لا حاجة لتحديث إن حُذف كل العملاء.

COMMIT TRANSACTION;

PRINT N'تم تنفيذ القسم (2) — مسح كامل للعمليات والعملاء والأصناف وسجل النشاط.';
PRINT N'راجع إعدادات الحسابات والمخازن والفئات؛ لم تُمسَح (غير جزء من استيراد الأصناف/العملاء).';
