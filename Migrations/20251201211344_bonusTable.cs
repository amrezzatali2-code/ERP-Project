using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class bonusTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProductBonusGroupId",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductBonusGroup",
                columns: table => new
                {
                    ProductBonusGroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BonusAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductBonusGroup", x => x.ProductBonusGroupId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_ProductBonusGroupId",
                table: "Products",
                column: "ProductBonusGroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_ProductBonusGroup_ProductBonusGroupId",
                table: "Products",
                column: "ProductBonusGroupId",
                principalTable: "ProductBonusGroup",
                principalColumn: "ProductBonusGroupId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_ProductBonusGroup_ProductBonusGroupId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "ProductBonusGroup");

            migrationBuilder.DropIndex(
                name: "IX_Products_ProductBonusGroupId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ProductBonusGroupId",
                table: "Products");
        }
    }
}
