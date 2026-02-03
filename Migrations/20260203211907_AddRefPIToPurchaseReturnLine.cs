using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddRefPIToPurchaseReturnLine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RefPIId",
                table: "PurchaseReturnLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefPILineNo",
                table: "PurchaseReturnLines",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefPIId",
                table: "PurchaseReturnLines");

            migrationBuilder.DropColumn(
                name: "RefPILineNo",
                table: "PurchaseReturnLines");
        }
    }
}
