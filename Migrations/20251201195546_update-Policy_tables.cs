using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class updatePolicy_tables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WarehousePolicyRules_PolicyId",
                table: "WarehousePolicyRules");

        

            migrationBuilder.AddColumn<decimal>(
                name: "MaxDiscountToCustomer",
                table: "ProductGroupPolicies",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ProfitPercent",
                table: "ProductGroupPolicies",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "ProductGroupPolicies",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "DefaultProfitPercent",
                table: "Policies",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_WarehousePolicyRules_PolicyId_WarehouseId",
                table: "WarehousePolicyRules",
                columns: new[] { "PolicyId", "WarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductGroupPolicies_ProductGroupId_PolicyId_WarehouseId",
                table: "ProductGroupPolicies",
                columns: new[] { "ProductGroupId", "PolicyId", "WarehouseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductGroupPolicies_WarehouseId",
                table: "ProductGroupPolicies",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductGroupPolicies_Warehouses_WarehouseId",
                table: "ProductGroupPolicies",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "WarehouseId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductGroupPolicies_Warehouses_WarehouseId",
                table: "ProductGroupPolicies");

            migrationBuilder.DropIndex(
                name: "IX_WarehousePolicyRules_PolicyId_WarehouseId",
                table: "WarehousePolicyRules");

            migrationBuilder.DropIndex(
                name: "IX_ProductGroupPolicies_ProductGroupId_PolicyId_WarehouseId",
                table: "ProductGroupPolicies");

            migrationBuilder.DropIndex(
                name: "IX_ProductGroupPolicies_WarehouseId",
                table: "ProductGroupPolicies");

            migrationBuilder.DropColumn(
                name: "MaxDiscountToCustomer",
                table: "ProductGroupPolicies");

            migrationBuilder.DropColumn(
                name: "ProfitPercent",
                table: "ProductGroupPolicies");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "ProductGroupPolicies");

            migrationBuilder.DropColumn(
                name: "DefaultProfitPercent",
                table: "Policies");

         

            migrationBuilder.CreateIndex(
                name: "IX_WarehousePolicyRules_PolicyId",
                table: "WarehousePolicyRules",
                column: "PolicyId");
        }
    }
}
