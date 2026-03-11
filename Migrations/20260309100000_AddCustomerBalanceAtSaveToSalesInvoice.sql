-- =========================================================
-- Migration: AddCustomerBalanceAtSaveToSalesInvoice
-- الحساب السابق للعميل عند الحفظ (يثبت في الفاتورة ولا يتغير)
-- تشغيل هذا السكربت يطبق التعديل على قاعدة البيانات فقط.
-- =========================================================

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.SalesInvoices')
    AND name = 'CustomerBalanceAtSave'
)
BEGIN
    ALTER TABLE [dbo].[SalesInvoices]
    ADD [CustomerBalanceAtSave] decimal(18,2) NOT NULL DEFAULT 0;
END
GO
