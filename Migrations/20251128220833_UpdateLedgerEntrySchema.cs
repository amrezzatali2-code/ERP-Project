using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class UpdateLedgerEntrySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_AccountId",
                table: "LedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_CustomerId",
                table: "LedgerEntries");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "LedgerEntries",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETDATE()");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "LedgerEntries",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_AccountId_EntryDate",
                table: "LedgerEntries",
                columns: new[] { "AccountId", "EntryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_CustomerId_EntryDate",
                table: "LedgerEntries",
                columns: new[] { "CustomerId", "EntryDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_SourceType_SourceId",
                table: "LedgerEntries",
                columns: new[] { "SourceType", "SourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_AccountId_EntryDate",
                table: "LedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_CustomerId_EntryDate",
                table: "LedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_LedgerEntries_SourceType_SourceId",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "LedgerEntries");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "LedgerEntries");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_AccountId",
                table: "LedgerEntries",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_CustomerId",
                table: "LedgerEntries",
                column: "CustomerId");
        }
    }
}
