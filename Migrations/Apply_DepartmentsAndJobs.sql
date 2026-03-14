-- سكربت تطبيق جدولي الأقسام والوظائف وربطهم بجدول الموظفين
-- نفّذ هذا السكربت يدوياً إذا لم تستطع تشغيل: dotnet ef database update
-- بعد التنفيذ أضف سجل الـ migration في __EFMigrationsHistory (انظر الأسفل)

BEGIN TRANSACTION;

-- 1) إنشاء جدول الأقسام
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Departments')
BEGIN
    CREATE TABLE [dbo].[Departments] (
        [Id] int NOT NULL IDENTITY(1,1),
        [Name] nvarchar(100) NOT NULL,
        [Code] nvarchar(20) NULL,
        [SortOrder] int NOT NULL DEFAULT 0,
        [IsActive] bit NOT NULL DEFAULT 1,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_Departments] PRIMARY KEY ([Id])
    );
END

-- 2) إنشاء جدول الوظائف
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Jobs')
BEGIN
    CREATE TABLE [dbo].[Jobs] (
        [Id] int NOT NULL IDENTITY(1,1),
        [Name] nvarchar(100) NOT NULL,
        [Code] nvarchar(20) NULL,
        [SortOrder] int NOT NULL DEFAULT 0,
        [IsActive] bit NOT NULL DEFAULT 1,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_Jobs] PRIMARY KEY ([Id])
    );
END

-- 3) إضافة أعمدة الربط في الموظفين (إن لم تكن موجودة)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Employees') AND name = 'DepartmentId')
    ALTER TABLE [dbo].[Employees] ADD [DepartmentId] int NULL;

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Employees') AND name = 'JobId')
    ALTER TABLE [dbo].[Employees] ADD [JobId] int NULL;

-- 4) حذف الأعمدة النصية القديمة (إن وُجدت)
IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Employees') AND name = 'Department')
    ALTER TABLE [dbo].[Employees] DROP COLUMN [Department];

IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Employees') AND name = 'JobTitle')
    ALTER TABLE [dbo].[Employees] DROP COLUMN [JobTitle];

-- 5) إنشاء الفهارس والعلاقات
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Employees_DepartmentId' AND object_id = OBJECT_ID('Employees'))
    CREATE INDEX [IX_Employees_DepartmentId] ON [dbo].[Employees] ([DepartmentId]);

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Employees_JobId' AND object_id = OBJECT_ID('Employees'))
    CREATE INDEX [IX_Employees_JobId] ON [dbo].[Employees] ([JobId]);

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Employees_Departments_DepartmentId')
    ALTER TABLE [dbo].[Employees] ADD CONSTRAINT [FK_Employees_Departments_DepartmentId]
        FOREIGN KEY ([DepartmentId]) REFERENCES [dbo].[Departments] ([Id]) ON DELETE SET NULL;

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Employees_Jobs_JobId')
    ALTER TABLE [dbo].[Employees] ADD CONSTRAINT [FK_Employees_Jobs_JobId]
        FOREIGN KEY ([JobId]) REFERENCES [dbo].[Jobs] ([Id]) ON DELETE SET NULL;

COMMIT;

-- بعد تشغيل السكربت أعلاه، سجّل الـ migration حتى لا يحاول EF تطبيقها مرة أخرى:
-- INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion]) 
-- VALUES (N'20260313190000_AddDepartmentsAndJobs', N'8.0.11');
-- (استخدم نفس MigrationId الذي يظهر عند تنفيذك: dotnet ef migrations add AddDepartmentsAndJobs)
