using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class tablesfromaccounttowarehouse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    AccountId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AccountName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    AccountType = table.Column<int>(type: "int", nullable: false),
                    ParentAccountId = table.Column<int>(type: "int", nullable: true),
                    Level = table.Column<int>(type: "int", nullable: false),
                    IsLeaf = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.AccountId);
                    table.ForeignKey(
                        name: "FK_Accounts_Accounts_ParentAccountId",
                        column: x => x.ParentAccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Batches",
                columns: table => new
                {
                    ProdId = table.Column<int>(type: "int", nullable: false),
                    BatchNo = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Expiry = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PriceRetailBatch = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    UnitCostDefault = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    EntryDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSDATETIME()"),
                    CustomerId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Batches", x => new { x.ProdId, x.BatchNo, x.Expiry });
                    table.CheckConstraint("CK_Batches_PriceRetailBatch", "[PriceRetailBatch] IS NULL OR [PriceRetailBatch] >= 0");
                    table.CheckConstraint("CK_Batches_UnitCostDefault", "[UnitCostDefault] IS NULL OR [UnitCostDefault] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Branches",
                columns: table => new
                {
                    BranchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BranchName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Branches", x => x.BranchId);
                });

            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    CategoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.CategoryId);
                });

            migrationBuilder.CreateTable(
                name: "DocumentSeries",
                columns: table => new
                {
                    SeriesId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SeriesCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FiscalYear = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: true),
                    ResetPolicy = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false, defaultValue: "Continuous"),
                    CurrentNo = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    NumberWidth = table.Column<int>(type: "int", nullable: false, defaultValue: 6),
                    Prefix = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSeries", x => x.SeriesId);
                });

            migrationBuilder.CreateTable(
                name: "Governorates",
                columns: table => new
                {
                    GovernorateId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GovernorateName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Governorates", x => x.GovernorateId);
                });

            migrationBuilder.CreateTable(
                name: "Stock_Batches",
                columns: table => new
                {
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ProdId = table.Column<int>(type: "int", nullable: false),
                    BatchNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Expiry = table.Column<DateTime>(type: "datetime2", nullable: false),
                    QtyOnHand = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stock_Batches", x => new { x.WarehouseId, x.ProdId, x.BatchNo, x.Expiry });
                    table.CheckConstraint("CK_StockBatches_Qty", "[QtyOnHand] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "StockFifoMap",
                columns: table => new
                {
                    MapId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OutEntryId = table.Column<int>(type: "int", nullable: false),
                    InEntryId = table.Column<int>(type: "int", nullable: false),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockFifoMap", x => x.MapId);
                    table.CheckConstraint("CK_Fifo_Qty_Positive", "[Qty] > 0");
                });

            migrationBuilder.CreateTable(
                name: "StockLedger",
                columns: table => new
                {
                    EntryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TranDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    ProdId = table.Column<int>(type: "int", nullable: false),
                    BatchNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Expiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    QtyIn = table.Column<int>(type: "int", nullable: false),
                    QtyOut = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    RemainingQty = table.Column<int>(type: "int", nullable: true),
                    SourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    SourceLine = table.Column<int>(type: "int", nullable: false),
                    MovementGroupId = table.Column<int>(type: "int", nullable: true),
                    CounterWarehouseId = table.Column<int>(type: "int", nullable: true),
                    AdjustmentReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    UserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockLedger", x => x.EntryId);
                    table.CheckConstraint("CK_Stock_Qty_Positive", "[QtyIn] >= 0 AND [QtyOut] >= 0 AND NOT ([QtyIn] > 0 AND [QtyOut] > 0)");
                    table.CheckConstraint("CK_Stock_Remaining_For_Inputs", "([QtyIn] = 0 AND [RemainingQty] IS NULL) OR ([QtyIn] > 0 AND [RemainingQty] >= 0)");
                    table.CheckConstraint("CK_Stock_UnitCost_NonNegative", "[UnitCost] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    WarehouseId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BranchId = table.Column<int>(type: "int", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.WarehouseId);
                    table.ForeignKey(
                        name: "FK_Warehouses_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "BranchId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    ProdId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProdName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Barcode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    GenericName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Strength = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CategoryId = table.Column<int>(type: "int", maxLength: 50, nullable: true),
                    PriceRetail = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DosageForm = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Imported = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true, defaultValue: "غير معروف"),
                    Company = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    LastPriceChangeDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.ProdId);
                    table.CheckConstraint("CK_Products_Imported", "[Imported] IN (N'محلي', N'مستورد', N'غير معروف')");
                    table.ForeignKey(
                        name: "FK_Products_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "CategoryId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Cities",
                columns: table => new
                {
                    CityId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CityName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    GovernorateId = table.Column<int>(type: "int", nullable: false),
                    CityType = table.Column<byte>(type: "tinyint", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Notes = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "GETDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true, defaultValueSql: "NULL")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cities", x => x.CityId);
                    table.ForeignKey(
                        name: "FK_Cities_Governorates_GovernorateId",
                        column: x => x.GovernorateId,
                        principalTable: "Governorates",
                        principalColumn: "GovernorateId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Districts",
                columns: table => new
                {
                    DistrictId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DistrictName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    GovernorateId = table.Column<int>(type: "int", nullable: false),
                    DistrictType = table.Column<byte>(type: "tinyint", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Notes = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Districts", x => x.DistrictId);
                    table.ForeignKey(
                        name: "FK_Districts_Governorates_GovernorateId",
                        column: x => x.GovernorateId,
                        principalTable: "Governorates",
                        principalColumn: "GovernorateId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductPriceHistory",
                columns: table => new
                {
                    PriceChangeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProdId = table.Column<int>(type: "int", nullable: false),
                    OldPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    NewPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ChangeDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ChangedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPriceHistory", x => x.PriceChangeId);
                    table.ForeignKey(
                        name: "FK_PriceHistory_Products_ProdId",
                        column: x => x.ProdId,
                        principalTable: "Products",
                        principalColumn: "ProdId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Areas",
                columns: table => new
                {
                    AreaId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AreaName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    GovernorateId = table.Column<int>(type: "int", nullable: false),
                    DistrictId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Notes = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Areas", x => x.AreaId);
                    table.ForeignKey(
                        name: "FK_Areas_Districts_DistrictId",
                        column: x => x.DistrictId,
                        principalTable: "Districts",
                        principalColumn: "DistrictId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Areas_Governorates_GovernorateId",
                        column: x => x.GovernorateId,
                        principalTable: "Governorates",
                        principalColumn: "GovernorateId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    CustomerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Phone1 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Phone2 = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Whatsapp = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    PartyCategory = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    GovernorateId = table.Column<int>(type: "int", nullable: true),
                    DistrictId = table.Column<int>(type: "int", nullable: true),
                    AreaId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreditLimit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CurrentBalance = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AccountId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.CustomerId);
                    table.ForeignKey(
                        name: "FK_Customers_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId");
                    table.ForeignKey(
                        name: "FK_Customers_Areas_AreaId",
                        column: x => x.AreaId,
                        principalTable: "Areas",
                        principalColumn: "AreaId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customers_Districts_DistrictId",
                        column: x => x.DistrictId,
                        principalTable: "Districts",
                        principalColumn: "DistrictId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customers_Governorates_GovernorateId",
                        column: x => x.GovernorateId,
                        principalTable: "Governorates",
                        principalColumn: "GovernorateId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseRequests",
                columns: table => new
                {
                    PRId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PRDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NeedByDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Draft"),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseRequests", x => x.PRId);
                    table.ForeignKey(
                        name: "FK_PurchaseRequests_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoices",
                columns: table => new
                {
                    SIId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SeriesCode = table.Column<int>(type: "int", maxLength: 10, nullable: true),
                    FiscalYear = table.Column<string>(type: "nchar(4)", fixedLength: true, maxLength: 4, nullable: true),
                    SIDate = table.Column<DateTime>(type: "date", nullable: false),
                    SITime = table.Column<TimeSpan>(type: "time(0)", nullable: false, defaultValueSql: "CONVERT(time(0), SYSDATETIME())"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    HeaderDiscountPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    HeaderDiscountValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    TotalBeforeDiscount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    TotalAfterDiscountBeforeTax = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    NetTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "مسودة"),
                    IsPosted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    PostedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "SYSDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoices", x => x.SIId);
                    table.CheckConstraint("CK_SalesInvoices_DiscountPercent", "[HeaderDiscountPercent] >= 0 AND [HeaderDiscountPercent] <= 100");
                    table.CheckConstraint("CK_SalesInvoices_PaymentMethod", "[PaymentMethod] IS NULL OR [PaymentMethod] IN (N'نقدي', N'شبكة', N'آجل', N'مختلط')");
                    table.CheckConstraint("CK_SalesInvoices_Status", "[Status] IN (N'مسودة', N'مرحل', N'ملغى')");
                    table.ForeignKey(
                        name: "FK_SalesInvoices_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrders",
                columns: table => new
                {
                    SOId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SODate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Open"),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrders", x => x.SOId);
                    table.ForeignKey(
                        name: "FK_SalesOrders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesReturns",
                columns: table => new
                {
                    SRId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SRDate = table.Column<DateTime>(type: "date", nullable: false),
                    SRTime = table.Column<TimeSpan>(type: "time(0)", nullable: false, defaultValueSql: "CONVERT(time(0), SYSDATETIME())"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", maxLength: 50, nullable: false),
                    HeaderDiscountPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false, defaultValue: 0m),
                    HeaderDiscountValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    TotalBeforeDiscount = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    TotalAfterDiscountBeforeTax = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    NetTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false, defaultValue: 0m),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Draft"),
                    IsPosted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    PostedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false, defaultValueSql: "SYSDATETIME()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(0)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesReturns", x => x.SRId);
                    table.CheckConstraint("CK_SalesReturns_DiscountPercent", "[HeaderDiscountPercent] >= 0 AND [HeaderDiscountPercent] <= 100");
                    table.CheckConstraint("CK_SalesReturns_Status", "[Status] IN ('Draft','Posted','Cancelled')");
                    table.ForeignKey(
                        name: "FK_SalesReturns_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PRLines",
                columns: table => new
                {
                    PRId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProdId = table.Column<int>(type: "int", nullable: false),
                    QtyRequested = table.Column<int>(type: "int", nullable: false),
                    PriceBasis = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    PriceRetail = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PurchaseDiscountPct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ExpectedCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PreferredBatchNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PreferredExpiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    QtyConverted = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PRLines", x => new { x.PRId, x.LineNo });
                    table.ForeignKey(
                        name: "FK_PRLines_PurchaseRequests_PRId",
                        column: x => x.PRId,
                        principalTable: "PurchaseRequests",
                        principalColumn: "PRId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoices",
                columns: table => new
                {
                    PIId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PIDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    RefPRId = table.Column<int>(type: "int", maxLength: 30, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Draft"),
                    IsPosted = table.Column<bool>(type: "bit", nullable: false),
                    PostedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoices", x => x.PIId);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoices_PurchaseRequests_RefPRId",
                        column: x => x.RefPRId,
                        principalTable: "PurchaseRequests",
                        principalColumn: "PRId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceLines",
                columns: table => new
                {
                    SIId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProdId = table.Column<int>(type: "int", nullable: false),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    PriceRetail = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Disc1Percent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    Disc2Percent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    Disc3Percent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    DiscountValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    UnitSalePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotalAfterDiscount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    TaxPercent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 0m),
                    TaxValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    LineNetTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    BatchNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Expiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    GroupNo = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceLines", x => new { x.SIId, x.LineNo });
                    table.CheckConstraint("CK_SIL_Disc1", "[Disc1Percent] >= 0 AND [Disc1Percent] <= 100");
                    table.CheckConstraint("CK_SIL_Disc2", "[Disc2Percent] >= 0 AND [Disc2Percent] <= 100");
                    table.CheckConstraint("CK_SIL_Disc3", "[Disc3Percent] >= 0 AND [Disc3Percent] <= 100");
                    table.CheckConstraint("CK_SIL_Qty_Positive", "[Qty] > 0");
                    table.CheckConstraint("CK_SIL_Tax", "[TaxPercent] >= 0 AND [TaxPercent] <= 100");
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLines_SalesInvoices_SIId",
                        column: x => x.SIId,
                        principalTable: "SalesInvoices",
                        principalColumn: "SIId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SOLines",
                columns: table => new
                {
                    SOId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProdId = table.Column<int>(type: "int", nullable: false),
                    QtyRequested = table.Column<int>(type: "int", nullable: false),
                    PriceBasis = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RequestedRetailPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SalesDiscountPct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ExpectedUnitPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PreferredBatchNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PreferredExpiry = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SOLines", x => new { x.SOId, x.LineNo });
                    table.ForeignKey(
                        name: "FK_SOLines_SalesOrders_SOId",
                        column: x => x.SOId,
                        principalTable: "SalesOrders",
                        principalColumn: "SOId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesReturnLines",
                columns: table => new
                {
                    SRId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProdId = table.Column<int>(type: "int", nullable: false),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    PriceRetail = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Disc1Percent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Disc2Percent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    Disc3Percent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    DiscountValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    UnitSalePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineTotalAfterDiscount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TaxValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    LineNetTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BatchNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Expiry = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesReturnLines", x => new { x.SRId, x.LineNo });
                    table.CheckConstraint("CK_SalesReturnLines_Percents", "[Disc1Percent] BETWEEN 0 AND 100 AND [Disc2Percent] BETWEEN 0 AND 100 AND [Disc3Percent] BETWEEN 0 AND 100 AND [TaxPercent] BETWEEN 0 AND 100");
                    table.CheckConstraint("CK_SalesReturnLines_Qty", "[Qty] >= 0");
                    table.ForeignKey(
                        name: "FK_SalesReturnLines_SalesReturns_SRId",
                        column: x => x.SRId,
                        principalTable: "SalesReturns",
                        principalColumn: "SRId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PILines",
                columns: table => new
                {
                    PIId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProdId = table.Column<int>(type: "int", nullable: false),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PurchaseDiscountPct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    PriceRetail = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BatchNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Expiry = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PILines", x => new { x.PIId, x.LineNo });
                    table.ForeignKey(
                        name: "FK_PILines_PurchaseInvoices_PIId",
                        column: x => x.PIId,
                        principalTable: "PurchaseInvoices",
                        principalColumn: "PIId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReturns",
                columns: table => new
                {
                    PRetId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PRetDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    RefPIId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Draft"),
                    IsPosted = table.Column<bool>(type: "bit", nullable: false),
                    PostedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReturns", x => x.PRetId);
                    table.ForeignKey(
                        name: "FK_PurchaseReturns_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturns_PurchaseInvoices_RefPIId",
                        column: x => x.RefPIId,
                        principalTable: "PurchaseInvoices",
                        principalColumn: "PIId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReturnLines",
                columns: table => new
                {
                    PRetId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProdId = table.Column<int>(type: "int", nullable: false),
                    Qty = table.Column<int>(type: "int", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PurchaseDiscountPct = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    PriceRetail = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BatchNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Expiry = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReturnLines", x => new { x.PRetId, x.LineNo });
                    table.ForeignKey(
                        name: "FK_PurchaseReturnLines_PurchaseReturns_PRetId",
                        column: x => x.PRetId,
                        principalTable: "PurchaseReturns",
                        principalColumn: "PRetId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_AccountCode",
                table: "Accounts",
                column: "AccountCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_ParentAccountId",
                table: "Accounts",
                column: "ParentAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Areas_DistrictId",
                table: "Areas",
                column: "DistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_Areas_GovernorateId",
                table: "Areas",
                column: "GovernorateId");

            migrationBuilder.CreateIndex(
                name: "UX_Areas_District_Name",
                table: "Areas",
                columns: new[] { "DistrictId", "AreaName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ProdId",
                table: "Batches",
                column: "ProdId");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_ProdId_Expiry",
                table: "Batches",
                columns: new[] { "ProdId", "Expiry" });

            migrationBuilder.CreateIndex(
                name: "IX_Cities_GovernorateId",
                table: "Cities",
                column: "GovernorateId");

            migrationBuilder.CreateIndex(
                name: "UX_Cities_GovernorateId_CityName",
                table: "Cities",
                columns: new[] { "GovernorateId", "CityName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_AccountId",
                table: "Customers",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_AreaId",
                table: "Customers",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_DistrictId",
                table: "Customers",
                column: "DistrictId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Name",
                table: "Customers",
                column: "CustomerName");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Phone1",
                table: "Customers",
                column: "Phone1");

            migrationBuilder.CreateIndex(
                name: "UX_Customers_UniqueNameInArea",
                table: "Customers",
                columns: new[] { "GovernorateId", "DistrictId", "AreaId", "CustomerName" },
                unique: true,
                filter: "[GovernorateId] IS NOT NULL AND [DistrictId] IS NOT NULL AND [AreaId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Districts_GovernorateId",
                table: "Districts",
                column: "GovernorateId");

            migrationBuilder.CreateIndex(
                name: "UX_Districts_Gov_Name",
                table: "Districts",
                columns: new[] { "GovernorateId", "DistrictName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_DocumentSeries_DocType_Series_Year",
                table: "DocumentSeries",
                columns: new[] { "DocType", "SeriesCode", "FiscalYear" },
                unique: true,
                filter: "[FiscalYear] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Governorates_Name",
                table: "Governorates",
                column: "GovernorateName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PILines_ProdId",
                table: "PILines",
                column: "ProdId");

            migrationBuilder.CreateIndex(
                name: "IX_PILines_ProdId_BatchNo_Expiry",
                table: "PILines",
                columns: new[] { "ProdId", "BatchNo", "Expiry" });

            migrationBuilder.CreateIndex(
                name: "IX_PRLines_ProdId",
                table: "PRLines",
                column: "ProdId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPriceHistory_ProdId_ChangeDate",
                table: "ProductPriceHistory",
                columns: new[] { "ProdId", "ChangeDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId",
                table: "Products",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_CustomerId",
                table: "PurchaseInvoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_RefPRId",
                table: "PurchaseInvoices",
                column: "RefPRId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseRequests_CustomerId",
                table: "PurchaseRequests",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLines_ProdId",
                table: "PurchaseReturnLines",
                column: "ProdId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLines_ProdId_BatchNo_Expiry",
                table: "PurchaseReturnLines",
                columns: new[] { "ProdId", "BatchNo", "Expiry" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_CustomerId",
                table: "PurchaseReturns",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_RefPIId",
                table: "PurchaseReturns",
                column: "RefPIId");

            migrationBuilder.CreateIndex(
                name: "IX_SIL_ProdId",
                table: "SalesInvoiceLines",
                column: "ProdId");

            migrationBuilder.CreateIndex(
                name: "IX_SIL_SIId",
                table: "SalesInvoiceLines",
                column: "SIId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_Customer_Date",
                table: "SalesInvoices",
                columns: new[] { "CustomerId", "SIDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_Date_Id",
                table: "SalesInvoices",
                columns: new[] { "SIDate", "SIId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_Warehouse_Date",
                table: "SalesInvoices",
                columns: new[] { "WarehouseId", "SIDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_CustomerId",
                table: "SalesOrders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnLines_ProdId",
                table: "SalesReturnLines",
                column: "ProdId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_CustomerId_SRDate",
                table: "SalesReturns",
                columns: new[] { "CustomerId", "SRDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_SRDate",
                table: "SalesReturns",
                column: "SRDate");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_WarehouseId_SRDate",
                table: "SalesReturns",
                columns: new[] { "WarehouseId", "SRDate" });

            migrationBuilder.CreateIndex(
                name: "IX_SOL_ProdId",
                table: "SOLines",
                column: "ProdId");

            migrationBuilder.CreateIndex(
                name: "IX_SOL_SOId",
                table: "SOLines",
                column: "SOId");

            migrationBuilder.CreateIndex(
                name: "IX_Stock_Batches_ProdId_Expiry",
                table: "Stock_Batches",
                columns: new[] { "ProdId", "Expiry" });

            migrationBuilder.CreateIndex(
                name: "IX_Stock_Batches_QtyOnHand",
                table: "Stock_Batches",
                column: "QtyOnHand");

            migrationBuilder.CreateIndex(
                name: "IX_StockFifoMap_InEntryId",
                table: "StockFifoMap",
                column: "InEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_StockFifoMap_OutEntryId",
                table: "StockFifoMap",
                column: "OutEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_Fifo",
                table: "StockLedger",
                columns: new[] { "WarehouseId", "ProdId", "Expiry", "TranDate", "EntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_SourceType_SourceId_SourceLine",
                table: "StockLedger",
                columns: new[] { "SourceType", "SourceId", "SourceLine" });

            migrationBuilder.CreateIndex(
                name: "IX_StockLedger_WarehouseId_ProdId",
                table: "StockLedger",
                columns: new[] { "WarehouseId", "ProdId" });

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_BranchId",
                table: "Warehouses",
                column: "BranchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Batches");

            migrationBuilder.DropTable(
                name: "Cities");

            migrationBuilder.DropTable(
                name: "DocumentSeries");

            migrationBuilder.DropTable(
                name: "PILines");

            migrationBuilder.DropTable(
                name: "PRLines");

            migrationBuilder.DropTable(
                name: "ProductPriceHistory");

            migrationBuilder.DropTable(
                name: "PurchaseReturnLines");

            migrationBuilder.DropTable(
                name: "SalesInvoiceLines");

            migrationBuilder.DropTable(
                name: "SalesReturnLines");

            migrationBuilder.DropTable(
                name: "SOLines");

            migrationBuilder.DropTable(
                name: "Stock_Batches");

            migrationBuilder.DropTable(
                name: "StockFifoMap");

            migrationBuilder.DropTable(
                name: "StockLedger");

            migrationBuilder.DropTable(
                name: "Warehouses");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "PurchaseReturns");

            migrationBuilder.DropTable(
                name: "SalesInvoices");

            migrationBuilder.DropTable(
                name: "SalesReturns");

            migrationBuilder.DropTable(
                name: "SalesOrders");

            migrationBuilder.DropTable(
                name: "Branches");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "PurchaseInvoices");

            migrationBuilder.DropTable(
                name: "PurchaseRequests");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "Areas");

            migrationBuilder.DropTable(
                name: "Districts");

            migrationBuilder.DropTable(
                name: "Governorates");
        }
    }
}
