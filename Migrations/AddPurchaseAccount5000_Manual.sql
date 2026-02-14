-- ============================================================
-- إضافة حساب المشتريات (5000) المطلوب لترحيل مرتجعات الشراء
-- ============================================================
-- الخطأ: "لم يتم العثور على حساب بالكود (5000) داخل شجرة الحسابات"
-- الحل: تشغيل هذا السكربت لإضافة الحساب
-- ============================================================
-- 1) افتح SQL Server Management Studio
-- 2) اتصل بقاعدة البيانات ERP
-- 3) نفّذ هذا السكربت
-- ============================================================

USE ERP;
GO

-- التحقق من عدم وجود الحساب مسبقاً
IF NOT EXISTS (SELECT 1 FROM Accounts WHERE AccountCode = '5000')
BEGIN
    -- جلب رقم الحساب الأب (جذر المصروفات - كود 5)
    DECLARE @parentId INT = (SELECT AccountId FROM Accounts WHERE AccountCode = '5');

    IF @parentId IS NOT NULL
    BEGIN
        INSERT INTO Accounts (AccountCode, AccountName, AccountType, ParentAccountId, Level, IsLeaf, IsActive, Notes, CreatedAt)
        VALUES ('5000', N'المشتريات', 5, @parentId, 2, 0, 1, N'حساب المشتريات - يُستخدم في ترحيل مرتجعات الشراء', GETDATE());
        PRINT N'تم إضافة حساب المشتريات (5000) بنجاح.';
    END
    ELSE
    BEGIN
        PRINT N'تحذير: لم يتم العثور على الحساب الأب (كود 5). تأكد من وجود شجرة الحسابات.';
    END
END
ELSE
    PRINT N'حساب 5000 (المشتريات) موجود مسبقاً.';

GO
