using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackTraceFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTrackTraceEnabled",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TrackingCodeType",
                table: "Products",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EtaIntegrationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    UseSandbox = table.Column<bool>(type: "bit", nullable: false),
                    IdentityBaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ApiBaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ClientId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClientSecret = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TaxpayerRin = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CallbackBaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CertificateThumbprint = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EtaIntegrationSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EtaQueue",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    JsonData = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NextRetryAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EtaQueue", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EtaSubmissionLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceId = table.Column<int>(type: "int", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SubmissionUuid = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DocumentUuid = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EtaSubmissionLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemUnits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProdId = table.Column<int>(type: "int", nullable: false),
                    BatchId = table.Column<int>(type: "int", nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    Uid = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Gtin = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    SerialNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BatchNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Expiry = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CurrentSourceType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CurrentSourceId = table.Column<int>(type: "int", nullable: true),
                    CurrentSourceLineNo = table.Column<int>(type: "int", nullable: true),
                    IsTracked = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemUnits_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "BatchId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemUnits_Products_ProdId",
                        column: x => x.ProdId,
                        principalTable: "Products",
                        principalColumn: "ProdId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemUnits_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "WarehouseId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrackTraceIntegrationSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    UseSandbox = table.Column<bool>(type: "bit", nullable: false),
                    BaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PasswordOrToken = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ClientId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClientSecret = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CallbackBaseUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackTraceIntegrationSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseInvoiceLineUnits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PIId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ItemUnitId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseInvoiceLineUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLineUnits_ItemUnits_ItemUnitId",
                        column: x => x.ItemUnitId,
                        principalTable: "ItemUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseInvoiceLineUnits_PILines_PIId_LineNo",
                        columns: x => new { x.PIId, x.LineNo },
                        principalTable: "PILines",
                        principalColumns: new[] { "PIId", "LineNo" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReturnLineUnits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PRetId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ItemUnitId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReturnLineUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnLineUnits_ItemUnits_ItemUnitId",
                        column: x => x.ItemUnitId,
                        principalTable: "ItemUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnLineUnits_PurchaseReturnLines_PRetId_LineNo",
                        columns: x => new { x.PRetId, x.LineNo },
                        principalTable: "PurchaseReturnLines",
                        principalColumns: new[] { "PRetId", "LineNo" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceLineUnits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SIId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ItemUnitId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesInvoiceLineUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLineUnits_ItemUnits_ItemUnitId",
                        column: x => x.ItemUnitId,
                        principalTable: "ItemUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceLineUnits_SalesInvoiceLines_SIId_LineNo",
                        columns: x => new { x.SIId, x.LineNo },
                        principalTable: "SalesInvoiceLines",
                        principalColumns: new[] { "SIId", "LineNo" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesReturnLineUnits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SRId = table.Column<int>(type: "int", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ItemUnitId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesReturnLineUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesReturnLineUnits_ItemUnits_ItemUnitId",
                        column: x => x.ItemUnitId,
                        principalTable: "ItemUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReturnLineUnits_SalesReturnLines_SRId_LineNo",
                        columns: x => new { x.SRId, x.LineNo },
                        principalTable: "SalesReturnLines",
                        principalColumns: new[] { "SRId", "LineNo" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockAdjustmentLineUnits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockAdjustmentLineId = table.Column<int>(type: "int", nullable: false),
                    ItemUnitId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAdjustmentLineUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockAdjustmentLineUnits_ItemUnits_ItemUnitId",
                        column: x => x.ItemUnitId,
                        principalTable: "ItemUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockAdjustmentLineUnits_StockAdjustmentLines_StockAdjustmentLineId",
                        column: x => x.StockAdjustmentLineId,
                        principalTable: "StockAdjustmentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockTransferLineUnits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockTransferLineId = table.Column<int>(type: "int", nullable: false),
                    ItemUnitId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransferLineUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransferLineUnits_ItemUnits_ItemUnitId",
                        column: x => x.ItemUnitId,
                        principalTable: "ItemUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferLineUnits_StockTransferLines_StockTransferLineId",
                        column: x => x.StockTransferLineId,
                        principalTable: "StockTransferLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TrackTraceEventLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemUnitId = table.Column<long>(type: "bigint", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RequestJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackTraceEventLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackTraceEventLogs_ItemUnits_ItemUnitId",
                        column: x => x.ItemUnitId,
                        principalTable: "ItemUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TrackTraceQueue",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemUnitId = table.Column<long>(type: "bigint", nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    JsonData = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RetryCount = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NextRetryAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackTraceQueue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackTraceQueue_ItemUnits_ItemUnitId",
                        column: x => x.ItemUnitId,
                        principalTable: "ItemUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EtaQueue_SourceType_SourceId_DocumentType",
                table: "EtaQueue",
                columns: new[] { "SourceType", "SourceId", "DocumentType" });

            migrationBuilder.CreateIndex(
                name: "IX_EtaQueue_Status_NextRetryAt",
                table: "EtaQueue",
                columns: new[] { "Status", "NextRetryAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EtaSubmissionLogs_SourceType_SourceId_CreatedAt",
                table: "EtaSubmissionLogs",
                columns: new[] { "SourceType", "SourceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemUnits_BatchId_Status",
                table: "ItemUnits",
                columns: new[] { "BatchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemUnits_Gtin_SerialNo",
                table: "ItemUnits",
                columns: new[] { "Gtin", "SerialNo" },
                unique: true,
                filter: "[Gtin] IS NOT NULL AND [SerialNo] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ItemUnits_ProdId_WarehouseId_Status",
                table: "ItemUnits",
                columns: new[] { "ProdId", "WarehouseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemUnits_Uid",
                table: "ItemUnits",
                column: "Uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemUnits_WarehouseId",
                table: "ItemUnits",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLineUnits_ItemUnitId",
                table: "PurchaseInvoiceLineUnits",
                column: "ItemUnitId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceLineUnits_PIId_LineNo_ItemUnitId",
                table: "PurchaseInvoiceLineUnits",
                columns: new[] { "PIId", "LineNo", "ItemUnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLineUnits_ItemUnitId",
                table: "PurchaseReturnLineUnits",
                column: "ItemUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLineUnits_PRetId_LineNo_ItemUnitId",
                table: "PurchaseReturnLineUnits",
                columns: new[] { "PRetId", "LineNo", "ItemUnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLineUnits_ItemUnitId",
                table: "SalesInvoiceLineUnits",
                column: "ItemUnitId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceLineUnits_SIId_LineNo_ItemUnitId",
                table: "SalesInvoiceLineUnits",
                columns: new[] { "SIId", "LineNo", "ItemUnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnLineUnits_ItemUnitId",
                table: "SalesReturnLineUnits",
                column: "ItemUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnLineUnits_SRId_LineNo_ItemUnitId",
                table: "SalesReturnLineUnits",
                columns: new[] { "SRId", "LineNo", "ItemUnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustmentLineUnits_ItemUnitId",
                table: "StockAdjustmentLineUnits",
                column: "ItemUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustmentLineUnits_StockAdjustmentLineId_ItemUnitId",
                table: "StockAdjustmentLineUnits",
                columns: new[] { "StockAdjustmentLineId", "ItemUnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLineUnits_ItemUnitId",
                table: "StockTransferLineUnits",
                column: "ItemUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLineUnits_StockTransferLineId_ItemUnitId",
                table: "StockTransferLineUnits",
                columns: new[] { "StockTransferLineId", "ItemUnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackTraceEventLogs_ItemUnitId_CreatedAt",
                table: "TrackTraceEventLogs",
                columns: new[] { "ItemUnitId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackTraceQueue_ItemUnitId_EventType_Status",
                table: "TrackTraceQueue",
                columns: new[] { "ItemUnitId", "EventType", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackTraceQueue_Status_NextRetryAt",
                table: "TrackTraceQueue",
                columns: new[] { "Status", "NextRetryAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EtaIntegrationSettings");

            migrationBuilder.DropTable(
                name: "EtaQueue");

            migrationBuilder.DropTable(
                name: "EtaSubmissionLogs");

            migrationBuilder.DropTable(
                name: "PurchaseInvoiceLineUnits");

            migrationBuilder.DropTable(
                name: "PurchaseReturnLineUnits");

            migrationBuilder.DropTable(
                name: "SalesInvoiceLineUnits");

            migrationBuilder.DropTable(
                name: "SalesReturnLineUnits");

            migrationBuilder.DropTable(
                name: "StockAdjustmentLineUnits");

            migrationBuilder.DropTable(
                name: "StockTransferLineUnits");

            migrationBuilder.DropTable(
                name: "TrackTraceEventLogs");

            migrationBuilder.DropTable(
                name: "TrackTraceIntegrationSettings");

            migrationBuilder.DropTable(
                name: "TrackTraceQueue");

            migrationBuilder.DropTable(
                name: "ItemUnits");

            migrationBuilder.DropColumn(
                name: "IsTrackTraceEnabled",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "TrackingCodeType",
                table: "Products");
        }
    }
}
