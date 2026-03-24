-- =============================================================================
-- إضافة عمود الموقع (Location) لجدول الأصناف — لحل خطأ Invalid column name 'Location'
-- يُنفَّذ مرة واحدة على قاعدة ERP إذا لم تكن قد نفذت الهجرة من EF.
-- =============================================================================

USE [ERP];
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Products') AND name = N'Location'
)
BEGIN
    ALTER TABLE [dbo].[Products]
    ADD [Location] NVARCHAR(50) NULL;
    PRINT N'تمت إضافة عمود Location لجدول Products بنجاح.';
END
ELSE
    PRINT N'عمود Location موجود مسبقاً في جدول Products.';
GO
