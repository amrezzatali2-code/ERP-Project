using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddProductCartonQuantityAndPackQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CartonQuantity",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PackQuantity",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_UserId",
                table: "StockLedger",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockLedger_Users_UserId",
                table: "StockLedger",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockLedger_Users_UserId",
                table: "StockLedger");

            migrationBuilder.DropIndex(
                name: "IX_StockLedger_UserId",
                table: "StockLedger");

            migrationBuilder.DropColumn(
                name: "CartonQuantity",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PackQuantity",
                table: "Products");
        }
    }
}
