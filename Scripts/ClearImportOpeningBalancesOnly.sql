-- ============================================================
-- القسم (1) فقط — مسح أرصدة الافتتاح المستوردة
-- - أرصدة عملاء افتتاحية (LedgerEntries / Opening + CustomerId)
-- - رصيد خزينة افتتاحي (حساب كود 1101 عادةً)
-- - رصيد أول المدة المخزني (StockLedger / Opening) + FIFO مرتبط بها
-- SQL Server — نسخة احتياطية قبل التنفيذ
-- بعد التنفيذ: يُفضّل إعادة حساب أرصدة العملاء من التطبيق إن وُجدت
--
-- لماذا يبقى دفتر الحركة المخزنية ممتلئاً؟
-- هذا السكربت يحذف من StockLedger فقط الصفوف حيث SourceType = N'Opening'
-- (استيراد رصيد أول المدة). أي حركة أخرى تبقى: Sales, Purchase, TransferIn,
-- TransferOut, Adjustment, SalesReturn, PurchaseReturn, SyncToProductWarehouse، إلخ.
-- إن رأيت آلاف السجلات بعد التنفيذ فالأغلب أنها ليست Opening.
--
-- لماذا «قائمة التشغيلات» (جدول Batches) لا تصفر؟
-- السطر DELETE FROM [Stock_Batches] يمسح جدول الرصيد السريع Stock_Batches فقط.
-- أما تعريف التشغيلات نفسه فيكون في جدول Batches — وهذا السكربت لا يحذفه.
-- لمسح التشغيلات يجب حذف Batches بعد إزالة كل مراجع StockLedger (BatchId) وغيرها.
--
-- للتحقق من توزيع أنواع الحركة في قاعدتك:
--   SELECT SourceType, COUNT(*) AS Cnt FROM StockLedger GROUP BY SourceType ORDER BY Cnt DESC;
-- ============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRANSACTION;

-- (1.1) LedgerEntries — أرصدة عملاء افتتاحية
DELETE FROM LedgerEntries
WHERE SourceType = 1 /* Opening */ AND CustomerId IS NOT NULL;

-- (1.2) LedgerEntries — رصيد خزينة افتتاحي
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

-- (1.3) StockFifoMap — مرتبط بقيود Opening في StockLedger
DELETE FROM StockFifoMap
WHERE OutEntryId IN (SELECT EntryId FROM StockLedger WHERE SourceType = N'Opening')
   OR InEntryId  IN (SELECT EntryId FROM StockLedger WHERE SourceType = N'Opening');

-- (1.4) StockLedger — رصيد أول المدة للأصناف
DELETE FROM StockLedger WHERE SourceType = N'Opening';

-- (1.5) جدول الرصيد السريع لكل تشغيلة/مخزن — يُمسح كاملاً (ليس جدول Batches)
-- إن أردت الإبقاء على أرصدة تتوافق مع حركات غير Opening، علّق هذا السطر.
DELETE FROM [Stock_Batches];

COMMIT TRANSACTION;

PRINT N'تم — مسح أرصدة الافتتاح فقط.';
