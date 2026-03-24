using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddPurchasingModuleTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Areas_District_Name",
                table: "Areas");

            migrationBuilder.CreateTable(
                name: "PurchasePolicyRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    RuleType = table.Column<byte>(type: "tinyint", nullable: false),
                    CompareOp = table.Column<byte>(type: "tinyint", nullable: false),
                    DiffExact = table.Column<decimal>(type: "decimal(9,2)", nullable: true),
                    TargetPercent = table.Column<decimal>(type: "decimal(9,2)", nullable: true),
                    StockBelowPercent = table.Column<decimal>(type: "decimal(9,2)", nullable: true),
                    Tolerance = table.Column<decimal>(type: "decimal(9,2)", nullable: false),
                    Action = table.Column<byte>(type: "tinyint", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchasePolicyRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurchasingDataSourceConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchasingDataSourceConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurchasingOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    OrderDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AmendmentNotes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ErpPurchaseRequestId = table.Column<int>(type: "int", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchasingOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchasingOrders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchasingOrders_PurchaseRequests_ErpPurchaseRequestId",
                        column: x => x.ErpPurchaseRequestId,
                        principalTable: "PurchaseRequests",
                        principalColumn: "PRId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "VendorFaxUploads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ImportedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorFaxUploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendorFaxUploads_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VendorProductMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    VendorProductName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    PriceRetail = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    VendorProductCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Tag = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorProductMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendorProductMappings_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VendorProductMappings_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProdId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchasingOrderAmendments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchasingOrderId = table.Column<int>(type: "int", nullable: false),
                    AmendmentType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AmendmentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchasingOrderAmendments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchasingOrderAmendments_PurchasingOrders_PurchasingOrderId",
                        column: x => x.PurchasingOrderId,
                        principalTable: "PurchasingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchasingOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchasingOrderId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    VendorProductCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProductName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Qty = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    DiscountPct = table.Column<decimal>(type: "decimal(9,2)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchasingOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchasingOrderLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProdId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchasingOrderLines_PurchasingOrders_PurchasingOrderId",
                        column: x => x.PurchasingOrderId,
                        principalTable: "PurchasingOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VendorFaxLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VendorFaxUploadId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductNameFromVendor = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    VendorProductCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    DiscountPct = table.Column<decimal>(type: "decimal(9,2)", nullable: false),
                    MatchedProductId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VendorFaxLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VendorFaxLines_Products_MatchedProductId",
                        column: x => x.MatchedProductId,
                        principalTable: "Products",
                        principalColumn: "ProdId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VendorFaxLines_VendorFaxUploads_VendorFaxUploadId",
                        column: x => x.VendorFaxUploadId,
                        principalTable: "VendorFaxUploads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_Areas_District_Name",
                table: "Areas",
                columns: new[] { "DistrictId", "AreaName" },
                unique: true,
                filter: "[DistrictId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PurchasingOrderAmendments_PurchasingOrderId",
                table: "PurchasingOrderAmendments",
                column: "PurchasingOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchasingOrderLines_ProductId",
                table: "PurchasingOrderLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchasingOrderLines_PurchasingOrderId",
                table: "PurchasingOrderLines",
                column: "PurchasingOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchasingOrders_CustomerId",
                table: "PurchasingOrders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchasingOrders_ErpPurchaseRequestId",
                table: "PurchasingOrders",
                column: "ErpPurchaseRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorFaxLines_MatchedProductId",
                table: "VendorFaxLines",
                column: "MatchedProductId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorFaxLines_VendorFaxUploadId",
                table: "VendorFaxLines",
                column: "VendorFaxUploadId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorFaxUploads_CustomerId",
                table: "VendorFaxUploads",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_VendorProductMappings_CustomerId_VendorProductCode",
                table: "VendorProductMappings",
                columns: new[] { "CustomerId", "VendorProductCode" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorProductMappings_CustomerId_VendorProductName",
                table: "VendorProductMappings",
                columns: new[] { "CustomerId", "VendorProductName" });

            migrationBuilder.CreateIndex(
                name: "IX_VendorProductMappings_ProductId",
                table: "VendorProductMappings",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PurchasePolicyRules");

            migrationBuilder.DropTable(
                name: "PurchasingDataSourceConfigs");

            migrationBuilder.DropTable(
                name: "PurchasingOrderAmendments");

            migrationBuilder.DropTable(
                name: "PurchasingOrderLines");

            migrationBuilder.DropTable(
                name: "VendorFaxLines");

            migrationBuilder.DropTable(
                name: "VendorProductMappings");

            migrationBuilder.DropTable(
                name: "PurchasingOrders");

            migrationBuilder.DropTable(
                name: "VendorFaxUploads");

            migrationBuilder.DropIndex(
                name: "UX_Areas_District_Name",
                table: "Areas");

            migrationBuilder.CreateIndex(
                name: "UX_Areas_District_Name",
                table: "Areas",
                columns: new[] { "DistrictId", "AreaName" },
                unique: true);
        }
    }
}
