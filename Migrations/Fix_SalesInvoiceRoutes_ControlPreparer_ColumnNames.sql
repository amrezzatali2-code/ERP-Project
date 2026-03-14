-- إصلاح أسماء أعمدة الكونترول والمحضر (تصحيح خطأ إملائي: Employeeld -> EmployeeId)
-- نفّذ هذا السكربت في SQL Server Management Studio بعد اختيار قاعدة بيانات التطبيق (مثلاً ERP).
-- إذا كانت القاعدة باسم آخر فغيّر [ERP] أو احذف سطر USE واختر القاعدة من القائمة.

USE [ERP];
GO

-- إصلاح عمود الكونترول
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SalesInvoiceRoutes') AND name = 'ControlEmployeeld')
BEGIN
    EXEC sp_rename 'dbo.SalesInvoiceRoutes.ControlEmployeeld', 'ControlEmployeeId', 'COLUMN';
END
GO

-- إصلاح عمود المحضر
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SalesInvoiceRoutes') AND name = 'PreparerEmployeeld')
BEGIN
    EXEC sp_rename 'dbo.SalesInvoiceRoutes.PreparerEmployeeld', 'PreparerEmployeeId', 'COLUMN';
END
GO

-- إذا كانت الأعمدة الصحيحة غير موجودة أصلاً، أضفها (اختياري)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SalesInvoiceRoutes') AND name = 'ControlEmployeeId')
    ALTER TABLE [dbo].[SalesInvoiceRoutes] ADD [ControlEmployeeId] int NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SalesInvoiceRoutes') AND name = 'PreparerEmployeeId')
    ALTER TABLE [dbo].[SalesInvoiceRoutes] ADD [PreparerEmployeeId] int NULL;
GO
