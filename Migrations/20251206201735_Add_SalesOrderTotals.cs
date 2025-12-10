using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class Add_SalesOrderTotals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedItemsTotal",
                table: "SalesOrders",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "TotalQtyRequested",
                table: "SalesOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountTotal",
                table: "PurchaseReturns",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ItemsTotal",
                table: "PurchaseReturns",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "NetTotal",
                table: "PurchaseReturns",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TaxTotal",
                table: "PurchaseReturns",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpectedItemsTotal",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "TotalQtyRequested",
                table: "SalesOrders");

            migrationBuilder.DropColumn(
                name: "DiscountTotal",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "ItemsTotal",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "NetTotal",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "TaxTotal",
                table: "PurchaseReturns");
        }
    }
}
