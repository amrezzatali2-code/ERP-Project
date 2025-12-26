using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class Fix_PendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stock_Batches_WarehouseId_ProdId_BatchNo_Expiry",
                table: "Stock_Batches");

            migrationBuilder.AlterColumn<DateTime>(
                name: "Expiry",
                table: "Stock_Batches",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stock_Batches_WarehouseId_ProdId_BatchNo_Expiry",
                table: "Stock_Batches",
                columns: new[] { "WarehouseId", "ProdId", "BatchNo", "Expiry" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stock_Batches_WarehouseId_ProdId_BatchNo_Expiry",
                table: "Stock_Batches");

            migrationBuilder.AlterColumn<DateTime>(
                name: "Expiry",
                table: "Stock_Batches",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.CreateIndex(
                name: "IX_Stock_Batches_WarehouseId_ProdId_BatchNo_Expiry",
                table: "Stock_Batches",
                columns: new[] { "WarehouseId", "ProdId", "BatchNo", "Expiry" },
                unique: true,
                filter: "[Expiry] IS NOT NULL");
        }
    }
}
