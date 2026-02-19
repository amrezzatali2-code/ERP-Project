-- =========================================================
-- تصفير قيود الخزينة والبنوك (حذف قيود LedgerEntries لحسابات النقدية)
-- يؤدي إلى ظهور رصيد الخزينة والبنوك = 0 في التقارير
--
-- تحذير شديد:
-- - سيُحذف كل تاريخ الحركات النقدية (إيصالات قبض، إذون دفع)
--   المرتبطة بحسابات الخزينة والبنوك (1101، 1102، أو أسماء تحتوي
--   خزينة/صندوق/بنك)
-- - لا يمكن استرجاع البيانات بعد التنفيذ
-- - استخدم فقط عند البدء من جديد أو إعادة ضبط كاملة للنظام
--
-- الحسابات المستهدفة: AccountType=Asset و
--   (AccountCode يبدأ بـ 1101 أو 1102 أو AccountName يحتوي خزينة/صندوق/بنك)
-- =========================================================

BEGIN TRANSACTION;

-- عرض القيود التي سيتم حذفها (للتحقق قبل الحذف)
SELECT COUNT(*) AS [عدد_القيود_التي_ستُحذف]
FROM LedgerEntries e
WHERE e.AccountId IN (
    SELECT a.AccountId FROM Accounts a
    WHERE a.AccountType = 1  -- Asset
      AND (a.AccountName LIKE N'%خزينة%'
           OR a.AccountName LIKE N'%صندوق%'
           OR a.AccountName LIKE N'%بنك%'
           OR a.AccountCode LIKE '1101%'
           OR a.AccountCode LIKE '1102%')
);

-- حذف قيود الخزينة والبنوك
DELETE FROM LedgerEntries
WHERE AccountId IN (
    SELECT a.AccountId FROM Accounts a
    WHERE a.AccountType = 1  -- Asset
      AND (a.AccountName LIKE N'%خزينة%'
           OR a.AccountName LIKE N'%صندوق%'
           OR a.AccountName LIKE N'%بنك%'
           OR a.AccountCode LIKE '1101%'
           OR a.AccountCode LIKE '1102%')
);

SELECT @@ROWCOUNT AS [عدد_القيود_المحذوفة];

-- للتأكيد وتطبيق التغييرات: نفّذ السطر التالي
COMMIT;

-- لإلغاء التغييرات: استخدم ROLLBACK بدلاً من COMMIT
