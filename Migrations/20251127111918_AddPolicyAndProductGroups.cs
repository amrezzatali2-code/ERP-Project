using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddPolicyAndProductGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProductGroupId",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PolicyId",
                table: "Customers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Policies",
                columns: table => new
                {
                    PolicyId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Policies", x => x.PolicyId);
                });

            migrationBuilder.CreateTable(
                name: "ProductGroups",
                columns: table => new
                {
                    ProductGroupId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductGroups", x => x.ProductGroupId);
                });

            migrationBuilder.CreateTable(
                name: "WarehousePolicyRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    PolicyId = table.Column<int>(type: "int", nullable: false),
                    ProfitPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    MaxDiscountToCustomer = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WarehousePolicyRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WarehousePolicyRules_Policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policies",
                        principalColumn: "PolicyId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WarehousePolicyRules_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "WarehouseId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductGroupPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductGroupId = table.Column<int>(type: "int", nullable: false),
                    PolicyId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductGroupPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductGroupPolicies_Policies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "Policies",
                        principalColumn: "PolicyId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductGroupPolicies_ProductGroups_ProductGroupId",
                        column: x => x.ProductGroupId,
                        principalTable: "ProductGroups",
                        principalColumn: "ProductGroupId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_ProductGroupId",
                table: "Products",
                column: "ProductGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_PolicyId",
                table: "Customers",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_Policies_Name",
                table: "Policies",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductGroupPolicies_PolicyId",
                table: "ProductGroupPolicies",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductGroupPolicies_ProductGroupId",
                table: "ProductGroupPolicies",
                column: "ProductGroupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductGroups_Name",
                table: "ProductGroups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WarehousePolicyRules_PolicyId",
                table: "WarehousePolicyRules",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_WarehousePolicyRules_WarehouseId_PolicyId",
                table: "WarehousePolicyRules",
                columns: new[] { "WarehouseId", "PolicyId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Policies_PolicyId",
                table: "Customers",
                column: "PolicyId",
                principalTable: "Policies",
                principalColumn: "PolicyId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_ProductGroups_ProductGroupId",
                table: "Products",
                column: "ProductGroupId",
                principalTable: "ProductGroups",
                principalColumn: "ProductGroupId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Policies_PolicyId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_ProductGroups_ProductGroupId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "ProductGroupPolicies");

            migrationBuilder.DropTable(
                name: "WarehousePolicyRules");

            migrationBuilder.DropTable(
                name: "ProductGroups");

            migrationBuilder.DropTable(
                name: "Policies");

            migrationBuilder.DropIndex(
                name: "IX_Products_ProductGroupId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Customers_PolicyId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "ProductGroupId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PolicyId",
                table: "Customers");
        }
    }
}
