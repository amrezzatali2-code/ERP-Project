-- إزالة الفهرس الفريد على ProductGroupId فقط من جدول ProductGroupPolicies
-- يسمح بنفس السياسة لنفس المجموعة في مخازن مختلفة
-- نفّذ هذا السكربت في SQL Server Management Studio أو Azure Data Studio

USE [ERP];
GO

IF EXISTS (SELECT 1 FROM sys.indexes 
           WHERE name = 'IX_ProductGroupPolicies_ProductGroupId' 
           AND object_id = OBJECT_ID('dbo.ProductGroupPolicies'))
BEGIN
    DROP INDEX IX_ProductGroupPolicies_ProductGroupId ON dbo.ProductGroupPolicies;
    PRINT 'تم حذف الفهرس IX_ProductGroupPolicies_ProductGroupId';
END
ELSE
    PRINT 'الفهرس غير موجود';

IF EXISTS (SELECT 1 FROM sys.indexes 
           WHERE name = 'IX_ProductGroupPolicies_ProductGroupld' 
           AND object_id = OBJECT_ID('dbo.ProductGroupPolicies'))
BEGIN
    DROP INDEX IX_ProductGroupPolicies_ProductGroupld ON dbo.ProductGroupPolicies;
    PRINT 'تم حذف الفهرس IX_ProductGroupPolicies_ProductGroupld';
END
GO
