using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class sync_BonusAndPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_ProductBonusGroup_ProductBonusGroupId",
                table: "Products");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ProductBonusGroup",
                table: "ProductBonusGroup");

         

            migrationBuilder.RenameTable(
                name: "ProductBonusGroup",
                newName: "ProductBonusGroups");

            migrationBuilder.AlterColumn<decimal>(
                name: "BonusAmount",
                table: "ProductBonusGroups",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProductBonusGroups",
                table: "ProductBonusGroups",
                column: "ProductBonusGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_ProductBonusGroups_ProductBonusGroupId",
                table: "Products",
                column: "ProductBonusGroupId",
                principalTable: "ProductBonusGroups",
                principalColumn: "ProductBonusGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_ProductBonusGroups_ProductBonusGroupId",
                table: "Products");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ProductBonusGroups",
                table: "ProductBonusGroups");

            migrationBuilder.RenameTable(
                name: "ProductBonusGroups",
                newName: "ProductBonusGroup");

            migrationBuilder.AlterColumn<decimal>(
                name: "BonusAmount",
                table: "ProductBonusGroup",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)",
                oldPrecision: 10,
                oldScale: 2);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ProductBonusGroup",
                table: "ProductBonusGroup",
                column: "ProductBonusGroupId");

          
            migrationBuilder.AddForeignKey(
                name: "FK_Products_ProductBonusGroup_ProductBonusGroupId",
                table: "Products",
                column: "ProductBonusGroupId",
                principalTable: "ProductBonusGroup",
                principalColumn: "ProductBonusGroupId");
        }
    }
}
