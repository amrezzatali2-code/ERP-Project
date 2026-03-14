using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentsAndJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // إنشاء الجداول فقط إن لم تكن موجودة (قد تكون أُنشئت في محاولة سابقة فاشلة)
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Departments')
                CREATE TABLE [Departments] (
                    [Id] int NOT NULL IDENTITY(1,1),
                    [Name] nvarchar(100) NOT NULL,
                    [Code] nvarchar(20) NULL,
                    [SortOrder] int NOT NULL,
                    [IsActive] bit NOT NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [UpdatedAt] datetime2 NULL,
                    CONSTRAINT [PK_Departments] PRIMARY KEY ([Id])
                );
            ");
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Jobs')
                CREATE TABLE [Jobs] (
                    [Id] int NOT NULL IDENTITY(1,1),
                    [Name] nvarchar(100) NOT NULL,
                    [Code] nvarchar(20) NULL,
                    [SortOrder] int NOT NULL,
                    [IsActive] bit NOT NULL,
                    [CreatedAt] datetime2 NOT NULL,
                    [UpdatedAt] datetime2 NULL,
                    CONSTRAINT [PK_Jobs] PRIMARY KEY ([Id])
                );
            ");

            // جدول Employees موجود مسبقاً — نضيف الأعمدة والعلاقات فقط (بشكل آمن إن وُجدت من سكربت سابق)
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Employees') AND name = 'DepartmentId')
                    ALTER TABLE [Employees] ADD [DepartmentId] int NULL;
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Employees') AND name = 'JobId')
                    ALTER TABLE [Employees] ADD [JobId] int NULL;
                IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Employees') AND name = 'Department')
                    ALTER TABLE [Employees] DROP COLUMN [Department];
                IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Employees') AND name = 'JobTitle')
                    ALTER TABLE [Employees] DROP COLUMN [JobTitle];
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Employees_DepartmentId' AND object_id = OBJECT_ID('Employees'))
                    CREATE INDEX [IX_Employees_DepartmentId] ON [Employees] ([DepartmentId]);
                IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Employees_JobId' AND object_id = OBJECT_ID('Employees'))
                    CREATE INDEX [IX_Employees_JobId] ON [Employees] ([JobId]);
            ");

            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Employees_Departments_DepartmentId')
                    ALTER TABLE [Employees] ADD CONSTRAINT [FK_Employees_Departments_DepartmentId]
                        FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([Id]) ON DELETE SET NULL;
                IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Employees_Jobs_JobId')
                    ALTER TABLE [Employees] ADD CONSTRAINT [FK_Employees_Jobs_JobId]
                        FOREIGN KEY ([JobId]) REFERENCES [Jobs] ([Id]) ON DELETE SET NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Employees_Departments_DepartmentId')
                    ALTER TABLE [Employees] DROP CONSTRAINT [FK_Employees_Departments_DepartmentId];
                IF EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_Employees_Jobs_JobId')
                    ALTER TABLE [Employees] DROP CONSTRAINT [FK_Employees_Jobs_JobId];
                IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Employees_DepartmentId' AND object_id = OBJECT_ID('Employees'))
                    DROP INDEX [IX_Employees_DepartmentId] ON [Employees];
                IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Employees_JobId' AND object_id = OBJECT_ID('Employees'))
                    DROP INDEX [IX_Employees_JobId] ON [Employees];
                IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Employees') AND name = 'DepartmentId')
                    ALTER TABLE [Employees] DROP COLUMN [DepartmentId];
                IF EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Employees') AND name = 'JobId')
                    ALTER TABLE [Employees] DROP COLUMN [JobId];
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Employees') AND name = 'Department')
                    ALTER TABLE [Employees] ADD [Department] nvarchar(100) NULL;
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Employees') AND name = 'JobTitle')
                    ALTER TABLE [Employees] ADD [JobTitle] nvarchar(100) NULL;
            ");
            migrationBuilder.DropTable(
                name: "Departments");
            migrationBuilder.DropTable(
                name: "Jobs");
        }
    }
}
