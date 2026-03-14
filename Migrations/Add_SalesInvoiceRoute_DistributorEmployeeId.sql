-- إضافة عمود الموزع لجدول خط السير
-- نفّذ في قاعدة بيانات التطبيق (مثلاً ERP)

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SalesInvoiceRoutes') AND name = 'DistributorEmployeeId')
BEGIN
    ALTER TABLE [dbo].[SalesInvoiceRoutes] ADD [DistributorEmployeeId] int NULL;
END
GO
