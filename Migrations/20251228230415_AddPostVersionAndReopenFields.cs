using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddPostVersionAndReopenFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PostVersion",
                table: "PurchaseInvoices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReopenedAt",
                table: "PurchaseInvoices",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReopenedBy",
                table: "PurchaseInvoices",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "WasReopened",
                table: "PurchaseInvoices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "PostVersion",
                table: "LedgerEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PostVersion",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "ReopenedAt",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "ReopenedBy",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "WasReopened",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "PostVersion",
                table: "LedgerEntries");
        }
    }
}
