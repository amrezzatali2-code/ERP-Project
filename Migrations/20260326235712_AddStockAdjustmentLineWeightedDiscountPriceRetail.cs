using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddStockAdjustmentLineWeightedDiscountPriceRetail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PriceRetail",
                table: "StockAdjustmentLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WeightedDiscountPct",
                table: "StockAdjustmentLines",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_WarehouseId",
                table: "PurchaseReturns",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseReturns_Warehouses_WarehouseId",
                table: "PurchaseReturns",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "WarehouseId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesReturnLines_Products_ProdId",
                table: "SalesReturnLines",
                column: "ProdId",
                principalTable: "Products",
                principalColumn: "ProdId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReturns_Warehouses_WarehouseId",
                table: "PurchaseReturns");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesReturnLines_Products_ProdId",
                table: "SalesReturnLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReturns_WarehouseId",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "PriceRetail",
                table: "StockAdjustmentLines");

            migrationBuilder.DropColumn(
                name: "WeightedDiscountPct",
                table: "StockAdjustmentLines");
        }
    }
}
