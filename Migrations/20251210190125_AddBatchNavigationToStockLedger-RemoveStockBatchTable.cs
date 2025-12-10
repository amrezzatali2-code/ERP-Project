using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchNavigationToStockLedgerRemoveStockBatchTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Stock_Batches");

            migrationBuilder.DropIndex(
                name: "IX_Batches_ProdId_BatchNo_Expiry",
                table: "Batches");

            migrationBuilder.AddColumn<int>(
                name: "BatchId",
                table: "StockLedger",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Batches",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Batches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Batches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_BatchId",
                table: "StockLedger",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_ProdId",
                table: "StockLedger",
                column: "ProdId");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_CustomerId",
                table: "Batches",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ProdId",
                table: "Batches",
                column: "ProdId");

            migrationBuilder.AddForeignKey(
                name: "FK_Batches_Customers_CustomerId",
                table: "Batches",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "CustomerId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockLedger_Batches_BatchId",
                table: "StockLedger",
                column: "BatchId",
                principalTable: "Batches",
                principalColumn: "BatchId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockLedger_Products_ProdId",
                table: "StockLedger",
                column: "ProdId",
                principalTable: "Products",
                principalColumn: "ProdId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockLedger_Warehouses_WarehouseId",
                table: "StockLedger",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "WarehouseId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Batches_Customers_CustomerId",
                table: "Batches");

            migrationBuilder.DropForeignKey(
                name: "FK_StockLedger_Batches_BatchId",
                table: "StockLedger");

            migrationBuilder.DropForeignKey(
                name: "FK_StockLedger_Products_ProdId",
                table: "StockLedger");

            migrationBuilder.DropForeignKey(
                name: "FK_StockLedger_Warehouses_WarehouseId",
                table: "StockLedger");

            migrationBuilder.DropIndex(
                name: "IX_StockLedger_BatchId",
                table: "StockLedger");

            migrationBuilder.DropIndex(
                name: "IX_StockLedger_ProdId",
                table: "StockLedger");

            migrationBuilder.DropIndex(
                name: "IX_Batches_CustomerId",
                table: "Batches");

            migrationBuilder.DropIndex(
                name: "IX_Batches_ProdId",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "StockLedger");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Batches");

            migrationBuilder.CreateTable(
                name: "Stock_Batches",
                columns: table => new
                {
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ProdId = table.Column<int>(type: "int", nullable: false),
                    BatchNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Expiry = table.Column<DateTime>(type: "datetime2", nullable: false),
                    QtyOnHand = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stock_Batches", x => new { x.WarehouseId, x.ProdId, x.BatchNo, x.Expiry });
                    table.CheckConstraint("CK_StockBatches_Qty", "[QtyOnHand] >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ProdId_BatchNo_Expiry",
                table: "Batches",
                columns: new[] { "ProdId", "BatchNo", "Expiry" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stock_Batches_ProdId_Expiry",
                table: "Stock_Batches",
                columns: new[] { "ProdId", "Expiry" });

            migrationBuilder.CreateIndex(
                name: "IX_Stock_Batches_QtyOnHand",
                table: "Stock_Batches",
                column: "QtyOnHand");
        }
    }
}
