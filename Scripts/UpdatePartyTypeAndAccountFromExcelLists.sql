-- ============================================================
-- تحديث نوع الطرف (PartyCategory) ومعرف الحساب المحاسبي (AccountId)
-- حسب قوائم من ملفي إكسل: الموظفين.xlsx ، المستثمرين.xlsx
--
-- الخطوات:
-- 1) افتح ملف الموظفين.xlsx وانسخ عمود أسماء الموظفين (أو العمود الذي يطابق اسم العميل في النظام).
-- 2) ضع الأسماء في القسم الأول أدناه (قيم @EmployeeNames).
-- 3) افتح ملف المستثمرين.xlsx وانسخ عمود أسماء المستثمرين.
-- 4) ضع الأسماء في القسم الثاني (قيم @InvestorNames).
-- 5) شغّل السكربت كاملاً.
--
-- ملاحظة: المطابقة تتم على CustomerName. لو الأسماء في الإكسل تختلف قليلاً (فراغات، تشكيل)
-- قد تحتاج لتعديل الأسماء يدوياً أو استخدام LIKE.
-- ============================================================

-- لو شغّلت من سطر أوامر: غيّر اسم القاعدة ثم أزل التعليق عن السطرين التاليين
-- USE [اسم_قاعدة_البيانات]
-- GO

-- أكواد الحسابات في شجرة الحسابات (غيّرها لو عندك أكواد مختلفة)
-- حساب الموظفين: 5201 = مرتبات وأجور  |  أو 1104 = ذمم أخرى/سلف وعهد
-- حساب المستثمرين: 3101 = رأس المال
DECLARE @EmployeeAccountCode NVARCHAR(20) = N'5201';
DECLARE @InvestorAccountCode  NVARCHAR(20) = N'3101';

-- ==================== 1) قائمة أسماء الموظفين (من ملف الموظفين.xlsx) ====================
DECLARE @EmployeeNames TABLE (Name NVARCHAR(200));

INSERT INTO @EmployeeNames (Name) VALUES
 (N'اسم الموظف 1')
,(N'اسم الموظف 2')
-- أضف بقية الأسماء من عمود الاسم في ملف الموظفين.xlsx، سطر لكل اسم، مثل:
-- ,(N'أحمد محمد')
-- ,(N'فاطمة علي')
;

-- ==================== 2) قائمة أسماء المستثمرين (من ملف المستثمرين.xlsx) ====================
DECLARE @InvestorNames TABLE (Name NVARCHAR(200));

INSERT INTO @InvestorNames (Name) VALUES
 (N'اسم المستثمر 1')
,(N'اسم المستثمر 2')
-- أضف بقية الأسماء من ملف المستثمرين.xlsx:
-- ,(N'خالد المستثمر')
;

-- ==================== 3) تحديث الموظفين: نوع الطرف = Employee ، الحساب = حساب الموظفين ====================
DECLARE @EmployeeAccountId INT = (SELECT TOP 1 AccountId FROM Accounts WHERE AccountCode = @EmployeeAccountCode);

UPDATE c
SET c.PartyCategory = N'Employee',
    c.AccountId    = @EmployeeAccountId
FROM Customers c
INNER JOIN @EmployeeNames e ON RTRIM(LTRIM(c.CustomerName)) = RTRIM(LTRIM(e.Name))
WHERE c.PartyCategory <> N'Employee' OR c.AccountId <> @EmployeeAccountId;

PRINT N'تم تحديث الموظفين: ' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + N' سجل.';

-- ==================== 4) تحديث المستثمرين: نوع الطرف = Investor ، الحساب = حساب المستثمرين ====================
DECLARE @InvestorAccountId INT = (SELECT TOP 1 AccountId FROM Accounts WHERE AccountCode = @InvestorAccountCode);

UPDATE c
SET c.PartyCategory = N'Investor',
    c.AccountId     = @InvestorAccountId
FROM Customers c
INNER JOIN @InvestorNames i ON RTRIM(LTRIM(c.CustomerName)) = RTRIM(LTRIM(i.Name))
WHERE c.PartyCategory <> N'Investor' OR c.AccountId <> @InvestorAccountId;

PRINT N'تم تحديث المستثمرين: ' + CAST(@@ROWCOUNT AS NVARCHAR(20)) + N' سجل.';

-- ==================== بديل: المطابقة برقم الهاتف ====================
-- لو عمود الاسم غير متطابق، استخدم أرقام الهواتف: أنشئ @EmployeePhones و @InvestorPhones
-- وضع الأرقام ثم JOIN على c.Phone1 أو c.Phone2.

-- ==================== (اختياري) عرض النتيجة ====================
-- SELECT PartyCategory, a.AccountCode, a.AccountName, COUNT(*) AS Cnt
-- FROM Customers c
-- LEFT JOIN Accounts a ON c.AccountId = a.AccountId
-- GROUP BY PartyCategory, a.AccountCode, a.AccountName
-- ORDER BY PartyCategory;
