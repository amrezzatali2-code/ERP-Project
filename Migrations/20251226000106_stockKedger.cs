using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class stockKedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Stock_Batches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ProdId = table.Column<int>(type: "int", nullable: false),
                    BatchNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Expiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    QtyOnHand = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    QtyReserved = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stock_Batches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stock_Batches_WarehouseId_ProdId",
                table: "Stock_Batches",
                columns: new[] { "WarehouseId", "ProdId" });

            migrationBuilder.CreateIndex(
                name: "IX_Stock_Batches_WarehouseId_ProdId_BatchNo_Expiry",
                table: "Stock_Batches",
                columns: new[] { "WarehouseId", "ProdId", "BatchNo", "Expiry" },
                unique: true,
                filter: "[Expiry] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Stock_Batches");
        }
    }
}
