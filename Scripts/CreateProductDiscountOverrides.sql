-- ============================================================
-- إنشاء جدول ProductDiscountOverrides يدوياً
-- شغّل هذا السكربت في SQL Server (SSMS أو Azure Data Studio) إذا لم تُطبّق الهجرة بعد.
-- ============================================================
--
-- مهم: يجب تشغيل السكربت على قاعدة بيانات مشروع ERP نفسها
--       (القاعدة التي فيها جداول Products, Warehouses, Batches).
--
-- في SSMS: من القائمة المنسدلة أعلى نافذة الاستعلام اختر قاعدة بيانات ERP
--          (مثلاً: ERP أو ErpDb أو الاسم الموجود في Connection String بالمشروع).
--
-- أو ألغِ التعليق عن السطر التالي وضَع اسم قاعدتك مكان YourErpDatabaseName:
-- USE [YourErpDatabaseName];
-- ============================================================

IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ProductDiscountOverrides')
BEGIN
    CREATE TABLE [dbo].[ProductDiscountOverrides] (
        [Id]                INT             IDENTITY(1,1) NOT NULL,
        [ProductId]         INT             NOT NULL,
        [WarehouseId]       INT             NULL,
        [BatchId]           INT             NULL,
        [OverrideDiscountPct] DECIMAL(5,2)  NOT NULL,
        [Reason]            NVARCHAR(200)  NULL,
        [CreatedBy]         NVARCHAR(100)   NULL,
        [CreatedAt]         DATETIME2       NOT NULL CONSTRAINT [DF_ProductDiscountOverrides_CreatedAt] DEFAULT (getdate()),
        CONSTRAINT [PK_ProductDiscountOverrides] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductDiscountOverrides_Products_ProductId] 
            FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProdId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductDiscountOverrides_Warehouses_WarehouseId] 
            FOREIGN KEY ([WarehouseId]) REFERENCES [dbo].[Warehouses] ([WarehouseId]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductDiscountOverrides_Batches_BatchId] 
            FOREIGN KEY ([BatchId]) REFERENCES [dbo].[Batches] ([BatchId]) ON DELETE NO ACTION
    );

    CREATE INDEX [IX_ProductDiscountOverrides_Product] 
        ON [dbo].[ProductDiscountOverrides] ([ProductId]);

    CREATE INDEX [IX_ProductDiscountOverrides_ProductWarehouseBatch] 
        ON [dbo].[ProductDiscountOverrides] ([ProductId], [WarehouseId], [BatchId]);

    PRINT 'تم إنشاء جدول ProductDiscountOverrides.';

    -- تسجيل الهجرة في __EFMigrationsHistory حتى لا يحاول EF تطبيقها مرة أخرى
    IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260224100000_AddProductDiscountOverrides')
        INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
        VALUES (N'20260224100000_AddProductDiscountOverrides', N'9.0.10');
END
ELSE
    PRINT 'الجدول ProductDiscountOverrides موجود مسبقاً.';
