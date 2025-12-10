using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class update_purchaseRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedItemsTotal",
                table: "PurchaseRequests",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsConverted",
                table: "PurchaseRequests",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "TotalQtyRequested",
                table: "PurchaseRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpectedItemsTotal",
                table: "PurchaseRequests");

            migrationBuilder.DropColumn(
                name: "IsConverted",
                table: "PurchaseRequests");

            migrationBuilder.DropColumn(
                name: "TotalQtyRequested",
                table: "PurchaseRequests");
        }
    }
}
