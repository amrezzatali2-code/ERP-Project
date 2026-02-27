-- ============================================================
-- إزالة أعمدة الخصم والتكلفة المعدّلة (إن وُجدت)
-- نفّذ على قاعدة بيانات ERP إذا كانت الأعمدة مضافة سابقاً
-- ============================================================

USE [ERP];
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Products]') AND name = 'WeightedDiscountOverride')
    ALTER TABLE [dbo].[Products] DROP COLUMN [WeightedDiscountOverride];
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Products]') AND name = 'UnitCostOverride')
    ALTER TABLE [dbo].[Products] DROP COLUMN [UnitCostOverride];
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Batches]') AND name = 'WeightedDiscountOverride')
    ALTER TABLE [dbo].[Batches] DROP COLUMN [WeightedDiscountOverride];
GO

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Batches]') AND name = 'UnitCostOverride')
    ALTER TABLE [dbo].[Batches] DROP COLUMN [UnitCostOverride];
GO
