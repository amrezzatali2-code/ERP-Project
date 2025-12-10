using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddCashVouchersAndNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashPayments",
                columns: table => new
                {
                    CashPaymentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    CashAccountId = table.Column<int>(type: "int", nullable: false),
                    CounterAccountId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashPayments", x => x.CashPaymentId);
                    table.ForeignKey(
                        name: "FK_CashPayments_Accounts_CashAccountId",
                        column: x => x.CashAccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashPayments_Accounts_CounterAccountId",
                        column: x => x.CounterAccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashPayments_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashReceipts",
                columns: table => new
                {
                    CashReceiptId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceiptNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReceiptDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    CashAccountId = table.Column<int>(type: "int", nullable: false),
                    CounterAccountId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashReceipts", x => x.CashReceiptId);
                    table.ForeignKey(
                        name: "FK_CashReceipts_Accounts_CashAccountId",
                        column: x => x.CashAccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashReceipts_Accounts_CounterAccountId",
                        column: x => x.CounterAccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashReceipts_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditNotes",
                columns: table => new
                {
                    CreditNoteId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NoteNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NoteDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    OffsetAccountId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditNotes", x => x.CreditNoteId);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Accounts_OffsetAccountId",
                        column: x => x.OffsetAccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditNotes_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DebitNotes",
                columns: table => new
                {
                    DebitNoteId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NoteNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    NoteDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    AccountId = table.Column<int>(type: "int", nullable: false),
                    OffsetAccountId = table.Column<int>(type: "int", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DebitNotes", x => x.DebitNoteId);
                    table.ForeignKey(
                        name: "FK_DebitNotes_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DebitNotes_Accounts_OffsetAccountId",
                        column: x => x.OffsetAccountId,
                        principalTable: "Accounts",
                        principalColumn: "AccountId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DebitNotes_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashPayments_CashAccountId",
                table: "CashPayments",
                column: "CashAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CashPayments_CounterAccountId",
                table: "CashPayments",
                column: "CounterAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CashPayments_CustomerId",
                table: "CashPayments",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CashReceipts_CashAccountId",
                table: "CashReceipts",
                column: "CashAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CashReceipts_CounterAccountId",
                table: "CashReceipts",
                column: "CounterAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CashReceipts_CustomerId",
                table: "CashReceipts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_AccountId",
                table: "CreditNotes",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_CustomerId",
                table: "CreditNotes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditNotes_OffsetAccountId",
                table: "CreditNotes",
                column: "OffsetAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DebitNotes_AccountId",
                table: "DebitNotes",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DebitNotes_CustomerId",
                table: "DebitNotes",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_DebitNotes_OffsetAccountId",
                table: "DebitNotes",
                column: "OffsetAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashPayments");

            migrationBuilder.DropTable(
                name: "CashReceipts");

            migrationBuilder.DropTable(
                name: "CreditNotes");

            migrationBuilder.DropTable(
                name: "DebitNotes");
        }
    }
}
