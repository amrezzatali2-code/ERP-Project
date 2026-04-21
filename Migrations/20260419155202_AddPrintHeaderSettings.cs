using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ERP.Migrations
{
    /// <inheritdoc />
    public partial class AddPrintHeaderSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrintHeaderSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LogoPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintHeaderSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrintHeaderSettings");
        }
    }
}
