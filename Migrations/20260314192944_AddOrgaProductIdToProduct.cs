using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgaProductIdToProduct : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // إصلاح أسماء الأعمدة إن وُجدت بخطأ إملائي (Employeeld -> EmployeeId)، ثم ضمان وجود الأعمدة الصحيحة
            migrationBuilder.Sql(@"
-- إصلاح خطأ إملائي: Employeeld -> EmployeeId (إن وُجد العمود الخاطئ والصائب غير موجود)
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SalesInvoiceRoutes') AND name = 'ControlEmployeeld')
  AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SalesInvoiceRoutes') AND name = 'ControlEmployeeId')
    EXEC sp_rename 'dbo.SalesInvoiceRoutes.ControlEmployeeld', 'ControlEmployeeId', 'COLUMN';
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SalesInvoiceRoutes') AND name = 'PreparerEmployeeld')
  AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SalesInvoiceRoutes') AND name = 'PreparerEmployeeId')
    EXEC sp_rename 'dbo.SalesInvoiceRoutes.PreparerEmployeeld', 'PreparerEmployeeId', 'COLUMN';
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SalesInvoiceRoutes') AND name = 'DistributorEmployeeld')
  AND NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SalesInvoiceRoutes') AND name = 'DistributorEmployeeId')
    EXEC sp_rename 'dbo.SalesInvoiceRoutes.DistributorEmployeeld', 'DistributorEmployeeId', 'COLUMN';

-- إضافة الأعمدة الصحيحة فقط إن لم تكن موجودة
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SalesInvoiceRoutes') AND name = 'ControlEmployeeId')
    ALTER TABLE [dbo].[SalesInvoiceRoutes] ADD [ControlEmployeeId] int NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SalesInvoiceRoutes') AND name = 'DistributorEmployeeId')
    ALTER TABLE [dbo].[SalesInvoiceRoutes] ADD [DistributorEmployeeId] int NULL;
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SalesInvoiceRoutes') AND name = 'PreparerEmployeeId')
    ALTER TABLE [dbo].[SalesInvoiceRoutes] ADD [PreparerEmployeeId] int NULL;
");

            // إضافة OrgaProductId لجدول Products إن لم يكن موجوداً
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Products') AND name = 'OrgaProductId')
    ALTER TABLE [dbo].[Products] ADD [OrgaProductId] int NULL;
");

            migrationBuilder.CreateTable(
                name: "SalesInvoiceRouteFridgeLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SIId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Qty = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceRouteFridgeLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceRouteFridgeLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProdId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceRouteFridgeLines_SalesInvoiceRoutes_SIId",
                        column: x => x.SIId,
                        principalTable: "SalesInvoiceRoutes",
                        principalColumn: "SIId",
                        onDelete: ReferentialAction.Cascade);
                });

            // إنشاء الفهارس فقط إن لم تكن موجودة (لتجنب خطأ عند إعادة تشغيل الـ migration)
            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SalesInvoiceRoutes') AND name = 'IX_SalesInvoiceRoutes_ControlEmployeeId')
    CREATE INDEX [IX_SalesInvoiceRoutes_ControlEmployeeId] ON [dbo].[SalesInvoiceRoutes] ([ControlEmployeeId]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SalesInvoiceRoutes') AND name = 'IX_SalesInvoiceRoutes_DistributorEmployeeId')
    CREATE INDEX [IX_SalesInvoiceRoutes_DistributorEmployeeId] ON [dbo].[SalesInvoiceRoutes] ([DistributorEmployeeId]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.SalesInvoiceRoutes') AND name = 'IX_SalesInvoiceRoutes_PreparerEmployeeId')
    CREATE INDEX [IX_SalesInvoiceRoutes_PreparerEmployeeId] ON [dbo].[SalesInvoiceRoutes] ([PreparerEmployeeId]);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Products') AND name = 'IX_Products_OrgaProductId')
    CREATE UNIQUE NONCLUSTERED INDEX [IX_Products_OrgaProductId] ON [dbo].[Products] ([OrgaProductId]) WHERE [OrgaProductId] IS NOT NULL;
");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceRouteFridgeLines_ProductId",
                table: "SalesInvoiceRouteFridgeLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceRouteFridgeLines_SIId",
                table: "SalesInvoiceRouteFridgeLines",
                column: "SIId");

            // إضافة قيود المفتاح الأجنبي لـ Employees بـ ON DELETE NO ACTION لتجنب multiple cascade paths في SQL Server
            // وحذف أي قيود قديمة بالاسم الخاطئ (Employeeld) إن وُجدت
            migrationBuilder.Sql(@"
-- حذف قيود قديمة بالاسم الخاطئ إن وُجدت
IF OBJECT_ID('FK_SalesInvoiceRoutes_Employees_ControlEmployeeld', 'F') IS NOT NULL
    ALTER TABLE [dbo].[SalesInvoiceRoutes] DROP CONSTRAINT [FK_SalesInvoiceRoutes_Employees_ControlEmployeeld];
IF OBJECT_ID('FK_SalesInvoiceRoutes_Employees_DistributorEmployeeld', 'F') IS NOT NULL
    ALTER TABLE [dbo].[SalesInvoiceRoutes] DROP CONSTRAINT [FK_SalesInvoiceRoutes_Employees_DistributorEmployeeld];
IF OBJECT_ID('FK_SalesInvoiceRoutes_Employees_PreparerEmployeeld', 'F') IS NOT NULL
    ALTER TABLE [dbo].[SalesInvoiceRoutes] DROP CONSTRAINT [FK_SalesInvoiceRoutes_Employees_PreparerEmployeeld];

-- إضافة القيود الصحيحة بـ NO ACTION (تجنب cycles or multiple cascade paths)
IF OBJECT_ID('FK_SalesInvoiceRoutes_Employees_ControlEmployeeId', 'F') IS NULL
    ALTER TABLE [dbo].[SalesInvoiceRoutes] ADD CONSTRAINT [FK_SalesInvoiceRoutes_Employees_ControlEmployeeId]
    FOREIGN KEY ([ControlEmployeeId]) REFERENCES [dbo].[Employees] ([Id]) ON DELETE NO ACTION;
IF OBJECT_ID('FK_SalesInvoiceRoutes_Employees_DistributorEmployeeId', 'F') IS NULL
    ALTER TABLE [dbo].[SalesInvoiceRoutes] ADD CONSTRAINT [FK_SalesInvoiceRoutes_Employees_DistributorEmployeeId]
    FOREIGN KEY ([DistributorEmployeeId]) REFERENCES [dbo].[Employees] ([Id]) ON DELETE NO ACTION;
IF OBJECT_ID('FK_SalesInvoiceRoutes_Employees_PreparerEmployeeId', 'F') IS NULL
    ALTER TABLE [dbo].[SalesInvoiceRoutes] ADD CONSTRAINT [FK_SalesInvoiceRoutes_Employees_PreparerEmployeeId]
    FOREIGN KEY ([PreparerEmployeeId]) REFERENCES [dbo].[Employees] ([Id]) ON DELETE NO ACTION;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceRoutes_Employees_ControlEmployeeId",
                table: "SalesInvoiceRoutes");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceRoutes_Employees_DistributorEmployeeId",
                table: "SalesInvoiceRoutes");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceRoutes_Employees_PreparerEmployeeId",
                table: "SalesInvoiceRoutes");

            migrationBuilder.DropTable(
                name: "SalesInvoiceRouteFridgeLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceRoutes_ControlEmployeeId",
                table: "SalesInvoiceRoutes");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceRoutes_DistributorEmployeeId",
                table: "SalesInvoiceRoutes");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceRoutes_PreparerEmployeeId",
                table: "SalesInvoiceRoutes");

            migrationBuilder.DropIndex(
                name: "IX_Products_OrgaProductId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ControlEmployeeId",
                table: "SalesInvoiceRoutes");

            migrationBuilder.DropColumn(
                name: "DistributorEmployeeId",
                table: "SalesInvoiceRoutes");

            migrationBuilder.DropColumn(
                name: "PreparerEmployeeId",
                table: "SalesInvoiceRoutes");

            migrationBuilder.DropColumn(
                name: "OrgaProductId",
                table: "Products");
        }
    }
}
