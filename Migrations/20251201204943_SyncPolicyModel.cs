using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class SyncPolicyModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Policies",
                type: "bit",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "bit");

            migrationBuilder.AlterColumn<decimal>(
                name: "DefaultProfitPercent",
                table: "Policies",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)",
                oldPrecision: 5,
                oldScale: 2);

            migrationBuilder.InsertData(
                table: "Policies",
                columns: new[] { "PolicyId", "CreatedAt", "Description", "IsActive", "Name", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 1", true, "Policy 1", null },
                    { 2, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 2", true, "Policy 2", null },
                    { 3, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 3", true, "Policy 3", null },
                    { 4, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 4", true, "Policy 4", null },
                    { 5, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 5", true, "Policy 5", null },
                    { 6, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 6", true, "Policy 6", null },
                    { 7, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 7", true, "Policy 7", null },
                    { 8, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 8", true, "Policy 8", null },
                    { 9, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 9", true, "Policy 9", null },
                    { 10, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 10", true, "Policy 10", null },
                    { 11, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 11", true, "Policy 11", null },
                    { 12, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 12", true, "Policy 12", null },
                    { 13, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 13", true, "Policy 13", null },
                    { 14, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 14", true, "Policy 14", null },
                    { 15, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 15", true, "Policy 15", null },
                    { 16, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 16", true, "Policy 16", null },
                    { 17, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 17", true, "Policy 17", null },
                    { 18, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 18", true, "Policy 18", null },
                    { 19, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 19", true, "Policy 19", null },
                    { 20, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 20", true, "Policy 20", null },
                    { 21, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 21", true, "Policy 21", null },
                    { 22, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 22", true, "Policy 22", null },
                    { 23, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 23", true, "Policy 23", null },
                    { 24, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 24", true, "Policy 24", null },
                    { 25, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 25", true, "Policy 25", null },
                    { 26, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 26", true, "Policy 26", null },
                    { 27, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 27", true, "Policy 27", null },
                    { 28, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 28", true, "Policy 28", null },
                    { 29, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 29", true, "Policy 29", null },
                    { 30, new DateTime(2025, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "سياسة افتراضية رقم 30", true, "Policy 30", null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 17);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 18);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 19);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 20);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 21);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 22);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 23);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 24);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 25);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 26);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 27);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 28);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 29);

            migrationBuilder.DeleteData(
                table: "Policies",
                keyColumn: "PolicyId",
                keyValue: 30);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "Policies",
                type: "bit",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "bit",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "DefaultProfitPercent",
                table: "Policies",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(5,2)",
                oldPrecision: 5,
                oldScale: 2,
                oldDefaultValue: 0m);
        }
    }
}
