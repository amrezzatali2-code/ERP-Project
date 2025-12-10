using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddDateToCashReceiptTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProductProdId",
                table: "PurchaseReturnLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "PurchaseReturnPRetId",
                table: "PurchaseReturnLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "CashReceipts",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "CashReceipts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPosted",
                table: "CashReceipts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PostedAt",
                table: "CashReceipts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostedBy",
                table: "CashReceipts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "CashReceipts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLines_ProductProdId",
                table: "PurchaseReturnLines",
                column: "ProductProdId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLines_PurchaseReturnPRetId",
                table: "PurchaseReturnLines",
                column: "PurchaseReturnPRetId");

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseReturnLines_Products_ProductProdId",
                table: "PurchaseReturnLines",
                column: "ProductProdId",
                principalTable: "Products",
                principalColumn: "ProdId",
                onDelete: ReferentialAction.NoAction);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseReturnLines_PurchaseReturns_PurchaseReturnPRetId",
                table: "PurchaseReturnLines",
                column: "PurchaseReturnPRetId",
                principalTable: "PurchaseReturns",
                principalColumn: "PRetId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReturnLines_Products_ProductProdId",
                table: "PurchaseReturnLines");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReturnLines_PurchaseReturns_PurchaseReturnPRetId",
                table: "PurchaseReturnLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReturnLines_ProductProdId",
                table: "PurchaseReturnLines");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReturnLines_PurchaseReturnPRetId",
                table: "PurchaseReturnLines");

            migrationBuilder.DropColumn(
                name: "ProductProdId",
                table: "PurchaseReturnLines");

            migrationBuilder.DropColumn(
                name: "PurchaseReturnPRetId",
                table: "PurchaseReturnLines");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "CashReceipts");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "CashReceipts");

            migrationBuilder.DropColumn(
                name: "IsPosted",
                table: "CashReceipts");

            migrationBuilder.DropColumn(
                name: "PostedAt",
                table: "CashReceipts");

            migrationBuilder.DropColumn(
                name: "PostedBy",
                table: "CashReceipts");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "CashReceipts");
        }
    }
}
