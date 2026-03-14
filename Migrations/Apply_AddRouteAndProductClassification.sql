-- تشغيل هذا السكربت يدوياً إذا لم يُطبَّق الـ migration (مثلاً أثناء تشغيل التطبيق)
-- نفّذه في قاعدة بيانات المشروع من SSMS أو Azure Data Studio

BEGIN TRANSACTION;

-- 1) جدول التصنيفات
IF OBJECT_ID(N'dbo.ProductClassifications', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ProductClassifications] (
        [Id] int NOT NULL IDENTITY(1,1),
        [Name] nvarchar(100) NOT NULL,
        [Code] nvarchar(20) NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_ProductClassifications] PRIMARY KEY ([Id])
    );
END

-- 2) جدول خطوط السير
IF OBJECT_ID(N'dbo.Routes', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Routes] (
        [Id] int NOT NULL IDENTITY(1,1),
        [Name] nvarchar(100) NOT NULL,
        [Code] nvarchar(20) NULL,
        [SortOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_Routes] PRIMARY KEY ([Id])
    );
END

-- 3) عمود التصنيف في Products
IF COL_LENGTH(N'dbo.Products', N'ClassificationId') IS NULL
BEGIN
    ALTER TABLE [dbo].[Products] ADD [ClassificationId] int NULL;
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Products_ClassificationId' AND object_id = OBJECT_ID(N'dbo.Products'))
BEGIN
    CREATE INDEX [IX_Products_ClassificationId] ON [dbo].[Products] ([ClassificationId]);
END
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Products_ProductClassifications_ClassificationId')
BEGIN
    ALTER TABLE [dbo].[Products] ADD CONSTRAINT [FK_Products_ProductClassifications_ClassificationId]
        FOREIGN KEY ([ClassificationId]) REFERENCES [dbo].[ProductClassifications] ([Id]) ON DELETE SET NULL;
END

-- 4) عمود خط السير في Customers
IF COL_LENGTH(N'dbo.Customers', N'RouteId') IS NULL
BEGIN
    ALTER TABLE [dbo].[Customers] ADD [RouteId] int NULL;
END
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Customers_RouteId' AND object_id = OBJECT_ID(N'dbo.Customers'))
BEGIN
    CREATE INDEX [IX_Customers_RouteId] ON [dbo].[Customers] ([RouteId]);
END
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Customers_Routes_RouteId')
BEGIN
    ALTER TABLE [dbo].[Customers] ADD CONSTRAINT [FK_Customers_Routes_RouteId]
        FOREIGN KEY ([RouteId]) REFERENCES [dbo].[Routes] ([Id]) ON DELETE SET NULL;
END

-- 5) جدول بيانات خط السير للفواتير
IF OBJECT_ID(N'dbo.SalesInvoiceRoutes', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[SalesInvoiceRoutes] (
        [SIId] int NOT NULL,
        [BagsCount] int NOT NULL,
        [PacketsCount] int NOT NULL,
        [CartonsCount] int NOT NULL,
        [FridgeItemsCount] int NOT NULL,
        [FridgeBoxesCount] int NOT NULL,
        [Notes] nvarchar(500) NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_SalesInvoiceRoutes] PRIMARY KEY ([SIId]),
        CONSTRAINT [FK_SalesInvoiceRoutes_SalesInvoices_SIId] FOREIGN KEY ([SIId])
            REFERENCES [dbo].[SalesInvoices] ([SIId]) ON DELETE CASCADE
    );
END

-- 6) تسجيل الـ migration في السجل (حتى لا يعيد EF تطبيقه لاحقاً)
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260313000000_AddRouteAndProductClassification')
BEGIN
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260313000000_AddRouteAndProductClassification', N'8.0.0');
END

COMMIT TRANSACTION;
