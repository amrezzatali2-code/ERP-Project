-- ============================================================
-- حل خطأ: Invalid column name 'IsLocked'
-- ============================================================
-- 1) افتح SQL Server Management Studio
-- 2) اتصل بقاعدة البيانات ERP
-- 3) نفّذ هذا السكربت بالكامل
-- ============================================================

USE ERP;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('DebitNotes') AND name = 'IsLocked')
BEGIN
    ALTER TABLE DebitNotes ADD IsLocked bit NOT NULL DEFAULT 0;
    PRINT 'تم إضافة IsLocked إلى DebitNotes';
END
ELSE
    PRINT 'عمود IsLocked موجود مسبقاً في DebitNotes';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('CreditNotes') AND name = 'IsLocked')
BEGIN
    ALTER TABLE CreditNotes ADD IsLocked bit NOT NULL DEFAULT 0;
    PRINT 'تم إضافة IsLocked إلى CreditNotes';
END
ELSE
    PRINT 'عمود IsLocked موجود مسبقاً في CreditNotes';
GO

PRINT 'تم التنفيذ بنجاح. أعد تشغيل التطبيق وحدّث الصفحة.';
