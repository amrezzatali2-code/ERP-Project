-- ============================================================
-- إضافة عمود TaxAmount لجدول PurchaseRequests
-- قاعدة البيانات: ERP (حسب appsettings.json)
-- نفّذ هذا السكربت في SQL Server Management Studio بعد اختيار قاعدة ERP
-- ============================================================

USE ERP;
GO

-- 1) إضافة العمود إن لم يكن موجوداً
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.PurchaseRequests') AND name = 'TaxAmount'
)
BEGIN
    ALTER TABLE dbo.PurchaseRequests
    ADD TaxAmount decimal(18,2) NOT NULL DEFAULT 0;
    PRINT 'تمت إضافة عمود TaxAmount بنجاح.';
END
ELSE
    PRINT 'عمود TaxAmount موجود مسبقاً.';
GO

-- 2) تسجيل الـ migration في سجل EF (حتى لا يعيد تطبيقه لاحقاً)
IF NOT EXISTS (SELECT 1 FROM dbo.[__EFMigrationsHistory] WHERE [MigrationId] = N'20260308000000_AddTaxAmountToPurchaseRequest')
BEGIN
    INSERT INTO dbo.[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260308000000_AddTaxAmountToPurchaseRequest', N'9.0.10');
    PRINT 'تم تسجيل الـ migration في السجل.';
END
ELSE
    PRINT 'الـ migration مسجّل مسبقاً.';
GO
