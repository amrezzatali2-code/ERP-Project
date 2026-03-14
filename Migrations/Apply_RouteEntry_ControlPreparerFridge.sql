-- إضافة أعمدة الكونترول والمحضر وجدول سطور أصناف الثلاجة لبيانات خط السير
-- نفّذ هذا السكربت يدوياً ثم سجّل الـ migration إن لزم.

-- 1) أعمدة الموظفين في SalesInvoiceRoutes
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('SalesInvoiceRoutes') AND name = 'ControlEmployeeId')
    ALTER TABLE [dbo].[SalesInvoiceRoutes] ADD [ControlEmployeeId] int NULL;
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('SalesInvoiceRoutes') AND name = 'PreparerEmployeeId')
    ALTER TABLE [dbo].[SalesInvoiceRoutes] ADD [PreparerEmployeeId] int NULL;

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_SalesInvoiceRoutes_Employees_ControlEmployeeId')
    ALTER TABLE [dbo].[SalesInvoiceRoutes] ADD CONSTRAINT [FK_SalesInvoiceRoutes_Employees_ControlEmployeeId]
        FOREIGN KEY ([ControlEmployeeId]) REFERENCES [dbo].[Employees] ([Id]) ON DELETE SET NULL;
IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_SalesInvoiceRoutes_Employees_PreparerEmployeeId')
    ALTER TABLE [dbo].[SalesInvoiceRoutes] ADD CONSTRAINT [FK_SalesInvoiceRoutes_Employees_PreparerEmployeeId]
        FOREIGN KEY ([PreparerEmployeeId]) REFERENCES [dbo].[Employees] ([Id]) ON DELETE SET NULL;

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SalesInvoiceRoutes_ControlEmployeeId' AND object_id = OBJECT_ID('SalesInvoiceRoutes'))
    CREATE INDEX [IX_SalesInvoiceRoutes_ControlEmployeeId] ON [dbo].[SalesInvoiceRoutes] ([ControlEmployeeId]);
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_SalesInvoiceRoutes_PreparerEmployeeId' AND object_id = OBJECT_ID('SalesInvoiceRoutes'))
    CREATE INDEX [IX_SalesInvoiceRoutes_PreparerEmployeeId] ON [dbo].[SalesInvoiceRoutes] ([PreparerEmployeeId]);

-- 2) جدول سطور أصناف الثلاجة
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SalesInvoiceRouteFridgeLines')
BEGIN
    CREATE TABLE [dbo].[SalesInvoiceRouteFridgeLines] (
        [Id] int NOT NULL IDENTITY(1,1),
        [SIId] int NOT NULL,
        [ProductId] int NOT NULL,
        [Qty] int NOT NULL,
        CONSTRAINT [PK_SalesInvoiceRouteFridgeLines] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SalesInvoiceRouteFridgeLines_SalesInvoiceRoutes_SIId] FOREIGN KEY ([SIId]) REFERENCES [dbo].[SalesInvoiceRoutes] ([SIId]) ON DELETE CASCADE,
        CONSTRAINT [FK_SalesInvoiceRouteFridgeLines_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [dbo].[Products] ([ProdId]) ON DELETE NO ACTION
    );
    CREATE INDEX [IX_SalesInvoiceRouteFridgeLines_SIId] ON [dbo].[SalesInvoiceRouteFridgeLines] ([SIId]);
    CREATE INDEX [IX_SalesInvoiceRouteFridgeLines_ProductId] ON [dbo].[SalesInvoiceRouteFridgeLines] ([ProductId]);
END
