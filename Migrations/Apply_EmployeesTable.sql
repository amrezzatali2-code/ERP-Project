-- إنشاء جدول الموظفين فقط (استخدمه إذا كانت جداول خط السير مُطبَّقة مسبقاً)
-- نفّذ على قاعدة بيانات المشروع من SSMS أو Azure Data Studio

IF OBJECT_ID(N'dbo.Employees', N'U') IS NOT NULL
    RETURN; -- الجدول موجود مسبقاً

CREATE TABLE [dbo].[Employees] (
    [Id] int NOT NULL IDENTITY(1,1),
    [FullName] nvarchar(100) NOT NULL,
    [Code] nvarchar(20) NULL,
    [NationalId] nvarchar(20) NULL,
    [BirthDate] datetime2 NULL,
    [HireDate] datetime2 NULL,
    [Department] nvarchar(100) NULL,
    [JobTitle] nvarchar(100) NULL,
    [Phone1] nvarchar(20) NULL,
    [Phone2] nvarchar(20) NULL,
    [Email] nvarchar(100) NULL,
    [Address] nvarchar(300) NULL,
    [BaseSalary] decimal(18,2) NULL,
    [IsActive] bit NOT NULL,
    [Notes] nvarchar(500) NULL,
    [UserId] int NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_Employees] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Employees_Users_UserId] FOREIGN KEY ([UserId])
        REFERENCES [dbo].[Users] ([UserId]) ON DELETE SET NULL
);

CREATE INDEX [IX_Employees_UserId] ON [dbo].[Employees] ([UserId]);

-- تسجيل الـ migration حتى لا يعيد EF تطبيقه
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260313185731_AddEmployeesTable')
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260313185731_AddEmployeesTable', N'8.0.0');
