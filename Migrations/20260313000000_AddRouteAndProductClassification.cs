using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <summary>
    /// إضافة: تصنيفات الأصناف، خطوط السير، بيانات خط السير للفاتورة؛ ربط الصنف بالتصنيف والعميل بخط السير.
    /// </summary>
    public partial class AddRouteAndProductClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductClassifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_ProductClassifications", x => x.Id));

            migrationBuilder.CreateTable(
                name: "Routes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table => table.PrimaryKey("PK_Routes", x => x.Id));

            migrationBuilder.AddColumn<int>(
                name: "ClassificationId",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RouteId",
                table: "Customers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalesInvoiceRoutes",
                columns: table => new
                {
                    SIId = table.Column<int>(type: "int", nullable: false),
                    BagsCount = table.Column<int>(type: "int", nullable: false),
                    PacketsCount = table.Column<int>(type: "int", nullable: false),
                    CartonsCount = table.Column<int>(type: "int", nullable: false),
                    FridgeItemsCount = table.Column<int>(type: "int", nullable: false),
                    FridgeBoxesCount = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceRoutes", x => x.SIId);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceRoutes_SalesInvoices_SIId",
                        column: x => x.SIId,
                        principalTable: "SalesInvoices",
                        principalColumn: "SIId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_ClassificationId",
                table: "Products",
                column: "ClassificationId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_RouteId",
                table: "Customers",
                column: "RouteId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_ProductClassifications_ClassificationId",
                table: "Products",
                column: "ClassificationId",
                principalTable: "ProductClassifications",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Routes_RouteId",
                table: "Customers",
                column: "RouteId",
                principalTable: "Routes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_Products_ProductClassifications_ClassificationId", table: "Products");
            migrationBuilder.DropForeignKey(name: "FK_Customers_Routes_RouteId", table: "Customers");
            migrationBuilder.DropTable(name: "SalesInvoiceRoutes");
            migrationBuilder.DropIndex(name: "IX_Products_ClassificationId", table: "Products");
            migrationBuilder.DropIndex(name: "IX_Customers_RouteId", table: "Customers");
            migrationBuilder.DropColumn(name: "ClassificationId", table: "Products");
            migrationBuilder.DropColumn(name: "RouteId", table: "Customers");
            migrationBuilder.DropTable(name: "ProductClassifications");
            migrationBuilder.DropTable(name: "Routes");
        }
    }
}
