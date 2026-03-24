-- تطبيق هجرة: إضافة كود الإكسل (ExternalCode) والمخزن (WarehouseId) لجدول الأصناف
-- تشغيل هذا السكربت يدوياً إذا لم تُطبَّق الهجرة عبر dotnet ef database update

-- 1) إضافة عمود كود الإكسل (إن لم يكن موجوداً)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Products') AND name = N'ExternalCode'
)
BEGIN
    ALTER TABLE dbo.Products
    ADD ExternalCode nvarchar(50) NULL;
END
GO

-- 2) إضافة عمود المخزن (إن لم يكن موجوداً)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Products') AND name = N'WarehouseId'
)
BEGIN
    ALTER TABLE dbo.Products
    ADD WarehouseId int NULL;
END
GO

-- 3) إنشاء الفهرس (إن لم يكن موجوداً)
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Products') AND name = N'IX_Products_WarehouseId'
)
BEGIN
    CREATE INDEX IX_Products_WarehouseId ON dbo.Products (WarehouseId);
END
GO

-- 4) إضافة المفتاح الأجنبي (إن لم يكن موجوداً)
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'FK_Products_Warehouses_WarehouseId'
)
BEGIN
    ALTER TABLE dbo.Products
    ADD CONSTRAINT FK_Products_Warehouses_WarehouseId
    FOREIGN KEY (WarehouseId) REFERENCES dbo.Warehouses(WarehouseId) ON DELETE SET NULL;
END
GO

-- 5) تسجيل الهجرة في جدول السجل (حتى لا يحاول EF تطبيقها مرة أخرى)
IF NOT EXISTS (
    SELECT 1 FROM dbo.__EFMigrationsHistory
    WHERE MigrationId = N'20260316000000_AddExternalCodeAndWarehouseIdToProduct'
)
BEGIN
    INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
    VALUES (N'20260316000000_AddExternalCodeAndWarehouseIdToProduct', N'8.0.0');
END
GO
