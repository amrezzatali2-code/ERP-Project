using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddStockAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Batches",
                table: "Batches");

            migrationBuilder.DropIndex(
                name: "IX_Batches_ProdId",
                table: "Batches");

            migrationBuilder.DropIndex(
                name: "IX_Batches_ProdId_Expiry",
                table: "Batches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Batches_PriceRetailBatch",
                table: "Batches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Batches_UnitCostDefault",
                table: "Batches");


            migrationBuilder.AlterColumn<DateTime>(
                name: "EntryDate",
                table: "Batches",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldDefaultValueSql: "SYSDATETIME()");

            migrationBuilder.AlterColumn<string>(
                name: "BatchNo",
                table: "Batches",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Batches",
                table: "Batches",
                column: "BatchId");

            migrationBuilder.CreateTable(
                name: "StockAdjustments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AdjustmentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ReferenceNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockAdjustments_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "WarehouseId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockAdjustmentLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockAdjustmentId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    BatchId = table.Column<int>(type: "int", nullable: true),
                    QtyBefore = table.Column<int>(type: "int", nullable: false),
                    QtyAfter = table.Column<int>(type: "int", nullable: false),
                    QtyDiff = table.Column<int>(type: "int", nullable: false),
                    CostPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CostDiff = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAdjustmentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockAdjustmentLines_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "BatchId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockAdjustmentLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProdId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockAdjustmentLines_StockAdjustments_StockAdjustmentId",
                        column: x => x.StockAdjustmentId,
                        principalTable: "StockAdjustments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ProdId_BatchNo_Expiry",
                table: "Batches",
                columns: new[] { "ProdId", "BatchNo", "Expiry" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustmentLines_BatchId",
                table: "StockAdjustmentLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustmentLines_ProductId",
                table: "StockAdjustmentLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustmentLines_StockAdjustmentId",
                table: "StockAdjustmentLines",
                column: "StockAdjustmentId");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_WarehouseId",
                table: "StockAdjustments",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Batches_Products_ProdId",
                table: "Batches",
                column: "ProdId",
                principalTable: "Products",
                principalColumn: "ProdId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Batches_Products_ProdId",
                table: "Batches");

            migrationBuilder.DropTable(
                name: "StockAdjustmentLines");

            migrationBuilder.DropTable(
                name: "StockAdjustments");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Batches",
                table: "Batches");

            migrationBuilder.DropIndex(
                name: "IX_Batches_ProdId_BatchNo_Expiry",
                table: "Batches");

       

            migrationBuilder.AlterColumn<DateTime>(
                name: "EntryDate",
                table: "Batches",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSDATETIME()",
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AlterColumn<string>(
                name: "BatchNo",
                table: "Batches",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Batches",
                table: "Batches",
                columns: new[] { "ProdId", "BatchNo", "Expiry" });

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ProdId",
                table: "Batches",
                column: "ProdId");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ProdId_Expiry",
                table: "Batches",
                columns: new[] { "ProdId", "Expiry" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Batches_PriceRetailBatch",
                table: "Batches",
                sql: "[PriceRetailBatch] IS NULL OR [PriceRetailBatch] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Batches_UnitCostDefault",
                table: "Batches",
                sql: "[UnitCostDefault] IS NULL OR [UnitCostDefault] >= 0");
        }
    }
}
