-- إصلاح: جدول Employees موجود لكن EF يحاول إنشاءه مرة أخرى
-- نفّذ هذا السكربت مرة واحدة فقط عندما يظهر الخطأ:
-- "There is already an object named 'Employees' in the database."

-- تسجيل أن migration جدول الموظفين تم تطبيقه مسبقاً (لأن الجدول موجود فعلاً)
IF NOT EXISTS (SELECT 1 FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'20260313185731_AddEmployeesTable')
    INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260313185731_AddEmployeesTable', N'8.0.11');
