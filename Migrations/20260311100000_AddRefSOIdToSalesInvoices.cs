using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddRefSOIdToSalesInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RefSOId",
                table: "SalesInvoices",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_RefSOId",
                table: "SalesInvoices",
                column: "RefSOId");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_SalesOrders_RefSOId",
                table: "SalesInvoices",
                column: "RefSOId",
                principalTable: "SalesOrders",
                principalColumn: "SOId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_SalesOrders_RefSOId",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_RefSOId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "RefSOId",
                table: "SalesInvoices");
        }
    }
}
