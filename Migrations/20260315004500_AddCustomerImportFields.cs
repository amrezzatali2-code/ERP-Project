using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerImportFields : Migration
    {
        /// <inheritdoc />
        /// <remarks>لا نعدّل جدول Products (WarehouseId و ExternalCode مضافان في هجرة AddExternalCodeAndWarehouseIdToProduct).</remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceRoutes_Employees_ControlEmployeeId",
                table: "SalesInvoiceRoutes");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceRoutes_Employees_DistributorEmployeeId",
                table: "SalesInvoiceRoutes");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceRoutes_Employees_PreparerEmployeeId",
                table: "SalesInvoiceRoutes");

            migrationBuilder.AddColumn<string>(
                name: "ExternalCode",
                table: "Customers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseNumber",
                table: "Customers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecordNumber",
                table: "Customers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegionName",
                table: "Customers",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Segment",
                table: "Customers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxIdOrNationalId",
                table: "Customers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceRoutes_Employees_ControlEmployeeId",
                table: "SalesInvoiceRoutes",
                column: "ControlEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceRoutes_Employees_DistributorEmployeeId",
                table: "SalesInvoiceRoutes",
                column: "DistributorEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceRoutes_Employees_PreparerEmployeeId",
                table: "SalesInvoiceRoutes",
                column: "PreparerEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceRoutes_Employees_ControlEmployeeId",
                table: "SalesInvoiceRoutes");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceRoutes_Employees_DistributorEmployeeId",
                table: "SalesInvoiceRoutes");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceRoutes_Employees_PreparerEmployeeId",
                table: "SalesInvoiceRoutes");

            migrationBuilder.DropColumn(
                name: "ExternalCode",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "LicenseNumber",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "RecordNumber",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "RegionName",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Segment",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TaxIdOrNationalId",
                table: "Customers");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceRoutes_Employees_ControlEmployeeId",
                table: "SalesInvoiceRoutes",
                column: "ControlEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceRoutes_Employees_DistributorEmployeeId",
                table: "SalesInvoiceRoutes",
                column: "DistributorEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceRoutes_Employees_PreparerEmployeeId",
                table: "SalesInvoiceRoutes",
                column: "PreparerEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
