-- إضافة عمود IsConverted لجدول SalesOrders (إن لم يكن موجوداً)
-- تشغيل هذا الملف يدوياً إذا لم يُطبَّق ترحيل EF: 20260310100000_AddIsConvertedToSalesOrders

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.SalesOrders') AND name = N'IsConverted'
)
BEGIN
    ALTER TABLE dbo.SalesOrders
    ADD IsConverted BIT NOT NULL CONSTRAINT DF_SalesOrders_IsConverted DEFAULT 0;
END
GO
