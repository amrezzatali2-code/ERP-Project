using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class updatesalesinvoicestableprofitneedscalculate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostPerUnit",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CostTotal",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProfitPercent",
                table: "SalesInvoiceLines",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ProfitValue",
                table: "SalesInvoiceLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PurchaseDiscountEffective",
                table: "SalesInvoiceLines",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CostPerUnit",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "CostTotal",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "ProfitPercent",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "ProfitValue",
                table: "SalesInvoiceLines");

            migrationBuilder.DropColumn(
                name: "PurchaseDiscountEffective",
                table: "SalesInvoiceLines");
        }
    }
}
