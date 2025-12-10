using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class link_salesreturn_lines_to_invoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesReturnLines_Percents",
                table: "SalesReturnLines");

            migrationBuilder.AddColumn<int>(
                name: "SalesInvoiceId",
                table: "SalesReturnLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesInvoiceLineNo",
                table: "SalesReturnLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnLines_SalesInvoiceId_SalesInvoiceLineNo",
                table: "SalesReturnLines",
                columns: new[] { "SalesInvoiceId", "SalesInvoiceLineNo" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesReturnLines_Percents",
                table: "SalesReturnLines",
                sql: "[Disc1Percent] BETWEEN 0 AND 100 AND [Disc2Percent] BETWEEN 0 AND 100 AND [Disc3Percent] BETWEEN 0 AND 100 AND [TaxPercent]   BETWEEN 0 AND 100");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesReturnLines_SalesInvoiceLines_SalesInvoiceId_SalesInvoiceLineNo",
                table: "SalesReturnLines",
                columns: new[] { "SalesInvoiceId", "SalesInvoiceLineNo" },
                principalTable: "SalesInvoiceLines",
                principalColumns: new[] { "SIId", "LineNo" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesReturnLines_SalesInvoices_SalesInvoiceId",
                table: "SalesReturnLines",
                column: "SalesInvoiceId",
                principalTable: "SalesInvoices",
                principalColumn: "SIId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesReturnLines_SalesInvoiceLines_SalesInvoiceId_SalesInvoiceLineNo",
                table: "SalesReturnLines");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesReturnLines_SalesInvoices_SalesInvoiceId",
                table: "SalesReturnLines");

            migrationBuilder.DropIndex(
                name: "IX_SalesReturnLines_SalesInvoiceId_SalesInvoiceLineNo",
                table: "SalesReturnLines");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SalesReturnLines_Percents",
                table: "SalesReturnLines");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceId",
                table: "SalesReturnLines");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceLineNo",
                table: "SalesReturnLines");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SalesReturnLines_Percents",
                table: "SalesReturnLines",
                sql: "[Disc1Percent] BETWEEN 0 AND 100 AND [Disc2Percent] BETWEEN 0 AND 100 AND [Disc3Percent] BETWEEN 0 AND 100 AND [TaxPercent] BETWEEN 0 AND 100");
        }
    }
}
