using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class RemoveProductGroupIdUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // إزالة الفهرس الفريد على ProductGroupId فقط (يمنع نفس المجموعة لمخازن مختلفة)
            // معالجة الاسمين المحتملين: ProductGroupId و ProductGroupld (خطأ إملائي قديم)
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProductGroupPolicies_ProductGroupId' AND object_id = OBJECT_ID('dbo.ProductGroupPolicies'))
                    DROP INDEX IX_ProductGroupPolicies_ProductGroupId ON dbo.ProductGroupPolicies;
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ProductGroupPolicies_ProductGroupld' AND object_id = OBJECT_ID('dbo.ProductGroupPolicies'))
                    DROP INDEX IX_ProductGroupPolicies_ProductGroupld ON dbo.ProductGroupPolicies;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ProductGroupPolicies_ProductGroupId",
                table: "ProductGroupPolicies",
                column: "ProductGroupId",
                unique: true);
        }
    }
}
